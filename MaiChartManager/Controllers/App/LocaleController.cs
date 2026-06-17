using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace MaiChartManager.Controllers.App;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api")]
public class LocaleController(StaticSettings settings, ILogger<LocaleController> logger, MaiChartManager.Platform.IAppShell appShell) : ControllerBase
{
    [HttpGet]
    public string GetCurrentLocale()
    {
        return StaticSettings.CurrentLocale;
    }

    [HttpPost]
    public void SetLocale([FromBody] string locale)
    {
        if (locale != "zh" && locale != "zh-TW" && locale != "en")
        {
            throw new ArgumentException("Invalid locale. Must be 'zh', 'zh-TW', or 'en'");
        }

        StaticSettings.CurrentLocale = locale;
        StaticSettings.Config.Locale = locale;

        // 设置 Locale 资源管理器的 Culture（这会影响所有线程）
        var culture = locale switch
        {
            "zh" => new CultureInfo("zh-CN"),
            "zh-TW" => new CultureInfo("zh-TW"),
            _ => new CultureInfo("en-US"),
        };
        Locale.Culture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        // 给外部依赖库设置Locale
        MuConvert.utils.Utils.SetLocale(new CultureInfo(locale));

        StaticSettings.Config.Save();

        // 刷新原生 UI（Windows: 窗口/托盘；Linux: no-op）
        appShell.ReloadLocale(locale);
    }
}