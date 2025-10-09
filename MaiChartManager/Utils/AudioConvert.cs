using NAudio.Lame;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Sitreamai;
using Standart.Hash.xxHash;

namespace MaiChartManager.Utils;

public static class AudioConvert
{
    public static async Task<string?> GetCachedWavPath(int musicId)
    {
        var awb = StaticSettings.AcbAwb.GetValueOrDefault($"music{(musicId % 10000):000000}.awb");
        if (awb is null)
        {
            return null;
        }

        string hash;
        await using (var readStream = File.OpenRead(awb))
        {
            hash = (await xxHash64.ComputeHashAsync(readStream)).ToString();
        }

        var cachePath = Path.Combine(StaticSettings.tempPath, hash + ".wav");
        if (File.Exists(cachePath)) return cachePath;

        var wav = Audio.AcbToWav(StaticSettings.AcbAwb[$"music{(musicId % 10000):000000}.acb"]);
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