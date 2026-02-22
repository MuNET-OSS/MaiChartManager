using Microsoft.VisualBasic.FileIO;
using Xabe.FFmpeg;

namespace MaiChartManager.Utils;

public static class VideoConvert
{
    public enum HardwareAccelerationStatus
    {
        Pending,
        Enabled,
        Disabled
    }

    public static HardwareAccelerationStatus HardwareAcceleration { get; private set; } = HardwareAccelerationStatus.Pending;
    public static string H264Encoder { get; private set; } = "libx264";

    private static string Vp9Encoding => HardwareAcceleration == HardwareAccelerationStatus.Enabled ? "vp9_qsv" : "vp9";
    private static readonly SemaphoreSlim UsmToMp4Semaphore = new(
        Math.Max(1, Environment.ProcessorCount / 4),
        Math.Max(1, Environment.ProcessorCount / 4));

    /// <summary>
    /// 检测硬件加速支持
    /// </summary>
    public static async Task CheckHardwareAcceleration()
    {
        var tmpDir = Directory.CreateTempSubdirectory();
        try
        {
            // 测试 VP9 QSV 硬件加速
            var blankPath = Path.Combine(tmpDir.FullName, "blank.ivf");
            await FFmpeg.Conversions.New()
                .SetOutputTime(TimeSpan.FromSeconds(2))
                .SetInputFormat(Format.lavfi)
                .AddParameter("-i color=c=black:s=720x720:r=1")
                .AddParameter("-c:v vp9_qsv")
                .UseMultiThread(true)
                .SetOutput(blankPath)
                .Start();
            HardwareAcceleration = HardwareAccelerationStatus.Enabled;
        }
        catch (Exception e)
        {
            HardwareAcceleration = HardwareAccelerationStatus.Disabled;
        }

        // 检测 H264 硬件编码器
        foreach (var encoder in (string[])["h264_nvenc", "h264_qsv", "h264_vaapi", "h264_amf", "h264_mf", "h264_vulkan"])
        {
            try
            {
                var blankPath = Path.Combine(tmpDir.FullName, $"{encoder}.mp4");
                await FFmpeg.Conversions.New()
                    .SetOutputTime(TimeSpan.FromSeconds(2))
                    .SetInputFormat(Format.lavfi)
                    .AddParameter("-i color=c=black:s=720x720:r=1")
                    .AddParameter($"-c:v {encoder}")
                    .UseMultiThread(true)
                    .SetOutput(blankPath)
                    .Start();
                H264Encoder = encoder;
                break;
            }
            catch (Exception e) { }
        }

        Console.WriteLine($"H264 encoder: {H264Encoder}");
    }

    public class VideoConvertOptions
    {
        /// <summary>
        /// 是否禁用缩放
        /// </summary>
        public bool NoScale { get; set; }

        /// <summary>
        /// 是否使用 H264 编码（而非 VP9）
        /// </summary>
        public bool UseH264 { get; set; }

        /// <summary>
        /// 是否使用 YUV420p 颜色空间
        /// </summary>
        public bool UseYuv420p { get; set; }

        /// <summary>
        /// 视频 padding（秒），正数为前置空白，负数为裁剪开头
        /// </summary>
        public double Padding { get; set; }

        /// <summary>
        /// 输入文件路径
        /// </summary>
        public required string InputPath { get; set; }

        /// <summary>
        /// 输出文件路径
        /// </summary>
        public required string OutputPath { get; set; }

        /// <summary>
        /// 进度回调
        /// </summary>
        public Action<int>? OnProgress { get; set; }

        /// <summary>
        /// 输入文件 MIME 类型
        /// </summary>
        public string? ContentType { get; set; }

        public bool TaskbarProgress { get; set; } = true;
    }

    /// <summary>
    /// 转换视频为 VP9/H264，并可选转换为 USM
    /// </summary>
    public static async Task ConvertVideo(VideoConvertOptions options)
    {
        var tmpDir = Directory.CreateTempSubdirectory();
        try
        {
            if (options.TaskbarProgress)
            {
                WinUtils.SetTaskbarProgressIndeterminate();
            }

            var outputDirectory = Path.GetDirectoryName(options.OutputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            // 第一步：转换为 VP9 (IVF) 或 H264 (MP4)
            var intermediateFile = Path.Combine(tmpDir.FullName, options.UseH264 ? "out.mp4" : "out.ivf");
            await ConvertToVp9OrH264(options, intermediateFile, tmpDir.FullName);

            // 验证输出文件
            if (!File.Exists(intermediateFile) || new FileInfo(intermediateFile).Length == 0)
            {
                throw new Exception("视频转换失败：输出文件不存在或为空");
            }

            // 第二步：VP9 直接打包到目标 USM，避免中间 USM 文件再复制。
            if (options.UseH264)
            {
                FileSystem.CopyFile(intermediateFile, options.OutputPath, true);
            }
            else
            {
                if (options.TaskbarProgress)
                {
                    WinUtils.SetTaskbarProgressIndeterminate();
                }

                WannaCRI.WannaCRI.CreateUsm(intermediateFile, options.OutputPath);
                if (!File.Exists(options.OutputPath) || new FileInfo(options.OutputPath).Length == 0)
                {
                    throw new Exception("视频转换为 USM 失败：输出文件不存在或为空");
                }
            }
        }
        finally
        {
            WinUtils.ClearTaskbarProgress();
            // 清理临时目录
            try
            {
                tmpDir.Delete(true);
            }
            catch
            {
                // 忽略清理错误
            }
        }
    }

    private static async Task ConvertToVp9OrH264(VideoConvertOptions options, string outputPath, string tmpDir)
    {
        var srcMedia = await FFmpeg.GetMediaInfo(options.InputPath);
        var codec = options.UseH264 ? H264Encoder : Vp9Encoding;
        var firstStream = srcMedia.VideoStreams.First().SetCodec(codec);
        var conversion = FFmpeg.Conversions.New()
            .AddStream(firstStream);

        // 处理图片输入
        if (options.ContentType?.StartsWith("image/") == true)
        {
            options.Padding = 0;
            conversion.AddParameter("-r 1 -t 2");
            conversion.AddParameter("-loop 1", ParameterPosition.PreInput);
        }

        // 处理极小的 padding
        if (options.Padding is > 0 and < 0.05)
        {
            options.Padding = 0;
        }

        // 处理缩放
        var vf = "";
        var scale = options.UseH264 ? 2160 : 1080;
        if (!options.NoScale)
        {
            vf = $"scale={scale}:-1,pad={scale}:{scale}:({scale}-iw)/2:({scale}-ih)/2:black";
        }

        // 处理 padding
        if (options.Padding < 0)
        {
            // 负数：裁剪开头
            conversion.SetSeek(TimeSpan.FromSeconds(-options.Padding));
        }
        else if (options.Padding > 0)
        {
            // 正数：添加前置空白
            var blankPath = Path.Combine(tmpDir, "blank.mp4");
            var blank = FFmpeg.Conversions.New()
                .SetOutputTime(TimeSpan.FromSeconds(options.Padding))
                .SetInputFormat(Format.lavfi)
                .AddParameter($"-i color=c=black:s={srcMedia.VideoStreams.First().Width}x{srcMedia.VideoStreams.First().Height}:r=30")
                .UseMultiThread(true)
                .SetOutput(blankPath);
            await blank.Start();
            var blankVideoInfo = await FFmpeg.GetMediaInfo(blankPath);
            conversion = Concatenate(vf, blankVideoInfo, srcMedia);
            conversion.AddParameter($"-c:v {codec}");
        }

        // 基本参数
        conversion
            .SetOutput(outputPath)
            .AddParameter("-hwaccel dxva2", ParameterPosition.PreInput)
            .UseMultiThread(true);

        // VP9 特定参数
        if (!options.UseH264)
        {
            conversion.AddParameter("-cpu-used 5");
            if (options.UseYuv420p)
                conversion.AddParameter("-pix_fmt yuv420p");
        }

        // 应用缩放参数
        if (!options.NoScale && options.Padding <= 0)
        {
            conversion.AddParameter($"-vf {vf}");
        }

        // 进度回调
        if (options.OnProgress != null)
        {
            conversion.OnProgress += (sender, args) =>
            {
                options.OnProgress((int)args.Percent);
            };
        }
        if (options.TaskbarProgress)
        {
            conversion.OnProgress += (sender, args) =>
            {
                WinUtils.SetTaskbarProgress((ulong)args.Percent);
            };
        }

        await conversion.Start();
    }

    private static IConversion Concatenate(string vf, params IMediaInfo[] mediaInfos)
    {
        var conversion = FFmpeg.Conversions.New();
        foreach (var inputVideo in mediaInfos)
        {
            conversion.AddParameter("-i " + inputVideo.Path.Escape() + " ");
        }

        conversion.AddParameter("-filter_complex \"");
        for (var index = 0; index < mediaInfos.Length; ++index)
            conversion.AddParameter($"[{index}:v]setsar=1[{index}s];");
        for (var index = 0; index < mediaInfos.Length; ++index)
            conversion.AddParameter($"[{index}s] ");
        conversion.AddParameter($"concat=n={mediaInfos.Length}:v=1 [v]; [v]{vf}[vout]\" -map \"[vout]\"");

        conversion.AddParameter("-aspect 1:1");
        return conversion;
    }

    /// <summary>
    /// 简化版：只转换视频到 VP9 IVF，然后到 USM/DAT
    /// </summary>
    public static async Task ConvertVideoToUsm(string inputPath, string outputPath, bool noScale = false, bool yuv420p = false, Action<int>? onProgress = null)
    {
        await ConvertVideo(new VideoConvertOptions
        {
            InputPath = inputPath,
            OutputPath = outputPath,
            NoScale = noScale,
            UseH264 = false,
            UseYuv420p = yuv420p,
            Padding = 0,
            OnProgress = onProgress
        });
    }

    /// <summary>
    /// 将 USM/DAT 文件转换为 MP4
    /// </summary>
    /// <param name="inputPath">输入的 USM 或 DAT 文件路径</param>
    /// <param name="outputPath">输出的 MP4 文件路径</param>
    /// <param name="onProgress">进度回调（可选）</param>
    public static async Task ConvertUsmToMp4(string inputPath, string outputPath, Action<int>? onProgress = null)
    {
        await UsmToMp4Semaphore.WaitAsync();
        try
        {
            var tmpDir = Directory.CreateTempSubdirectory();
            try
            {
                var movieUsm = Path.Combine(tmpDir.FullName, "movie.usm");

                onProgress?.Invoke(10);
                FileSystem.CopyFile(inputPath, movieUsm, UIOption.OnlyErrorDialogs);

                // 解包 USM
                onProgress?.Invoke(30);
                WannaCRI.WannaCRI.UnpackUsm(movieUsm, Path.Combine(tmpDir.FullName, "output"));

                // 查找解包后的 IVF 文件
                onProgress?.Invoke(50);
                var outputIvfFile = Directory.EnumerateFiles(Path.Combine(tmpDir.FullName, @"output\movie.usm\videos")).FirstOrDefault();
                if (outputIvfFile is null)
                {
                    throw new Exception("USM 解包失败：未找到视频文件");
                }

                // 转换为 MP4
                var conversion = FFmpeg.Conversions.New()
                    .AddParameter("-i " + outputIvfFile.Escape())
                    .AddParameter("-c:v copy")
                    .SetOutput(outputPath);

                if (onProgress != null)
                {
                    conversion.OnProgress += (sender, args) =>
                    {
                        // FFmpeg 进度从 50% 开始，映射到 50-100%
                        var percent = 50 + (int)(args.Percent / 2);
                        onProgress(percent);
                    };
                }

                await conversion.Start();

                if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
                {
                    throw new Exception("转换失败：输出文件不存在或为空");
                }
            }
            finally
            {
                // 清理临时目录
                try
                {
                    tmpDir.Delete(true);
                }
                catch
                {
                    // 忽略清理错误
                }
            }
        }
        finally
        {
            UsmToMp4Semaphore.Release();
        }
    }
}