using System.Diagnostics;
using SingleInstanceCore;
using System.Text.Json;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using MaiChartManager.Utils;
using Microsoft.Web.WebView2.Core;
using Xabe.FFmpeg;

namespace MaiChartManager;

public class AppMain : ISingleInstance
{
    public const string Version = "1.6.0";
    public static Browser? BrowserWin { get; set; }

    private Launcher _launcher;

    private static ILoggerFactory _loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.AddConsole();
        builder.SetMinimumLevel(LogLevel.Information);
    });

    public static ILogger GetLogger<T>() => _loggerFactory.CreateLogger<T>();

    public void Run()
    {
        try
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

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            ApplicationConfiguration.Initialize();
            FFmpeg.SetExecutablesPath(StaticSettings.exeDir);
            VideoConvert.CheckHardwareAcceleration();

            Directory.CreateDirectory(StaticSettings.appData);
            Directory.CreateDirectory(StaticSettings.tempPath);
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
                    MessageBox.Show("看起来配置文件损坏了…已经重置配置文件", "不太对劲", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    File.Delete(cfgFilePath);
                }
            }

            string? availableVersion = null;
            try
            {
                availableVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
            }
            catch (WebView2RuntimeNotFoundException) { }

            if (availableVersion == null && !IsFromStartup)
            {
                var answer = MessageBox.Show("WebView2 运行时未安装，在启动之后可能会白屏…\n\n如果你觉得你已经安装了 WebView2 运行时，请尝试重启电脑。\n\n要尝试安装一下 WebView2 吗？", "WebView2 未安装", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (answer == DialogResult.Yes)
                {
                    Process.Start(new ProcessStartInfo(Path.Combine(StaticSettings.exeDir, "MicrosoftEdgeWebview2Setup.exe")) { UseShellExecute = true });
                }
            }

            IapManager.Init();

            _launcher = new Launcher();

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
        _launcher.ShowWindow();
    }
}