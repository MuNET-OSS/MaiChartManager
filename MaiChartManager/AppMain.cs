using System.Diagnostics;
using System.Globalization;
using SingleInstanceCore;
using System.Text.Json;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using MaiChartManager.Utils;
using Microsoft.Web.WebView2.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Xabe.FFmpeg;

namespace MaiChartManager;

public partial class AppMain : ISingleInstance
{
    public static Browser? BrowserWin { get; set; }
    public static OobeBrowser? OobeBrowser { get; set; }
    public static Form? ActiveForm { get; set; }
    /// <summary>
    /// UI 线程上下文
    /// </summary>
    public static SynchronizationContext? UiContext { get; private set; }

    private static ILoggerFactory _loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.AddConsole();
        builder.SetMinimumLevel(LogLevel.Information);
    });

    public static ILogger GetLogger<T>() => _loggerFactory.CreateLogger<T>();

    public static void InitConfiguration(bool noPopup = false)
    {
        SentrySdk.Init(o =>
            {
                // Tells which project in Sentry to send events to:
                o.Dsn = "https://be7a9ae3a9a88f4660737b25894b3c20@sentry.c5y.moe/3";
                // Set TracesSampleRate to 1.0 to capture 100% of transactions for tracing.
                // We recommend adjusting this value in production.
                o.TracesSampleRate = 0.5;
# if DEBUG
                o.Environment = "development";
# endif
            }
        );

        var cfgFilePath = Path.Combine(StaticSettings.appData, "config.json");
        if (File.Exists(cfgFilePath))
        {
            try
            {
                var cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(Path.Combine(StaticSettings.appData, "config.json")));
                if (cfg == null)
                {
                    throw new Exception("config.json is null");
                }
                StaticSettings.Config = cfg;
            }
            catch (Exception e)
            {
                SentrySdk.CaptureException(e, s => s.TransactionName = "读取配置文件");
                if (!noPopup)
                    MessageBox.Show(Locale.ConfigCorrupted, Locale.ConfigCorruptedTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                File.Delete(cfgFilePath);
            }
        }
    }

    public void Run()
    {
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            ApplicationConfiguration.Initialize();
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            UiContext = SynchronizationContext.Current;
            FFmpeg.SetExecutablesPath(StaticSettings.exeDir);
            VideoConvert.CheckHardwareAcceleration();

            Directory.CreateDirectory(StaticSettings.appData);
            Directory.CreateDirectory(StaticSettings.tempPath);
            InitConfiguration();

            // 初始化语言设置
            if (StaticSettings.Config.Locale == null)
            {
                // 首次启动，从系统语言检测
                var systemCulture = CultureInfo.CurrentUICulture;
                var cultureName = systemCulture.Name;

                // 检测语言：简体中文、繁体中文、英文
                if (cultureName.StartsWith("zh-CN") || cultureName.StartsWith("zh-Hans") || cultureName == "zh")
                {
                    StaticSettings.CurrentLocale = "zh";
                }
                else if (cultureName.StartsWith("zh-TW") || cultureName.StartsWith("zh-HK") || cultureName.StartsWith("zh-Hant"))
                {
                    StaticSettings.CurrentLocale = "zh-TW";
                }
                else
                {
                    // 非中文系统默认英文
                    StaticSettings.CurrentLocale = "en";
                }

                StaticSettings.Config.Locale = StaticSettings.CurrentLocale;
            }
            else
            {
                StaticSettings.CurrentLocale = StaticSettings.Config.Locale;
            }

            // 设置 Locale 资源管理器的 Culture（这会影响所有线程）
            var culture = StaticSettings.CurrentLocale switch
            {
                "zh" => new System.Globalization.CultureInfo("zh-CN"),
                "zh-TW" => new System.Globalization.CultureInfo("zh-TW"),
                _ => new System.Globalization.CultureInfo("en-US")
            };
            Locale.Culture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            string? availableVersion = null;
            try
            {
                availableVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
            }
            catch (WebView2RuntimeNotFoundException) { }

#if !DEBUG
            if (availableVersion == null && !IsFromStartup)
            {
                var answer = MessageBox.Show(Locale.WebView2NotInstalled, Locale.WebView2NotInstalledTitle, MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (answer == DialogResult.Yes)
                {
                    Process.Start(new ProcessStartInfo(Path.Combine(StaticSettings.exeDir, "MicrosoftEdgeWebview2Setup.exe")) { UseShellExecute = true });
                }
            }
#endif

            IapManager.Init();

            // Validate saved GamePath — if invalid, clear it to trigger OOBE
            if (!string.IsNullOrEmpty(StaticSettings.Config.GamePath))
            {
                StaticSettings.GamePath = StaticSettings.Config.GamePath;
                if (!Directory.Exists(StaticSettings.StreamingAssets))
                {
                    StaticSettings.Config.GamePath = "";
                    StaticSettings.GamePath = "";
                    StaticSettings.Config.Save();
                }
            }

            // 提前创建 OOBE 窗口，server 就绪后注入 backendUrl
            if (string.IsNullOrEmpty(StaticSettings.Config.GamePath) && availableVersion != null)
            {
                OobeBrowser = new OobeBrowser();
                OobeBrowser.Show();
                ServerManager.StartApp(false, (url) =>
                {
                    UiContext?.Post(_ => OobeBrowser?.InjectBackendUrl(url), null);
                });
            }
            else if (availableVersion != null && !StaticSettings.Config.Export)
            {
                BrowserWin = new Browser();
                BrowserWin.Show();
                ServerManager.StartApp(false, (url) =>
                {
                    UiContext?.Post(_ => BrowserWin?.InjectBackendUrl(url), null);
                });
            }
            else if (availableVersion != null && IsFromStartup)
            {
                // export mode + startup: show tray icon, no browser window
                ServerManager.StartApp(true);
                AppLifecycleManager.ShowTrayIcon();
            }
            else if (availableVersion != null)
            {
                // export mode + manual launch: show OOBE at mode select page
                OobeBrowser = new OobeBrowser(hash: "/server");
                OobeBrowser.Show();
                ServerManager.StartApp(true, (url) =>
                {
                    UiContext?.Post(_ => OobeBrowser?.InjectBackendUrl(url), null);
                });
            }
            else
            {
                // 这里不用 Show，可能是在托盘的
                var launcher = new Launcher(); // prevent GC by assigning to ActiveForm
                ActiveForm = launcher;
            }
            Application.Run();
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            MessageBox.Show(e.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            throw;
        }
    }

    private static bool? _isFromStartup;

    public static bool IsFromStartup
    {
        get
        {
            if (_isFromStartup.HasValue)
                return _isFromStartup.Value;
            try
            {
                var aeArgs = AppInstance.GetActivatedEventArgs();
                _isFromStartup = aeArgs?.Kind == ActivationKind.StartupTask;
                return _isFromStartup.Value;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                SentrySdk.CaptureException(e);
            }

            _isFromStartup = false;
            return false;
        }
    }

    public void OnInstanceInvoked(string[] args)
    {
        var url = ServerManager.IsRunning ? ServerManager.GetLoopbackUrl() : null;
        if (url != null)
            AppLifecycleManager.ShowBrowser(url);
    }

    public static void SetLocale(string locale)
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
    }
}
