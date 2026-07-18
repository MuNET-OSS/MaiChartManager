#if !WINDOWS
using System.Globalization;
using System.Text.Json;
using Photino.NET;
using FFMpegCore;

namespace MaiChartManager;

public static class LinuxProgram
{
    public static async Task Main(string[] args)
    {
        Directory.CreateDirectory(StaticSettings.appData);
        Directory.CreateDirectory(StaticSettings.tempPath);
        InitConfiguration();
        ConfigureFfmpeg();

        // 启动进程内 Kestrel：loopback + 伺服 SPA（wwwroot）+ API 同源，但不开 LAN 端口。
        // Kestrel 在后台线程运行（StartApp 内部 Task.Run），主线程留给 Photino 开窗。
        var serverReady = new ManualResetEventSlim(false);
        string? backendUrl = null;
        var serverTask = ServerManager.StartApp(export: false, serveSpa: true, onStart: url =>
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

        // 决定初始路由（对齐 Windows AppMain 的逻辑）：
        // 未配置有效游戏目录时加载 OOBE 引导页（#/oobe），否则加载主界面（根路由）。
        // 直接加载主界面会让 SPA 立刻调用依赖 GamePath 的接口，导致一连串异常。
        var startUrl = string.IsNullOrEmpty(StaticSettings.GamePath)
            ? $"{backendUrl.TrimEnd('/')}/#/oobe"
            : backendUrl;

#if BACKENDONLY
        await serverTask;
#else
        // Photino 必须在主线程创建并显示窗口。Linux 下底层走系统 WebKitGTK。
        // 加载 Kestrel 的 loopback 地址：SPA 与 API 同源，前端无需注入 backendUrl。
        var window = new PhotinoWindow()
            .SetTitle("MaiChartManager")
            .SetUseOsDefaultSize(false)
            .SetSize(1600, 800)
            .Center()
            .Load(new Uri(startUrl));

        // 把窗口实例交给平台服务持有者，供 Linux 的对话框服务与应用外壳（导航/标题等）使用。
        Platform.Linux.PhotinoWindowHolder.Current = window;

        // 处理前端发来的「开新窗口」请求（预览谱面等）。WebKitGTK 不支持 window.open，
        // 前端改为 window.external.sendMessage 通知宿主，由宿主开一个内置 webview 子窗口。
        window.RegisterWebMessageReceivedHandler((sender, message) => HandleWebMessage(window, message));

        window.WaitForClose();
#endif
    }

    /// <summary>
    /// 处理前端通过 window.external.sendMessage 发来的消息。
    /// 目前支持 { type:"open-window", url, title, width, height }：开一个内置 webview 子窗口加载 url。
    /// </summary>
    private static void HandleWebMessage(PhotinoWindow parent, string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp) || typeProp.GetString() != "open-window") return;

            var url = root.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(url)) return;
            var title = root.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "MaiChartManager" : "MaiChartManager";
            var width = root.TryGetProperty("width", out var wProp) && wProp.TryGetInt32(out var w) ? w : 960;
            var height = root.TryGetProperty("height", out var hProp) && hProp.TryGetInt32(out var h) ? h : 640;

            // 在宿主 UI 线程上创建子窗口（消息回调本身就在 UI 线程）。
            // child.WaitForClose() 会进入一个嵌套的 GTK 事件循环：父窗口仍可交互，子窗口关闭后返回。
            var child = new PhotinoWindow(parent)
                .SetTitle(title)
                .SetUseOsDefaultSize(false)
                .SetSize(width, height)
                .Center()
                .Load(new Uri(url));
            child.WaitForClose();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"处理 WebMessage 失败：{e}");
        }
    }

    /// <summary>
    /// 配置 FFMpegCore 使用系统 ffmpeg/ffprobe。
    /// Windows 版在 AppMain 里指向内置的 ffmpeg.exe；Linux 不内置，改用系统 PATH 里的 ffmpeg 所在目录。
    /// FFMpegCore 用参数数组传给 ffmpeg（无引号问题），按 OS 自动补可执行名后缀。
    /// 公开以便 CLI 等其它 Linux 入口复用。
    /// </summary>
    public static void ConfigureFfmpeg()
    {
        var dir = ResolveExecutableDir("ffmpeg") ?? "/usr/bin";
        GlobalFFOptions.Configure(o =>
        {
            o.BinaryFolder = dir;
            o.TemporaryFilesFolder = StaticSettings.tempPath;
        });
        // 检测硬件加速（与 Windows 的 AppMain 一致，失败不影响主流程）
        _ = MaiChartManager.Utils.VideoConvert.CheckHardwareAcceleration();
    }

    /// 在 $PATH 中查找可执行文件所在目录，找不到返回 null。
    private static string? ResolveExecutableDir(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        foreach (var d in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(d)) continue;
            if (File.Exists(Path.Combine(d, exe))) return d;
        }
        return null;
    }

    /// <summary>
    /// Linux 的最小化无头配置加载。对应 AppMain.InitConfiguration，但去掉了
    /// Sentry / MessageBox / WinForms 相关部分（这些代码在被排除的 AppMain.cs 中）。
    /// 公开以便 CLI 等其它 Linux 入口复用。
    /// </summary>
    public static void InitConfiguration()
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
