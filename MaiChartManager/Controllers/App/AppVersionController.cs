using MaiChartManager.Utils;
using Microsoft.AspNetCore.Mvc;

namespace MaiChartManager.Controllers.App;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api")]
public class AppVersionController(StaticSettings settings, ILogger<AppVersionController> logger) : ControllerBase
{
#if WINDOWS
    public record AppVersionResult(string Version, int GameVersion, IapManager.LicenseStatus License, VideoConvert.HardwareAccelerationStatus HardwareAcceleration, string H264Encoder, string Locale, string Platform, bool Export);

    [HttpGet]
    public AppVersionResult GetAppVersion()
    {
        return new AppVersionResult(
            Application.ProductVersion,
            settings.gameVersion,
            IapManager.License,
            VideoConvert.HardwareAcceleration,
            VideoConvert.H264Encoder,
            StaticSettings.CurrentLocale,
            OperatingSystem.IsWindows() ? "Windows" : "Linux",
            StaticSettings.Config.Export);
    }
#else
    public enum LicenseStatus { Pending, Active, Inactive }
    public record AppVersionResult(string Version, int GameVersion, LicenseStatus License, VideoConvert.HardwareAccelerationStatus HardwareAcceleration, string H264Encoder, string Locale, string Platform, bool Export);

    [HttpGet]
    public AppVersionResult GetAppVersion()
    {
        // 与 Windows 的 Application.ProductVersion 语义一致：取程序集 InformationalVersion
        //（由 PKGBUILD 在 publish 时通过 -p:InformationalVersion 注入 git 派生的版本号），
        // 去掉 SourceLink 可能附带的 "+<commit>" 后缀。
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        var info = (System.Reflection.AssemblyInformationalVersionAttribute?)System.Attribute
            .GetCustomAttribute(asm, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
        var version = info?.InformationalVersion?.Split('+')[0] ?? "linux";
        return new AppVersionResult(
            version,
            settings.gameVersion,
            LicenseStatus.Active,
            VideoConvert.HardwareAcceleration,
            VideoConvert.H264Encoder,
            StaticSettings.CurrentLocale,
            OperatingSystem.IsWindows() ? "Windows" : "Linux",
            StaticSettings.Config.Export);
    }
#endif
}
