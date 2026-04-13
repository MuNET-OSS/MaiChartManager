using NAudio.Lame;
using Standart.Hash.xxHash;
using Xabe.FFmpeg;

namespace MaiChartManager.Utils;

public static class AudioConvert
{
    private static IEnumerable<int> GetDistinctAudioIds(IEnumerable<int> musicIds)
    {
        var seen = new HashSet<int>();
        foreach (var rawId in musicIds)
        {
            var musicId = (int)(Math.Abs((long)rawId) % 10000);
            if (seen.Add(musicId))
            {
                yield return musicId;
            }
        }
    }

    public static bool TryResolveAcbAwb(IEnumerable<int> musicIds, out int resolvedMusicId, out string? acbPath, out string? awbPath)
    {
        foreach (var musicId in GetDistinctAudioIds(musicIds))
        {
            var acbKey = $"music{musicId:000000}.acb";
            var awbKey = $"music{musicId:000000}.awb";
            if (!StaticSettings.AcbAwb.TryGetValue(acbKey, out var acb) || string.IsNullOrEmpty(acb))
            {
                continue;
            }

            if (!StaticSettings.AcbAwb.TryGetValue(awbKey, out var awb) || string.IsNullOrEmpty(awb))
            {
                continue;
            }

            resolvedMusicId = musicId;
            acbPath = acb;
            awbPath = awb;
            return true;
        }

        resolvedMusicId = 0;
        acbPath = null;
        awbPath = null;
        return false;
    }

    public static async Task<string?> GetCachedWavPath(params int[] musicIds)
    {
        if (!TryResolveAcbAwb(musicIds, out _, out var acbPath, out var awbPath) || acbPath is null || awbPath is null)
        {
            return null;
        }

        return await GetCachedWavPath(acbPath, awbPath);
    }

    public static async Task<string> GetCachedWavPath(string acbPath, string awbPath)
    {
        string hash;
        await using (var readStream = File.OpenRead(awbPath))
        {
            hash = (await xxHash64.ComputeHashAsync(readStream)).ToString();
        }

        var cachePath = Path.Combine(StaticSettings.tempPath, hash + ".wav");
        if (File.Exists(cachePath)) return cachePath;

        var wav = Audio.AcbToWav(acbPath);
        await File.WriteAllBytesAsync(cachePath, wav);
        return cachePath;
    }

    public static void ConvertWavToMp3Stream(byte[] wav, Stream mp3Stream, ID3TagData? tagData = null)
    {
        var tempFileGuid = Guid.NewGuid();
        var inputPath = Path.Combine(StaticSettings.tempPath, $"ConvertToMp3_{tempFileGuid:N}.wav");
        var outputPath = Path.Combine(StaticSettings.tempPath, $"ConvertToMp3_{tempFileGuid:N}.mp3");
        string? albumArtPath = null;
        try
        {
            Directory.CreateDirectory(StaticSettings.tempPath);
            File.WriteAllBytes(inputPath, wav);

            var conversion = FFmpeg.Conversions.New()
                .AddParameter($"-i " + FFmpegHelper.Escape(inputPath));

            if (tagData != null)
            {
                if (tagData.AlbumArt != null && tagData.AlbumArt.Length > 0)
                {
                    // 把专辑封面写到临时文件，然后让ffmpeg把它嵌入mp3
                    albumArtPath = Path.Combine(StaticSettings.tempPath, $"ConvertToMp3_{tempFileGuid:N}.png");
                    File.WriteAllBytes(albumArtPath, tagData.AlbumArt);
                    conversion.AddParameter($"-i {FFmpegHelper.Escape(albumArtPath)}");
                } // 顺序不能换！这个必须在第一个，因为-i必须在任何其他参数之前。
                if (!string.IsNullOrEmpty(tagData.Title)) conversion.AddParameter($"-metadata title=" + FFmpegHelper.Escape(tagData.Title));
                if (!string.IsNullOrEmpty(tagData.Artist)) conversion.AddParameter($"-metadata artist=" + FFmpegHelper.Escape(tagData.Artist));
                if (!string.IsNullOrEmpty(tagData.Album)) conversion.AddParameter($"-metadata album=" + FFmpegHelper.Escape(tagData.Album));
                if (!string.IsNullOrEmpty(tagData.Year)) conversion.AddParameter($"-metadata date=" + FFmpegHelper.Escape(tagData.Year));
                if (!string.IsNullOrEmpty(tagData.Comment)) conversion.AddParameter($"-metadata comment=" + FFmpegHelper.Escape(tagData.Comment));
                if (!string.IsNullOrEmpty(tagData.Genre)) conversion.AddParameter($"-metadata genre=" + FFmpegHelper.Escape(tagData.Genre));
                if (!string.IsNullOrEmpty(tagData.Track)) conversion.AddParameter($"-metadata track=" + FFmpegHelper.Escape(tagData.Track));
            }
            
            conversion.AddParameter("-c:a libmp3lame -b:a 256k"); // 把wav编码为256kbps的LAME mp3

            if (albumArtPath != null)
            { // 如果有专辑封面，还需要加一堆参数以写入专辑封面
                conversion.AddParameter("-map 0:a -map 1:v -c:v copy -disposition:v attached_pic");
            }
            
            conversion.SetOutput(outputPath).SetOverwriteOutput(true);
            conversion.Start().GetAwaiter().GetResult();

            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            {
                throw new InvalidOperationException("ffmpeg produced empty mp3 file from wav input.");
            }

            using var outputFile = new FileStream(outputPath, FileMode.Open, FileAccess.Read);
            outputFile.CopyTo(mp3Stream);
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
            if (albumArtPath != null) File.Delete(albumArtPath);
        }
    }
}
