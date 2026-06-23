namespace MaiChartManager.Utils;

public enum VideoCodecKind { H264, Vp9 }

/// 描述「用某个编码器怎么编一段视频」。把今天散落、且会泄漏给硬件编码器的软件专属参数
/// （-cpu-used/-pix_fmt）收拢：硬件 profile 携带 device/hwupload，软件参数只在软件 profile 生效。
public sealed record VideoEncoderProfile(
    string Name, // "h264_vaapi" 等，仅日志/识别
    VideoCodecKind Kind,
    bool IsHardware,
    IReadOnlyList<string> PreInputArgs, // 输入前参数，如 ["-vaapi_device", "/dev/dri/renderD128"]
    string Codec, // -c:v 的值，如 h264_vaapi / libx264 / vp9
    string? UploadFilter, // 追加到 filter 链末尾，如 "format=nv12,hwupload"；NVENC/软件为 null
    IReadOnlyList<string> ExtraOutputArgs) // 硬件专属输出参数（初版留空，后续调码控用）
{
    // 软件兜底 profile（编码器名与今天一致：H264=libx264，VP9=vp9，保证 Windows 逐参数不变）
    public static VideoEncoderProfile SoftwareH264 { get; } =
        new("libx264", VideoCodecKind.H264, false, [], "libx264", null, []);

    public static VideoEncoderProfile SoftwareVp9 { get; } =
        new("vp9", VideoCodecKind.Vp9, false, [], "vp9", null, []);
}
