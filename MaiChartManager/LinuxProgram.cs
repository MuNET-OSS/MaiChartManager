#if !WINDOWS
using System.Globalization;
using System.Text.Json;
using Photino.NET;

namespace MaiChartManager;

public static class LinuxProgram
{
    public static void Main(string[] args)
    {
        Directory.CreateDirectory(StaticSettings.appData);
        Directory.CreateDirectory(StaticSettings.tempPath);
        InitConfiguration();

        // 启动进程内 Kestrel：loopback + 伺服 SPA（wwwroot）+ API 同源，但不开 LAN 端口。
        // Kestrel 在后台线程运行（StartApp 内部 Task.Run），主线程留给 Photino 开窗。
        var serverReady = new ManualResetEventSlim(false);
        string? backendUrl = null;
        ServerManager.StartApp(export: false, serveSpa: true, onStart: url =>
        {
            backendUrl = url;
            serverReady.Set();
        });

        // 等待后端就绪拿到 loopback url，超时 30 秒视为启动失败。
        if (!serverReady.Wait(TimeSpan.FromSeconds(30)) || string.IsNullOrWhiteSpace(backendUrl))
        {
            Console.Error.WriteLine("后端在 30 秒内未能就绪，退出。");
            Environment.Exit(1);
            return;
        }

        Console.WriteLine($"MaiChartManager backend listening at {backendUrl}");

        // Photino 必须在主线程创建并显示窗口。Linux 下底层走系统 WebKitGTK。
        // 加载 Kestrel 的 loopback 根地址：SPA 与 API 同源，前端无需注入 backendUrl。
        var window = new PhotinoWindow()
            .SetTitle("MaiChartManager")
            .SetUseOsDefaultSize(false)
            .SetSize(1280, 800)
            .Center()
            .Load(new Uri(backendUrl));

        // 把窗口实例交给平台服务持有者，供 Linux 的对话框服务（PhotinoDialogService）使用。
        Platform.Linux.PhotinoWindowHolder.Current = window;

        window.WaitForClose();
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
