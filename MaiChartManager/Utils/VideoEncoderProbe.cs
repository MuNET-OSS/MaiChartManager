using FFMpegCore;

namespace MaiChartManager.Utils;

/// 视频编码器探测：对每个候选 profile 跑「完整 recipe」的 1 帧 lavfi 试编码，
/// 输出非空且退出码 0 才算通过——这正是旧探测的缺陷所在（VAAPI/QSV 缺 device 初始化必失败）。
/// 选第一个通过的；都不通过回退软件。结果存静态，供 VideoConvert 使用。
public static class VideoEncoderProbe
{
    public static VideoEncoderProfile H264Profile { get; private set; } = VideoEncoderProfile.SoftwareH264;
    public static VideoEncoderProfile Vp9Profile { get; private set; } = VideoEncoderProfile.SoftwareVp9;

    /// 启动时调用。forceSoftware=true 时直接用软件 profile，不探测。
    public static async Task Probe(bool forceSoftware)
    {
        if (forceSoftware)
        {
            H264Profile = VideoEncoderProfile.SoftwareH264;
            Vp9Profile = VideoEncoderProfile.SoftwareVp9;
            Console.WriteLine("[hwaccel] 强制软件编码：H264=libx264 VP9=vp9");
            return;
        }

#if WINDOWS
        await ProbeWindows();
#else
        await ProbeLinux();
#endif
        Console.WriteLine($"[hwaccel] 选中 H264={H264Profile.Name} VP9={Vp9Profile.Name}");
    }

    // 试编一帧；成功（输出非空 + 退出码 0）返回 true。任何异常都视为该编码器不可用。
    private static async Task<bool> TryEncode(VideoEncoderProfile p)
    {
        var ext = p.Kind == VideoCodecKind.H264 ? "mp4" : "ivf";
        var outPath = Path.Combine(StaticSettings.tempPath, $"hwprobe_{p.Name}_{Guid.NewGuid():N}.{ext}");
        try
        {
            Directory.CreateDirectory(StaticSettings.tempPath);
            var pre = new List<string>(p.PreInputArgs) { "-t", "0:00:01.000", "-f", "lavfi" };
            var post = new List<string>();
            if (!string.IsNullOrEmpty(p.UploadFilter)) { post.Add("-vf"); post.Add(p.UploadFilter); }
            post.Add("-c:v"); post.Add(p.Codec);

            await FFMpegArguments
                .FromFileInput("color=c=black:s=720x720:r=1", verifyExists: false,
                    o => o.WithCustomArgument(string.Join(" ", pre)))
                .OutputToFile(outPath, overwrite: true, o => o.WithCustomArgument(string.Join(" ", post)))
                .ProcessAsynchronously();
            return File.Exists(outPath) && new FileInfo(outPath).Length > 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { File.Delete(outPath); } catch { /* ignore */ }
        }
    }

#if !WINDOWS
    private static async Task ProbeLinux()
    {
        var dev = await FindVaapiDevice();
        var vaapiPre = dev is null ? null : new[] { "-vaapi_device", dev };

        // H264：VAAPI → NVENC → QSV → 软件
        H264Profile = await FirstWorking(
        [
            dev is null ? null : new VideoEncoderProfile("h264_vaapi", VideoCodecKind.H264, true, vaapiPre!, "h264_vaapi", "format=nv12,hwupload", []),
            new VideoEncoderProfile("h264_nvenc", VideoCodecKind.H264, true, [], "h264_nvenc", null, []),
            dev is null ? null : new VideoEncoderProfile("h264_qsv", VideoCodecKind.H264, true, ["-qsv_device", dev], "h264_qsv", null, []),
        ]) ?? VideoEncoderProfile.SoftwareH264;

        // VP9：VAAPI → QSV → 软件（NVENC 没有 VP9 编码器，不入候选）
        Vp9Profile = await FirstWorking(
        [
            dev is null ? null : new VideoEncoderProfile("vp9_vaapi", VideoCodecKind.Vp9, true, vaapiPre!, "vp9_vaapi", "format=nv12,hwupload", []),
            dev is null ? null : new VideoEncoderProfile("vp9_qsv", VideoCodecKind.Vp9, true, ["-qsv_device", dev], "vp9_qsv", null, []),
        ]) ?? VideoEncoderProfile.SoftwareVp9;
    }

    // 枚举 /dev/dri/renderD128..135，取第一个能让 VAAPI 试编通过的设备
    private static async Task<string?> FindVaapiDevice()
    {
        for (var i = 128; i <= 135; i++)
        {
            var dev = $"/dev/dri/renderD{i}";
            if (!File.Exists(dev)) continue;
            var test = new VideoEncoderProfile("h264_vaapi", VideoCodecKind.H264, true,
                ["-vaapi_device", dev], "h264_vaapi", "format=nv12,hwupload", []);
            if (await TryEncode(test)) return dev;
        }
        return null;
    }

    private static async Task<VideoEncoderProfile?> FirstWorking(IEnumerable<VideoEncoderProfile?> candidates)
    {
        foreach (var c in candidates)
        {
            if (c is null) continue;
            if (await TryEncode(c)) return c;
        }
        return null;
    }
#endif

#if WINDOWS
    private static async Task ProbeWindows()
    {
        // 复刻今天的 Windows 语义：naive 命令（仅 -c:v，无 device）探测编码器名，全部带 -hwaccel dxva2。
        string[] h264Candidates = ["h264_nvenc", "h264_qsv", "h264_vaapi", "h264_amf", "h264_mf", "h264_vulkan"];
        var h264 = "libx264";
        foreach (var enc in h264Candidates)
            if (await TryEncode(new VideoEncoderProfile(enc, VideoCodecKind.H264, true, [], enc, null, []))) { h264 = enc; break; }
        H264Profile = new VideoEncoderProfile(h264, VideoCodecKind.H264, h264 != "libx264",
            ["-hwaccel", "dxva2"], h264, null, []);

        var vp9Hw = await TryEncode(new VideoEncoderProfile("vp9_qsv", VideoCodecKind.Vp9, true, [], "vp9_qsv", null, []));
        var vp9 = vp9Hw ? "vp9_qsv" : "vp9";
        Vp9Profile = new VideoEncoderProfile(vp9, VideoCodecKind.Vp9, vp9Hw,
            ["-hwaccel", "dxva2"], vp9, null, []);
    }
#endif
}
