using NAudio.Lame;
using NAudio.Wave;
using System.Diagnostics;
using Xabe.FFmpeg;
using VGAudio;
using VGAudio.Cli;
using Xv2CoreLib.ACB;

namespace MaiChartManager.Utils;

public static class Audio
{
    public static void ConvertToMai(string srcPath, string savePath, float padding = 0, Stream? src = null, string? previewFilename = null, Stream? preview = null, bool forceUseNAudio = false)
    {
        ACB_File acbTemplate;
        lock (_acbFileLoadLock) {
            acbTemplate = ACB_File.Load(File.ReadAllBytes(Path.Combine(StaticSettings.exeDir, previewFilename is null ? "nopreview.acb" : "template.acb")), null);
        }
        var wrapper = new ACB_Wrapper(acbTemplate);
        var trackBytes = LoadAndConvertFile(srcPath, FileType.Hca, false, 9170825592834449000, padding, src, forceUseNAudio);

        wrapper.Cues[0].AddTrackToCue(trackBytes, true, false, EncodeType.HCA);
        if (previewFilename is not null)
        {
            var previewTrackBytes = LoadAndConvertFile(previewFilename, FileType.Hca, true, 9170825592834449000, 0, preview);
            wrapper.Cues[1].AddTrackToCue(previewTrackBytes, true, false, EncodeType.HCA);
        }

        wrapper.AcbFile.Save(savePath);
    }

    // 不要 byte[] 转 memory stream 倒来倒去，直接传入 stream
    public static byte[] LoadAndConvertFile(string path, FileType convertToType, bool loop, ulong encrpytionKey = 0, float padding = 0, Stream? src = null, bool forceUseNAudio = false)
    {
        using var read = src ?? File.OpenRead(path);
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".wav":
            case ".mp3":
            case ".ogg":
            case ".wma":
            case ".aac":
                return ConvertFile(ConvertToWav(read, Path.GetExtension(path).ToLowerInvariant(), padding, forceUseNAudio), FileType.Wave, convertToType, loop, encrpytionKey);
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

    public static Stream ConvertToWav(Stream src, string extension, float padding = 0, bool forceUseNAudio = false)
    {
        using WaveStream reader = extension switch
        {
            ".ogg" => new NAudio.Vorbis.VorbisWaveReader(src, true),
            ".mp3" when !forceUseNAudio => new WaveFileReader(ConvertToWavViaFfmpeg(src, ".mp3")), // 默认情况下，优先使用ffmpeg
            // WAV / WMA / AAC（以及 MP3+forceUseNAudio 的兼容模式）原本走 Windows-only 的 MediaFoundation，
            // 跨平台改为用 ffmpeg 把任意输入解码成 16bit PCM wav，再用 NAudio WaveFileReader 读取。
            _ => new WaveFileReader(ConvertToWavViaFfmpeg(src, extension)),
        };
        // 关于上述MP3 Gapless问题的影响等具体讨论，详见 https://github.com/MuNET-OSS/MaiChartManager/issues/40
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
        WaveFileWriter.WriteWavFileToStream(stream, sample.ToWaveProvider16());
#if DEBUG
        Console.WriteLine($"ConvertToWav: extension={extension}, padding={padding}, forceUseNAudio={forceUseNAudio}");
        stream.Position = 0; // 把wav的内容写到本地文件以供调试
        File.WriteAllBytes(Path.Combine(StaticSettings.tempPath, "ConvertToWav_debug.wav"), stream.ToArray());
#endif
        stream.Position = 0;
        return stream;
    }

    // 用 ffmpeg 把任意输入流（按 ext 写到临时文件）解码成 16bit PCM wav，返回 wav 的内存流。
    // 替代 Windows-only 的 MediaFoundation，跨平台可用（系统 ffmpeg 已配好）。
    private static MemoryStream ConvertToWavViaFfmpeg(Stream src, string ext)
    {
        var tempFileGuid = Guid.NewGuid();
        // ext 形如 ".mp3"/".wav"/".aac" 等；去掉前导点用作临时输入文件后缀
        var inputExt = string.IsNullOrEmpty(ext) ? "" : (ext.StartsWith('.') ? ext : "." + ext);
        var inputPath = Path.Combine(StaticSettings.tempPath, $"ConvertToWav_{tempFileGuid:N}{inputExt}");
        var outputPath = Path.Combine(StaticSettings.tempPath, $"ConvertToWav_{tempFileGuid:N}.wav");
        try
        {
            Directory.CreateDirectory(StaticSettings.tempPath);

            using (var inputFile = new FileStream(inputPath, FileMode.Create, FileAccess.Write))
            {
                src.CopyTo(inputFile);
            }

            // 用 Process + ArgumentList 直接调 ffmpeg（每个参数独立、不拼引号）。
            // Xabe 在 Linux 上会把路径里的引号字面传给 ffmpeg，导致输出名变成 ...wav" → "Couldn't initialize muxer"；
            // ArgumentList 既能正确处理带空格的路径（Windows），又不引入引号问题（Linux）。
            RunFfmpeg("-y", "-i", inputPath, "-c:a", "pcm_s16le", outputPath);

            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
                throw new InvalidOperationException("ffmpeg produced empty wav file from input.");

            return new MemoryStream(File.ReadAllBytes(outputPath));
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
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

        // AcbCore 的 ConvertStream.ConvertFile 要求传入 MemoryStream；若来源不是则拷贝一份
        MemoryStream ms;
        if (s is MemoryStream existing)
        {
            ms = existing;
        }
        else
        {
            ms = new MemoryStream();
            s.CopyTo(ms);
            ms.Position = 0;
        }

        byte[] track = ConvertStream.ConvertFile(options, ms, encodeType, convertToType);

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
    
    private static readonly object _acbFileLoadLock = new();

    public static byte[] AcbToWav(string acbPath)
    {
        ACB_File acb;
        lock (_acbFileLoadLock) {
            acb = ACB_File.Load(acbPath);
        }
        var wave = acb.GetWaveformsFromCue(acb.Cues[0])[0];
        var entry = acb.GetAfs2Entry(wave.AwbId);
        using MemoryStream stream = new MemoryStream(entry.bytes);
        return ConvertStream.ConvertFile(new Options(), stream, GetFileType(wave.EncodeType), FileType.Wave);
    }

    // 从MP4视频文件中提取音频轨道并保存为WAV文件
    public static void ExtractAudioFromMp4(string mp4Path, string outputWavPath)
    {
        // 原本用 Windows-only 的 MediaFoundationReader 解码 mp4 内的音频流，
        // 跨平台改为先用 ffmpeg 把 mp4 的音频解码成 16bit PCM wav，再用 NAudio 读取写出。
        var wavPath = ConvertMp4AudioToWavViaFfmpeg(mp4Path);
        try
        {
            using var reader = new WaveFileReader(wavPath);
            WaveFileWriter.CreateWaveFile(outputWavPath, reader);
        }
        finally
        {
            File.Delete(wavPath);
        }
    }

    // 用 ffmpeg 把 mp4（或其它视频容器）里的音频流解码成 16bit PCM wav，返回临时 wav 文件路径（调用方负责删除）。
    private static string ConvertMp4AudioToWavViaFfmpeg(string mp4Path)
    {
        Directory.CreateDirectory(StaticSettings.tempPath);
        var outputPath = Path.Combine(StaticSettings.tempPath, $"ExtractMp4Audio_{Guid.NewGuid():N}.wav");

        // 同 ConvertToWavViaFfmpeg：用 Process + ArgumentList 直接调，避免 Xabe 在 Linux 上的引号问题。
        RunFfmpeg("-y", "-i", mp4Path, "-vn", "-c:a", "pcm_s16le", outputPath);

        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
        {
            File.Delete(outputPath);
            throw new InvalidOperationException("ffmpeg produced empty wav file from mp4 input.");
        }

        return outputPath;
    }

    /// <summary>
    /// 直接用 Process + ArgumentList 调系统/内置 ffmpeg，每个参数作为独立 argv（不拼引号）。
    /// 跨平台正确处理带空格的路径，且规避 Xabe.FFmpeg 在 Linux 上把引号字面传给 ffmpeg 的问题。
    /// </summary>
    private static void RunFfmpeg(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = FfmpegExePath(),
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 ffmpeg 进程");
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg 转换失败（exit {p.ExitCode}）：{err}");
    }

    /// 解析 ffmpeg 可执行文件路径：Windows 用内置 ffmpeg.exe，Linux 在 PATH 里找系统 ffmpeg。
    private static string FfmpegExePath()
    {
#if WINDOWS
        return Path.Combine(StaticSettings.exeDir, "ffmpeg.exe");
#else
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var d in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(d)) continue;
            var candidate = Path.Combine(d, "ffmpeg");
            if (File.Exists(candidate)) return candidate;
        }
        return "ffmpeg";
#endif
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