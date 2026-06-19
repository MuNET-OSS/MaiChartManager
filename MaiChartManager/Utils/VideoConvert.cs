using MaiChartManager.Platform;
using FFMpegCore;
using FFMpegCore.Enums;

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

    // 暴露给前端显示的选中 H264 编码器名（保持原 API 契约）。
    public static string H264Encoder => VideoEncoderProbe.H264Profile.Name;

    private static readonly SemaphoreSlim UsmToMp4Semaphore = new(
        Math.Max(1, Environment.ProcessorCount / 4),
        Math.Max(1, Environment.ProcessorCount / 4));

    /// <summary>
    /// 等价于 Xabe 的 UseMultiThread(true)：渲染为 "-threads {Min(ProcessorCount, 16)}"。
    /// </summary>
    private static string MultiThreadArg => $"-threads {Math.Min(Environment.ProcessorCount, 16)}";

    /// <summary>
    /// 把 TimeSpan 格式化为 ffmpeg 时间字符串 H:MM:SS.mmm，
    /// 与原 Xabe TimeExtensions.ToFFmpeg 行为一致（"{0:D}:{1:D2}:{2:D2}.{3:D3}"，
    /// 参数为 (int)TotalHours, Minutes, Seconds, Milliseconds）。
    /// </summary>
    private static string FormatFFmpegTime(TimeSpan time) =>
        $"{(int)time.TotalHours:D}:{time.Minutes:D2}:{time.Seconds:D2}.{time.Milliseconds:D3}";

    /// <summary>
    /// 软件专属输出参数（-cpu-used/-pix_fmt）只对软件 VP9 编码器发出；-threads 与今天一致始终发出。
    /// 顺序对齐今天：-c:v &lt;codec&gt; -map 0:0 [图片 -r 1 -t 2] -threads N [软件VP9: -cpu-used 5 [pix_fmt]] [profile 硬件额外]
    /// </summary>
    private static List<string> BuildPostArgs(VideoEncoderProfile p, VideoConvertOptions options, bool isImage)
    {
        var post = new List<string> { $"-c:v {p.Codec}", "-map 0:0" };
        if (isImage) post.Add("-r 1 -t 2");
        post.Add(MultiThreadArg);
        if (p.Kind == VideoCodecKind.Vp9 && !p.IsHardware)
        {
            post.Add("-cpu-used 5");
            if (options.UseYuv420p) post.Add("-pix_fmt yuv420p");
        }
        post.AddRange(p.ExtraOutputArgs);
        return post;
    }

    /// <summary>
    /// 把硬件上传节点追加到软件 filter 链末尾（软件 profile 原样返回 vf）。
    /// </summary>
    private static string AppendUpload(string vf, VideoEncoderProfile p)
    {
        if (string.IsNullOrEmpty(p.UploadFilter)) return vf;
        return string.IsNullOrEmpty(vf) ? p.UploadFilter : $"{vf},{p.UploadFilter}";
    }

    /// <summary>
    /// 探测并选定 H264/VP9 编码器 profile。启动时调用（AppMain / LinuxProgram）。
    /// </summary>
    public static async Task CheckHardwareAcceleration()
    {
        await VideoEncoderProbe.Probe(StaticSettings.Config.ForceSoftwareVideo);
        HardwareAcceleration = (VideoEncoderProbe.H264Profile.IsHardware || VideoEncoderProbe.Vp9Profile.IsHardware)
            ? HardwareAccelerationStatus.Enabled
            : HardwareAccelerationStatus.Disabled;
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
#if WINDOWS
                WinUtils.SetTaskbarProgressIndeterminate();
#endif
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
                PlatformFile.CopyFile(intermediateFile, options.OutputPath);
            }
            else
            {
                if (options.TaskbarProgress)
                {
#if WINDOWS
                    WinUtils.SetTaskbarProgressIndeterminate();
#endif
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
#if WINDOWS
            WinUtils.ClearTaskbarProgress();
#endif
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
        var srcMedia = await FFProbe.AnalyseAsync(options.InputPath);
        var profile = options.UseH264 ? VideoEncoderProbe.H264Profile : VideoEncoderProbe.Vp9Profile;
        var srcWidth = srcMedia.PrimaryVideoStream!.Width;
        var srcHeight = srcMedia.PrimaryVideoStream!.Height;
        var srcDuration = srcMedia.Duration;

        var logCollector = new FfmpegLogCollector();

        // 处理图片输入
        var isImage = options.ContentType?.StartsWith("image/") == true;
        if (isImage)
        {
            options.Padding = 0;
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

        // 与 Xabe 行为一致：源视频流默认通过 -map 0:0 选取并以 -c:v <codec> 编码。
        // PreInput 参数（如 -loop 1 / -hwaccel dxva2）放到 FromFileInput 的 input options；
        // PostInput 参数（-c:v / -t / -vf / -threads 等）放到 OutputToFile 的 output options，
        // 顺序严格对应原 Xabe 的拼装顺序：
        //   [streams PostInput: -c:v <codec> -map 0:0] [user PostInput...]

        if (options.Padding > 0)
        {
            // 正数：添加前置空白，先生成 blank，再 concat。
            // 等价 Xabe：-t <padding> -f lavfi -i color=c=black:s=WxH:r=30 -threads N <blank.mp4>
            var blankPath = Path.Combine(tmpDir, "blank.mp4");
            await FFMpegArguments
                .FromFileInput($"color=c=black:s={srcWidth}x{srcHeight}:r=30", verifyExists: false,
                    opt => opt.WithCustomArgument($"-t {FormatFFmpegTime(TimeSpan.FromSeconds(options.Padding))} -f lavfi"))
                .OutputToFile(blankPath, overwrite: true,
                    opt => opt.WithCustomArgument(MultiThreadArg))
                .NotifyOnError(logCollector.AddLine)
                .ProcessAsynchronously();

            await RunConcatenate(vf, profile, [blankPath, options.InputPath], outputPath, options, logCollector, srcDuration);
            return;
        }

        // 非 padding>0 的常规路径，消费选定的编码器 profile。
        // PreInput：图片 -loop 1 在前，随后 profile.PreInputArgs（Windows=-hwaccel dxva2，Linux VAAPI=-vaapi_device …）。
        var preArgs = new List<string>();
        if (isImage)
        {
            preArgs.Add("-loop 1");
        }
        preArgs.AddRange(profile.PreInputArgs);

        // PostInput：-c:v <codec> -map 0:0 [图片 -r 1 -t 2] -threads N [软件VP9: -cpu-used 5 [pix_fmt]] [硬件额外]
        var postArgs = BuildPostArgs(profile, options, isImage);

        // 负数 padding：裁剪开头（-ss），位置与今天一致（用户 PostInput 段、在 -threads 之后）。
        if (options.Padding < 0)
        {
            postArgs.Add($"-ss {FormatFFmpegTime(TimeSpan.FromSeconds(-options.Padding))}");
        }

        // 应用 filter：软件缩放 + 硬件上传节点（hwupload）。
        if (!options.NoScale && options.Padding <= 0)
        {
            postArgs.Add($"-vf {AppendUpload(vf, profile)}");
        }
        else if (profile.UploadFilter is not null)
        {
            // NoScale 但硬件编码器仍需要把帧上传到硬件表面
            postArgs.Add($"-vf {AppendUpload("", profile)}");
        }

        // preArgs 在 Linux 软件编码非图片场景下可能为空，此时不附加任何 input 参数。
        var args = preArgs.Count > 0
            ? FFMpegArguments.FromFileInput(options.InputPath, verifyExists: false,
                opt => opt.WithCustomArgument(string.Join(" ", preArgs)))
            : FFMpegArguments.FromFileInput(options.InputPath, verifyExists: false);

#if DEBUG
        Console.WriteLine($"[ffargs] kind={(options.UseH264 ? "H264" : "VP9")} profile={profile.Name} pre=[{string.Join(" ", preArgs)}] post=[{string.Join(" ", postArgs)}]");
#endif

        var processor = args
            .OutputToFile(outputPath, overwrite: true,
                opt => opt.WithCustomArgument(string.Join(" ", postArgs)))
            .NotifyOnError(logCollector.AddLine);

        AttachProgress(processor, options, srcDuration);

        try
        {
            await processor.ProcessAsynchronously();
        }
        catch (Exception ex)
        {
            throw new VideoConversionException(
                FfmpegDiagnostics.CreateSummary(ex, logCollector.GetLog()),
                FfmpegDiagnostics.CreateDetail(ex, logCollector.GetLog()),
                ex);
        }
    }

    /// <summary>
    /// 把进度回调挂到 FFMpegCore 的 NotifyOnProgress 上，用源时长换算百分比，
    /// 语义对应原 Xabe 的 conversion.OnProgress += (s, args) => cb((int)args.Percent)。
    /// 注意：FFMpegCore 的 NotifyOnProgress 只保留最后一次注册的回调（不像 Xabe 的事件可叠加），
    /// 所以必须把「前端进度」和「任务栏进度」合并到同一个回调里，否则后者会把前者覆盖掉，
    /// 导致 Linux 上（任务栏回调是 #if WINDOWS 空实现）前端进度恒为 0。
    /// </summary>
    private static void AttachProgress(FFMpegArgumentProcessor processor, VideoConvertOptions options, TimeSpan totalDuration)
    {
        if (options.OnProgress == null && !options.TaskbarProgress) return;

        processor.NotifyOnProgress(percent =>
        {
            options.OnProgress?.Invoke((int)percent);
#if WINDOWS
            if (options.TaskbarProgress)
                WinUtils.SetTaskbarProgress((ulong)percent);
#endif
        }, totalDuration);
    }

    /// <summary>
    /// 等价于原 Concatenate + 启动转换：把多个输入用 concat 滤镜拼接后输出。
    /// 原 Xabe 拼装（按输入顺序）：
    ///   -i in0 -i in1 ... -filter_complex "[0:v]setsar=1[0s];...[0s] [1s] ...concat=n=K:v=1 [v]; [v]<vf>[vout]" -map "[vout]" -aspect 1:1
    /// 然后再 AddParameter("-c:v &lt;codec&gt;") 与基本段的 -hwaccel(PreInput)/-threads，
    /// 此处一并按相同顺序拼出。
    /// </summary>
    private static async Task RunConcatenate(string vf, VideoEncoderProfile profile, string[] inputs, string outputPath,
        VideoConvertOptions options, FfmpegLogCollector logCollector, TimeSpan totalDuration)
    {
        // filter_complex 串，完全照搬原 Concatenate 的拼接逻辑；末尾把硬件上传节点接到 [vout] 前。
        var fc = "";
        for (var index = 0; index < inputs.Length; ++index)
            fc += $"[{index}:v]setsar=1[{index}s];";
        for (var index = 0; index < inputs.Length; ++index)
            fc += $"[{index}s] ";
        var tail = AppendUpload(vf, profile); // 软件: vf；硬件: vf,format=nv12,hwupload（vf 可空）
        fc += $"concat=n={inputs.Length}:v=1 [v]; [v]{tail}[vout]";

        // 输入：保持原顺序（blank 在前，源在后）。第一个输入的 PreInput 用 profile.PreInputArgs
        //（Windows=-hwaccel dxva2，Linux VAAPI=-vaapi_device …；软件为空串无害）。
        var args = profile.PreInputArgs.Count > 0
            ? FFMpegArguments.FromFileInput(inputs[0], verifyExists: false,
                opt => opt.WithCustomArgument(string.Join(" ", profile.PreInputArgs)))
            : FFMpegArguments.FromFileInput(inputs[0], verifyExists: false);
        for (var i = 1; i < inputs.Length; i++)
        {
            args = args.AddFileInput(inputs[i], verifyExists: false);
        }

        // PostInput 段顺序对应原代码：
        //   -filter_complex "..." -map "[vout]"  （来自 Concatenate）
        //   -aspect 1:1                          （来自 Concatenate）
        //   -c:v <codec>                         （Concatenate 后 AddParameter）
        //   -threads N                           （基本段 UseMultiThread）
        //   [软件VP9] -cpu-used 5 [+ -pix_fmt yuv420p]
        //   [硬件额外]
        var postArgs = new List<string>
        {
            $"-filter_complex \"{fc}\" -map \"[vout]\"",
            "-aspect 1:1",
            $"-c:v {profile.Codec}",
            MultiThreadArg,
        };

        if (profile.Kind == VideoCodecKind.Vp9 && !profile.IsHardware)
        {
            postArgs.Add("-cpu-used 5");
            if (options.UseYuv420p)
                postArgs.Add("-pix_fmt yuv420p");
        }
        postArgs.AddRange(profile.ExtraOutputArgs);

#if DEBUG
        Console.WriteLine($"[ffargs] concat profile={profile.Name} pre=[{string.Join(" ", profile.PreInputArgs)}] post=[{string.Join(" ", postArgs)}]");
#endif

        var processor = args
            .OutputToFile(outputPath, overwrite: true,
                opt => opt.WithCustomArgument(string.Join(" ", postArgs)))
            .NotifyOnError(logCollector.AddLine);

        AttachProgress(processor, options, totalDuration);

        try
        {
            await processor.ProcessAsynchronously();
        }
        catch (Exception ex)
        {
            throw new VideoConversionException(
                FfmpegDiagnostics.CreateSummary(ex, logCollector.GetLog()),
                FfmpegDiagnostics.CreateDetail(ex, logCollector.GetLog()),
                ex);
        }
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
                PlatformFile.CopyFile(inputPath, movieUsm);

                // 解包 USM
                onProgress?.Invoke(30);
                WannaCRI.WannaCRI.UnpackUsm(movieUsm, Path.Combine(tmpDir.FullName, "output"));

                // 查找解包后的 IVF 文件
                onProgress?.Invoke(50);
                var outputIvfFile = Directory.EnumerateFiles(Path.Combine(tmpDir.FullName, "output", "movie.usm", "videos")).FirstOrDefault();
                if (outputIvfFile is null)
                {
                    throw new Exception("USM 解包失败：未找到视频文件");
                }

                // 转换为 MP4
                // 等价 Xabe 命令行：-i <ivf> -c:v copy <mp4>
                var logCollector = new FfmpegLogCollector();
                var srcDuration = (await FFProbe.AnalyseAsync(outputIvfFile)).Duration;

                var processor = FFMpegArguments
                    .FromFileInput(outputIvfFile, verifyExists: false)
                    .OutputToFile(outputPath, overwrite: true, opt => opt.WithCustomArgument("-c:v copy"))
                    .NotifyOnError(logCollector.AddLine);

                if (onProgress != null)
                {
                    // FFmpeg 进度从 50% 开始，映射到 50-100%
                    processor.NotifyOnProgress(percent => onProgress(50 + (int)(percent / 2)), srcDuration);
                }

                try
                {
                    await processor.ProcessAsynchronously();
                }
                catch (Exception ex)
                {
                    throw new VideoConversionException(
                        FfmpegDiagnostics.CreateSummary(ex, logCollector.GetLog()),
                        FfmpegDiagnostics.CreateDetail(ex, logCollector.GetLog()),
                        ex);
                }

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

public sealed class VideoConversionException(string message, string detail, Exception innerException)
    : Exception(message, innerException)
{
    public string Detail { get; } = detail;
}
