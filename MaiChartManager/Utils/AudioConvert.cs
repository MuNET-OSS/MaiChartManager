using NAudio.Lame;
using Standart.Hash.xxHash;
using FFMpegCore;

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

            // 输出参数（PostInput）：按原 Xabe 调用顺序拼装，保持 ffmpeg 命令行等价。
            // FFMpegCore 用 ArgumentList 传递，不会像 Xabe 那样产生引号问题；
            // 但 -metadata 的值可能含空格，这里仍用 Escape() 把值包成带引号的单个 token，
            // 与原先 FFmpegHelper.Escape 行为一致。
            var output = new List<string>();

            if (tagData != null)
            {
                // 注意：第二个输入（专辑封面）由 AddFileInput 处理，必须在所有输出参数之前，
                // 对应原注释“-i 必须在任何其他参数之前”。
                if (!string.IsNullOrEmpty(tagData.Title)) output.Add("-metadata title=" + Escape(tagData.Title));
                if (!string.IsNullOrEmpty(tagData.Artist)) output.Add("-metadata artist=" + Escape(tagData.Artist));
                if (!string.IsNullOrEmpty(tagData.Album)) output.Add("-metadata album=" + Escape(tagData.Album));
                if (!string.IsNullOrEmpty(tagData.Year)) output.Add("-metadata date=" + Escape(tagData.Year));
                if (!string.IsNullOrEmpty(tagData.Comment)) output.Add("-metadata comment=" + Escape(tagData.Comment));
                if (!string.IsNullOrEmpty(tagData.Genre)) output.Add("-metadata genre=" + Escape(tagData.Genre));
                if (!string.IsNullOrEmpty(tagData.Track)) output.Add("-metadata track=" + Escape(tagData.Track));
            }

            output.Add("-c:a libmp3lame -b:a 256k"); // 把wav编码为256kbps的LAME mp3

            if (tagData?.AlbumArt is { Length: > 0 })
            {
                // 把专辑封面写到临时文件，然后让ffmpeg把它嵌入mp3
                albumArtPath = Path.Combine(StaticSettings.tempPath, $"ConvertToMp3_{tempFileGuid:N}.png");
                File.WriteAllBytes(albumArtPath, tagData.AlbumArt);
                // 如果有专辑封面，还需要加一堆参数以写入专辑封面
                output.Add("-map 0:a -map 1:v -c:v copy -disposition:v attached_pic");
            }

            var args = FFMpegArguments.FromFileInput(inputPath, verifyExists: false);
            if (albumArtPath != null)
            {
                args = args.AddFileInput(albumArtPath, verifyExists: false);
            }

            args
                .OutputToFile(outputPath, overwrite: true, o => o.WithCustomArgument(string.Join(" ", output)))
                .ProcessSynchronously();

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

    /// <summary>
    /// 将字符串转义后用双引号包裹，作为单个 ffmpeg 参数 token。
    /// 等价于原 FFmpegHelper.Escape：正确转义内容中的反斜杠和双引号。
    /// 用于 -metadata 值，避免含空格的值被拆成多个参数。
    /// </summary>
    private static string Escape(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
