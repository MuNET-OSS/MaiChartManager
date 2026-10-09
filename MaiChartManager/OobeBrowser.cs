using System.ComponentModel;
using Microsoft.Web.WebView2.Core;

namespace MaiChartManager;

public sealed class OobeBrowser : Form
{
    private Uri? loopbackUrl;
    private readonly Microsoft.Web.WebView2.WinForms.WebView2 webView21;

    public OobeBrowser(string? loopbackUrl = null, string hash = "/oobe")
    {
        if (loopbackUrl != null) this.loopbackUrl = new Uri(loopbackUrl);

        webView21 = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill };
        Controls.Add(webView21);

        Text = "MaiChartManager";
        ClientSize = new Size(1200, 1000);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimumSize = new Size(480, 480);

        var rm = new ComponentResourceManager(typeof(Launcher));
        Icon = (Icon?)rm.GetObject("notifyIcon1.Icon");

        webView21.CoreWebView2InitializationCompleted += OnCoreWebView2InitializationCompleted;
        FormClosed += (_, _) =>
        {
            webView21.Dispose();
            AppMain.OobeBrowser = null;
            AppMain.ActiveForm = null;
            AppLifecycleManager.CheckShouldExit();
        };

        AppMain.ActiveForm = this;

        webView21.Source = new Uri($"https://mcm.invalid/index.html#{hash}");
        webView21.DefaultBackgroundColor = Color.Transparent;

        StartPosition = FormStartPosition.Manual;
        Location = WebViewHelper.CalculatePosition(Width, Height);
    }

    private void OnCoreWebView2InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        WebViewHelper.SetupCoreWebView2(webView21.CoreWebView2, loopbackUrl);
        webView21.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        webView21.CoreWebView2.PermissionRequested += WebViewHelper.OnPermissionRequested;
    }

    public async void InjectBackendUrl(string url)
    {
        loopbackUrl = new Uri(url);
        await webView21.EnsureCoreWebView2Async();
        await webView21.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync($"globalThis.backendUrl = `{url}`");
        await webView21.CoreWebView2.ExecuteScriptAsync($"globalThis.backendUrl = `{url}`");
        webView21.CoreWebView2.PostWebMessageAsString(url);
    }
}
