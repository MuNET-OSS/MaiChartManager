using MaiChartManager.Platform;
using Microsoft.Extensions.Logging;

namespace MaiChartManager.Platform.Linux;

/// <summary>
/// Headless app-shell for Linux. Window / tray / startup-task operations are no-ops;
/// Phase 3 Photino wires the real native behaviour.
/// </summary>
public class HeadlessAppShell(ILogger<HeadlessAppShell> logger) : IAppShell
{
    public void ShowBrowser(string loopbackUrl)
        => logger.LogInformation("ShowBrowser (headless no-op): {Url}", loopbackUrl);

    public void GoToModeSwitch(string loopbackUrl, string hash = "/set-mode")
        => logger.LogInformation("GoToModeSwitch (headless no-op): {Url}{Hash}", loopbackUrl, hash);

    public void CloseOobeBrowser()
        => logger.LogInformation("CloseOobeBrowser (headless no-op)");

    public void InjectOobeBackendUrl(string loopbackUrl)
        => logger.LogInformation("InjectOobeBackendUrl (headless no-op): {Url}", loopbackUrl);

    public void UpdateMainWindowTitle(string gamePath)
        => logger.LogDebug("UpdateMainWindowTitle (headless no-op): {GamePath}", gamePath);

    public void DisposeTrayIcon()
        => logger.LogDebug("DisposeTrayIcon (headless no-op)");

    public Task<bool> SetStartupEnabledAsync(bool enabled)
    {
        logger.LogInformation("SetStartupEnabledAsync (headless no-op): {Enabled}", enabled);
        return Task.FromResult(false);
    }

    public void ReloadLocale(string locale)
        => logger.LogDebug("ReloadLocale (headless no-op): {Locale}", locale);

    public double GetTargetDpiScale() => 1.0;

    public void ExitApp()
        => logger.LogInformation("ExitApp (headless no-op)");
}
