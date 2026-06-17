#if WINDOWS
using System.Windows.Forms;

namespace MaiChartManager.Platform.Windows;

/// <summary>
/// Windows app-shell, delegating to AppLifecycleManager / AppMain / Browser /
/// Application / UWP StartupTask exactly as the original controllers did.
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
            var startupTask = await Windows.ApplicationModel.StartupTask.GetAsync("MaiChartManagerStartupId");
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

    public void ReloadLocale(string locale) => AppMain.SetLocale(locale);

    public double GetTargetDpiScale() => Browser.TargetDpiScale;

    public void ExitApp() => Application.Exit();
}
#endif
