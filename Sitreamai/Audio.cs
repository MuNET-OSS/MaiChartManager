using System;
using System.IO;
using System.Reflection;
using NAudio.Lame;
using NAudio.Wave;
using VGAudio;
using VGAudio.Cli;
using Xv2CoreLib.ACB;

namespace Sitreamai;

public static class Audio
{
    public static void ConvertToMai(string srcPath, string savePath, float padding = 0, Stream src = null, string previewFilename = null, Stream preview = null)
    {
        var wrapper = new ACB_Wrapper(ACB_File.Load(ReadResourceFile(previewFilename is null ? "Sitreamai.Resources.nopreview.acb" : "Sitreamai.Resources.template.acb"), null));
        var trackBytes = LoadAndConvertFile(srcPath, FileType.Hca, false, 9170825592834449000, padding, src);

        wrapper.Cues[0].AddTrackToCue(trackBytes, true, false, EncodeType.HCA);
        if (previewFilename is not null)
        {
            var previewTrackBytes = LoadAndConvertFile(previewFilename, FileType.Hca, true, 9170825592834449000, 0, preview);
            wrapper.Cues[1].AddTrackToCue(previewTrackBytes, true, false, EncodeType.HCA);
        }

        wrapper.AcbFile.Save(savePath);
    }

    private static byte[] ReadResourceFile(string filename)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(filename);
        var buffer = new byte[1024];
        using var ms = new MemoryStream();
        while (true)
        {
            var read = s.Read(buffer, 0, buffer.Length);
            if (read <= 0)
                return ms.ToArray();
            ms.Write(buffer, 0, read);
        }
    }

    // 不要 byte[] 转 memory stream 倒来倒去，直接传入 stream
    public static byte[] LoadAndConvertFile(string path, FileType convertToType, bool loop, ulong encrpytionKey = 0, float padding = 0, Stream src = null)
    {
        using var read = src ?? File.OpenRead(path);
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".wav":
            case ".mp3":
            case ".ogg":
            case ".wma":
            case ".aac":
                return ConvertFile(ConvertToWav(read, Path.GetExtension(path).Equals(".ogg", StringComparison.InvariantCultureIgnoreCase), padding), FileType.Wave, convertToType, loop, encrpytionKey);
            case ".hca":
                return ConvertFile(read, FileType.Hca, convertToType, loop, encrpytionKey);
            case ".adx":
                if (convertToType == FileType.Adx)
                {
                    var ms = new MemoryStream();
                    read.CopyTo(ms);
                    return ms.ToArray();
                }

                return ConvertFile(read, FileType.Adx, convertToType, loop, encrpytionKey);
            case ".at9":
                return ConvertFile(read, FileType.Atrac9, convertToType, loop, encrpytionKey);
            case ".dsp":
                return ConvertFile(read, FileType.Dsp, convertToType, loop, encrpytionKey);
            case ".bcwav":
                return ConvertFile(read, FileType.Bcwav, convertToType, loop, encrpytionKey);
        }

        throw new InvalidDataException($"Filetype of \"{path}\" is not supported.");
    }

    public static Stream ConvertToWav(Stream src, bool isOgg, float padding = 0)
    {
        using WaveStream reader = isOgg ? new NAudio.Vorbis.VorbisWaveReader(src, true) : new StreamMediaFoundationReader(src);
        var sample = reader.ToSampleProvider();

        switch (padding)
        {
            case > 0:
            {
                var sp = new SilenceProvider(reader.WaveFormat);
                var silence = sp.ToSampleProvider().Take(TimeSpan.FromSeconds(padding));
                sample = silence.FollowedBy(sample);
                break;
            }
            case < 0:
                sample = sample.Skip(TimeSpan.FromSeconds(-padding));
                break;
        }

        var stream = new MemoryStream();
        WaveFileWriter.WriteWavFileToStream(stream, sample.ToWaveProvider16()); // 淦
        stream.Position = 0;                                                    // 淦 x2
        return stream;
    }

    public static byte[] ConvertFile(
        Stream s,
        FileType encodeType,
        FileType convertToType,
        bool loop,
        ulong encryptionKey = 0)
    {
        ConvertStatics.SetLoop(loop, 0, 0);

        var options = new Options
        {
            KeyCode = encryptionKey,
            Loop = loop
        };

        if (options.Loop)
            options.LoopEnd = int.MaxValue;

        byte[] track = ConvertStream.ConvertFile(options, s, encodeType, convertToType);

        //if (convertToType == FileType.Hca && loop)
        //    track = HCA.EncodeLoop(track, loop);

        return track;
    }

    private static FileType GetFileType(EncodeType encodeType)
    {
        switch (encodeType)
        {
            case EncodeType.HCA:
            case EncodeType.HCA_ALT:
                return FileType.Hca;
            case EncodeType.ADX:
                return FileType.Adx;
            case EncodeType.ATRAC9:
                return FileType.Atrac9;
            case EncodeType.DSP:
                return FileType.Dsp;
            case EncodeType.BCWAV:
                return FileType.Bcwav;
            default:
                return FileType.NotSet;
        }
    }

    public static byte[] AcbToWav(string acbPath)
    {
        var acb = ACB_File.Load(acbPath);
        var wave = acb.GetWaveformsFromCue(acb.Cues[0])[0];
        var entry = acb.GetAfs2Entry(wave.AwbId);
        using MemoryStream stream = new MemoryStream(entry.bytes);
        return ConvertStream.ConvertFile(new Options(), stream, GetFileType(wave.EncodeType), FileType.Wave);
    }

    // 从MP4视频文件中提取音频轨道并保存为WAV文件
    public static void ExtractAudioFromMp4(string mp4Path, string outputWavPath)
    {
        using (var reader = new MediaFoundationReader(mp4Path))
        {
            // MediaFoundationReader 会自动解码视频中的音频流（如AAC）为PCM
            WaveFileWriter.CreateWaveFile(outputWavPath, reader);
        }
    }

    // 将 WAV 字节数据转换为 MP3 文件
    public static void ConvertWavBytesToMp3(byte[] wavData, string mp3Path)
    {
        // 将 WAV 字节数据写入内存流
        using var wavStream = new MemoryStream(wavData);
        using var reader = new WaveFileReader(wavStream);

        // 创建 MP3 文件并编码
        using var writer = new LameMP3FileWriter(mp3Path, reader.WaveFormat, 256);
        reader.CopyTo(writer);
    }
}