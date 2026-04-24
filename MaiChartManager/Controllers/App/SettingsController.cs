using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace MaiChartManager.Controllers.App;

public class SettingsDto
{
    public MovieCodec MovieCodec { get; set; }
    public bool Yuv420p { get; set; }
    public bool NoScale { get; set; }
    public bool IgnoreLevel { get; set; }
    public bool DisableBga { get; set; }
    public int UiZoom { get; set; }
    public double TargetDpiScale { get; set; }
}

[ApiController]
[Route("MaiChartManagerServlet/[action]Api")]
public class SettingsController : ControllerBase
{
    [HttpGet]
    public SettingsDto GetSettings()
    {
        return new SettingsDto
        {
            MovieCodec = StaticSettings.Config.MovieCodec,
            Yuv420p = StaticSettings.Config.Yuv420p,
            NoScale = StaticSettings.Config.NoScale,
            IgnoreLevel = StaticSettings.Config.IgnoreLevel,
            DisableBga = StaticSettings.Config.DisableBga,
            UiZoom = StaticSettings.Config.UiZoom,
            TargetDpiScale = Browser.TargetDpiScale,
        };
    }

    [HttpPut]
    public void SetSettings([FromBody] SettingsDto dto)
    {
        StaticSettings.Config.MovieCodec = dto.MovieCodec;
        StaticSettings.Config.Yuv420p = dto.Yuv420p;
        StaticSettings.Config.NoScale = dto.NoScale;
        StaticSettings.Config.IgnoreLevel = dto.IgnoreLevel;
        StaticSettings.Config.DisableBga = dto.DisableBga;
        StaticSettings.Config.Save();
    }
}
