namespace MaiChartManager.Platform;

/// <summary>
/// Desktop-shell / native window operations used by the web controllers.
/// On Windows these delegate to WinForms (AppLifecycleManager / AppMain / Browser / Application / UWP StartupTask).
/// On Linux they no-op or return defaults (Phase 3 Photino wires real behaviour).
/// </summary>
public interface IAppShell
{
    /// <summary>Show (or focus + refresh) the main browser window for the given loopback url.</summary>
    void ShowBrowser(string loopbackUrl);

    /// <summary>Switch to the OOBE / mode-switch window for the given loopback url.</summary>
    void GoToModeSwitch(string loopbackUrl, string hash = "/set-mode");

    /// <summary>Close / dispose the OOBE browser window if present.</summary>
    void CloseOobeBrowser();

    /// <summary>Inject a (possibly new) backend url into the OOBE browser window.</summary>
    void InjectOobeBackendUrl(string loopbackUrl);

    /// <summary>Update the main window title to reflect the current game path.</summary>
    void UpdateMainWindowTitle(string gamePath);

    /// <summary>Show / hide the tray icon (export + startup mode).</summary>
    void DisposeTrayIcon();

    /// <summary>Enable or disable the OS "run at startup" task. Returns true on success.</summary>
    Task<bool> SetStartupEnabledAsync(bool enabled);

    /// <summary>Apply a locale change to native UI (window chrome, embedded libs).</summary>
    void ReloadLocale(string locale);

    /// <summary>The DPI scale of the main window, used to report UI zoom defaults.</summary>
    double GetTargetDpiScale();

    /// <summary>Exit the whole application.</summary>
    void ExitApp();
}
