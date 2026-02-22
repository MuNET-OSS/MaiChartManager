using NAudio.Lame;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Standart.Hash.xxHash;

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

    public static void ConvertWavPathToMp3Stream(string wavPath, Stream mp3Stream, ID3TagData? tagData = null)
    {
        using var reader = new WaveFileReader(wavPath);
        using var writer = new LameMP3FileWriter(mp3Stream, reader.WaveFormat, 256, tagData);
        reader.CopyTo(writer);
    }
}
