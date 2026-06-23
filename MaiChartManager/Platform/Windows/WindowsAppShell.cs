#if WINDOWS
using System.Windows.Forms;
using Windows.ApplicationModel;

namespace MaiChartManager.Platform.Windows;

/// <summary>
/// Windows 应用外壳，委托给 AppLifecycleManager / AppMain / Browser /
/// Application / UWP StartupTask，与原来各控制器的实现完全一致。
/// </summary>
public class WindowsAppShell : IAppShell
{
    public void ShowBrowser(string loopbackUrl) => AppLifecycleManager.ShowBrowser(loopbackUrl);

    public void GoToModeSwitch(string loopbackUrl, string hash = "/set-mode")
        => AppLifecycleManager.GoToModeSwitch(loopbackUrl, hash);

    public void CloseOobeBrowser()
    {
        AppMain.UiContext?.Post(_ =>
        {
            AppMain.OobeBrowser?.Dispose();
            AppMain.OobeBrowser = null;
        }, null);
    }

    public void InjectOobeBackendUrl(string loopbackUrl)
    {
        AppMain.UiContext?.Post(_ => AppMain.OobeBrowser?.InjectBackendUrl(loopbackUrl), null);
    }

    public void UpdateMainWindowTitle(string gamePath)
    {
        AppMain.UiContext?.Post(_ =>
        {
            if (AppMain.BrowserWin is { IsDisposed: false })
                AppMain.BrowserWin.Text = $"MaiChartManager ({gamePath})";
        }, null);
    }

    public void DisposeTrayIcon() => AppLifecycleManager.DisposeTrayIcon();

    public async Task<bool> SetStartupEnabledAsync(bool enabled)
    {
        try
        {
            var startupTask = await StartupTask.GetAsync("MaiChartManagerStartupId");
            if (enabled)
                await startupTask.RequestEnableAsync();
            else
                startupTask.Disable();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void ReloadLocale(string locale)
    {
        // 语言区域状态（CurrentLocale/Config/Culture）已由 LocaleController 以平台无关的方式应用。
        // WinForms 外壳目前不需要额外刷新任何内容。
    }

    public double GetTargetDpiScale() => Browser.TargetDpiScale;

    public void ExitApp() => Application.Exit();
}
#endif
