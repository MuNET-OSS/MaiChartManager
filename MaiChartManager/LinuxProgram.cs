#if !WINDOWS
using System.Globalization;
using System.Text.Json;

namespace MaiChartManager;

public static class LinuxProgram
{
    public static void Main(string[] args)
    {
        Directory.CreateDirectory(StaticSettings.appData);
        Directory.CreateDirectory(StaticSettings.tempPath);
        InitConfiguration();
        ServerManager.StartApp(false, url => Console.WriteLine($"MaiChartManager backend listening at {url}"));
        Thread.Sleep(Timeout.Infinite);
    }

    /// <summary>
    /// Linux 的最小化无头配置加载。对应 AppMain.InitConfiguration，但去掉了
    /// Sentry / MessageBox / WinForms 相关部分（这些代码在被排除的 AppMain.cs 中）。
    /// </summary>
    private static void InitConfiguration()
    {
        var cfgFilePath = Path.Combine(StaticSettings.appData, "config.json");
        if (File.Exists(cfgFilePath))
        {
            try
            {
                var cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(cfgFilePath));
                if (cfg != null)
                {
                    StaticSettings.Config = cfg;
                }
            }
            catch
            {
                // 配置文件损坏：丢弃并使用默认值继续（进入 OOBE 流程）。
                try { File.Delete(cfgFilePath); }
                catch { /* ignore */ }
            }
        }

        // 应用持久化的语言区域（AppMain.SetLocale 仅限 Windows）。
        var locale = string.IsNullOrWhiteSpace(StaticSettings.Config.Locale) ? "zh" : StaticSettings.Config.Locale;
        if (locale != "zh" && locale != "zh-TW" && locale != "en")
            locale = "zh";

        StaticSettings.CurrentLocale = locale;
        StaticSettings.Config.Locale = locale;

        var culture = locale switch
        {
            "zh" => new CultureInfo("zh-CN"),
            "zh-TW" => new CultureInfo("zh-TW"),
            _ => new CultureInfo("en-US"),
        };
        Locale.Culture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        MuConvert.utils.Utils.SetLocale(new CultureInfo(locale));

        // 如果已持久化有效的游戏路径，则恢复它，使应用以管理模式启动。
        if (!string.IsNullOrWhiteSpace(StaticSettings.Config.GamePath) && Directory.Exists(StaticSettings.Config.GamePath))
        {
            StaticSettings.GamePath = StaticSettings.Config.GamePath;
        }
    }
}
#endif
