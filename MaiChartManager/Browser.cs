using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace MaiChartManager;

public sealed partial class Browser : Form
{
    private Uri? loopbackUrl;
    private double _currentZoomFactor = 1.0;
    private static ILogger logger = AppMain.GetLogger<Browser>();

    private static bool IsRunningAsUwp()
    {
        var helpers = new DesktopBridge.Helpers();
        return helpers.IsRunningAsUwp();
    }

    public Browser(string? loopbackUrl = null)
    {
        InitializeComponent();
        if (loopbackUrl != null)
        {
            this.loopbackUrl = new Uri(loopbackUrl);
            Text += $" ({StaticSettings.GamePath})";
        }
        webView21.Source = new Uri("https://mcm.invalid/index.html");
        webView21.DefaultBackgroundColor = Color.Transparent;
        IapManager.BindToForm(this);

        // 限制窗口高度
        var landscape = Screen.AllScreens.FirstOrDefault(s => s.WorkingArea.Width >= s.WorkingArea.Height);
        if (landscape == null)
        {
            // 竖屏显示器时，限制窗口高度不超过显示器宽度的 2/3
            var portrait = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            var maxHeight = portrait.WorkingArea.Width * 2 / 3;
            if (Height > maxHeight)
                Height = maxHeight;
        }
        else
        {
            var maxHeight = landscape.WorkingArea.Height;
            if (Height > maxHeight)
                Height = maxHeight;
        }

        StartPosition = FormStartPosition.Manual;
        Location = WebViewHelper.CalculatePosition(Width, Height);
    }

    public static double TargetDpiScale { get; private set; }

    /// <summary>
    /// 根据配置和屏幕信息计算 WebView2 的 ZoomFactor。
    /// UiZoom=0 表示自动模式：等效宽度 < 1440 时，将目标缩放 clamp 到 100%~150%，
    /// 然后除以系统缩放得到 ZoomFactor。
    /// UiZoom>0 表示用户指定的百分比（相对于物理分辨率的缩放比例），
    /// ZoomFactor = UiZoom / 100.0 / dpiScale。
    /// </summary>
    private double CalculateZoomFactor()
    {
        var dpiScale = DeviceDpi / 96.0;
        var screen = Screen.FromControl(this);
        var physicalWidth = screen.Bounds.Width;
        var effectiveWidth = physicalWidth / dpiScale;

        // 始终计算 auto 模式下的目标缩放，供前端显示
        TargetDpiScale = effectiveWidth >= 1440
            ? dpiScale
            : Math.Clamp(physicalWidth / 1440.0, 1.0, 1.5);

        var uiZoom = StaticSettings.Config.UiZoom;
        if (uiZoom > 0)
        {
            // 用户指定的百分比是 Windows 缩放意义上的，需要除以当前系统缩放
            return uiZoom / 100.0 / dpiScale;
        }

        // auto 模式
        return TargetDpiScale / dpiScale;
    }

    private void ApplyZoomFactor()
    {
        _currentZoomFactor = CalculateZoomFactor();
        webView21.ZoomFactor = _currentZoomFactor;
    }

    private void webView21_CoreWebView2InitializationCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (IsRunningAsUwp())
        {
            webView21.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView21.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        }
        else
        {
            Text += " (Unsupported)";
        }

        WebViewHelper.SetupCoreWebView2(webView21.CoreWebView2, loopbackUrl);

        // 应用缩放
        ApplyZoomFactor();

        webView21.CoreWebView2.PermissionRequested += WebViewHelper.OnPermissionRequested;
        webView21.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
        webView21.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

#if DEBUG
        WindowState = FormWindowState.Minimized;
#endif
    }

    private async void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        var targetUri = new Uri(e.Uri);
        if (targetUri.Host != "mcm.invalid")
        {
            e.Handled = true;
            Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true });
            return;
        }
        var deferral = e.GetDeferral();
        try
        {
            var features = e.WindowFeatures;
            var dpiScale = DeviceDpi / 96.0;
            var width = (int)((features.HasSize ? features.Width : 960) * dpiScale);
            var height = (int)((features.HasSize ? features.Height : 640) * dpiScale);
            var form = new Form
            {
                Text = "MaiChartManager",
                Icon = Icon,
                Width = width,
                Height = height,
                StartPosition = FormStartPosition.Manual,
                Location = WebViewHelper.CalculatePosition(width, height),
            };
            var webView = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill };
            form.Controls.Add(webView);
            form.Show();
            await webView.EnsureCoreWebView2Async(webView21.CoreWebView2.Environment);
            WebViewHelper.SetupCoreWebView2(webView.CoreWebView2, loopbackUrl);
            webView.CoreWebView2.PermissionRequested += WebViewHelper.OnPermissionRequested;
            e.NewWindow = webView.CoreWebView2;
            webView.CoreWebView2.DocumentTitleChanged += (_, _) => form.Text = webView.CoreWebView2.DocumentTitle;

            var prevBorderStyle = form.FormBorderStyle;
            var prevWindowState = form.WindowState;
            var prevBounds = form.Bounds;
            webView.CoreWebView2.ContainsFullScreenElementChanged += (_, _) =>
            {
                if (webView.CoreWebView2.ContainsFullScreenElement)
                {
                    prevBorderStyle = form.FormBorderStyle;
                    prevWindowState = form.WindowState;
                    prevBounds = form.Bounds;
                    if (form.WindowState == FormWindowState.Maximized)
                        form.WindowState = FormWindowState.Normal;
                    form.FormBorderStyle = FormBorderStyle.None;
                    form.WindowState = FormWindowState.Maximized;
                }
                else
                {
                    form.FormBorderStyle = prevBorderStyle;
                    form.WindowState = prevWindowState;
                    if (prevWindowState == FormWindowState.Normal)
                        form.Bounds = prevBounds;
                }
            };

            form.FormClosed += (_, _) => webView.Dispose();
            webView.ZoomFactor = targetUri.AbsolutePath.StartsWith("/touch-test", StringComparison.OrdinalIgnoreCase)
                ? 1.0
                : _currentZoomFactor;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = e.WebMessageAsJson;
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            if (root.GetProperty("type").GetString() != "setZoom") return;

            var value = root.GetProperty("value").GetInt32();
            StaticSettings.Config.UiZoom = value;
            StaticSettings.Config.Save();
            ApplyZoomFactor();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理 WebMessage 失败");
        }
    }


    private async void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        logger.LogInformation("Handle request: {uri}", args.Request.Uri);
        var uri = new UriBuilder(args.Request.Uri)
        {
            Host = loopbackUrl.Host
        };
        using var client = new HttpClient();
        var req = new HttpRequestMessage
        {
            Method = HttpMethod.Parse(args.Request.Method),
            RequestUri = uri.Uri,
            Content = args.Request.Content != null ? new StreamContent(args.Request.Content) : null,
        };
        foreach (var header in args.Request.Headers)
        {
            req.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        var response = await client.SendAsync(req);
        args.Response = webView21.CoreWebView2.Environment.CreateWebResourceResponse(await response.Content.ReadAsStreamAsync(), (int)response.StatusCode, response.ReasonPhrase,
            response.Headers.ToString());
    }


    private void Browser_FormClosed(object sender, FormClosedEventArgs e)
    {
        webView21.Dispose();
        AppMain.BrowserWin = null;
        AppLifecycleManager.CheckShouldExit();
    }

    public async void InjectBackendUrl(string url)
    {
        loopbackUrl = new Uri(url);
        Text = $"MaiChartManager ({StaticSettings.GamePath})";
        await webView21.EnsureCoreWebView2Async();
        // 为后续文档创建注入 backendUrl（导航刷新时生效）
        await webView21.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync($"globalThis.backendUrl = `{url}`");
        // 直接在当前页面设置 backendUrl，防止 PostWebMessageAsString 在 listener 注册前发送导致丢失
        await webView21.CoreWebView2.ExecuteScriptAsync($"globalThis.backendUrl = `{url}`");
        // PostWebMessage 仍然发送，用于通知已挂载的 listener 更新 apiClient.baseUrl
        webView21.CoreWebView2.PostWebMessageAsString(url);
    }
}