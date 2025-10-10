using MaiChartManager.Utils;
using Microsoft.AspNetCore.Mvc;

namespace MaiChartManager.Controllers.App;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api")]
public class AppVersionController(StaticSettings settings, ILogger<AppVersionController> logger) : ControllerBase
{
    public record AppVersionResult(string Version, int GameVersion, IapManager.LicenseStatus License, VideoConvert.HardwareAccelerationStatus HardwareAcceleration, string H264Encoder);

    [HttpGet]
    public AppVersionResult GetAppVersion()
    {
        return new AppVersionResult(Application.ProductVersion, settings.gameVersion, IapManager.License, VideoConvert.HardwareAcceleration, VideoConvert.H264Encoder);
    }
}