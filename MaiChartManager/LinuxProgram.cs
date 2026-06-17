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
    /// Minimal, headless config load for Linux. Mirrors AppMain.InitConfiguration but
    /// without the Sentry / MessageBox / WinForms parts (those live in the excluded AppMain.cs).
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
                // Corrupted config: drop it and continue with defaults (OOBE flow).
                try { File.Delete(cfgFilePath); }
                catch { /* ignore */ }
            }
        }

        // Apply persisted locale (AppMain.SetLocale is Windows-only).
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

        // If a valid game path was persisted, restore it so the app starts in management mode.
        if (!string.IsNullOrWhiteSpace(StaticSettings.Config.GamePath) && Directory.Exists(StaticSettings.Config.GamePath))
        {
            StaticSettings.GamePath = StaticSettings.Config.GamePath;
        }
    }
}
#endif
