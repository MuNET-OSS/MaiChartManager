using MaiChartManager.Utils;
using Microsoft.AspNetCore.Mvc;

namespace MaiChartManager.Controllers.App;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api")]
public class AppVersionController(StaticSettings settings, ILogger<AppVersionController> logger) : ControllerBase
{
#if WINDOWS
    public record AppVersionResult(string Version, int GameVersion, IapManager.LicenseStatus License, VideoConvert.HardwareAccelerationStatus HardwareAcceleration, string H264Encoder, string Locale);

    [HttpGet]
    public AppVersionResult GetAppVersion()
    {
        return new AppVersionResult(Application.ProductVersion, settings.gameVersion, IapManager.License, VideoConvert.HardwareAcceleration, VideoConvert.H264Encoder, StaticSettings.CurrentLocale);
    }
#else
    public enum LicenseStatus { Pending, Active, Inactive }
    public record AppVersionResult(string Version, int GameVersion, LicenseStatus License, VideoConvert.HardwareAccelerationStatus HardwareAcceleration, string H264Encoder, string Locale);

    [HttpGet]
    public AppVersionResult GetAppVersion()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "linux";
        return new AppVersionResult(version, settings.gameVersion, LicenseStatus.Active, VideoConvert.HardwareAcceleration, VideoConvert.H264Encoder, StaticSettings.CurrentLocale);
    }
#endif
}
