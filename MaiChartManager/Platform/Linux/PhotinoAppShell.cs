#if !WINDOWS
using MaiChartManager.Platform;
using Microsoft.Extensions.Logging;

namespace MaiChartManager.Platform.Linux;

/// <summary>
/// Linux 下基于 Photino 单窗口的应用外壳实现。
/// Windows 是多窗口（OOBE 窗口 + 主窗口），Linux 只有一个 Photino 窗口，
/// 因此「打开主界面 / 切换模式」等操作统一转化为对同一个窗口的导航（Load）。
/// 托盘 / 开机启动等 Windows 专属能力在 Linux 上为空操作。
/// </summary>
public class PhotinoAppShell(ILogger<PhotinoAppShell> logger) : IAppShell
{
    /// 在窗口的 UI 线程上把窗口导航到指定地址（Photino 的 Load 需在 UI 线程调用）。
    private void Navigate(string targetUrl)
    {
        var window = PhotinoWindowHolder.Current;
        if (window is null)
        {
            logger.LogWarning("Photino 窗口尚未就绪，无法导航到 {Url}", targetUrl);
            return;
        }

        window.Invoke(() =>
        {
            try
            {
                window.Load(new Uri(targetUrl));
            }
            catch (Exception e)
            {
                logger.LogError(e, "导航到 {Url} 失败", targetUrl);
            }
        });
    }

    /// 拼出 SPA 的 hash 路由地址，例如 http://127.0.0.1:port/#/oobe
    private static string HashUrl(string loopbackUrl, string hash)
        => $"{loopbackUrl.TrimEnd('/')}/#{hash}";

    // 打开主界面：导航到 loopback 根路由，SPA 进入主界面（此时 GamePath 已配置）。
    public void ShowBrowser(string loopbackUrl) => Navigate(loopbackUrl);

    // 切换模式：导航到对应 hash 路由（单窗口，等价于在当前窗口换路由）。
    public void GoToModeSwitch(string loopbackUrl, string hash = "/set-mode")
        => Navigate(HashUrl(loopbackUrl, hash));

    // 单窗口模型下没有独立的 OOBE 窗口，ShowBrowser 已经完成了导航，这里无需操作。
    public void CloseOobeBrowser()
        => logger.LogDebug("CloseOobeBrowser：Linux 单窗口，无需关闭独立 OOBE 窗口");

    // 局域网（export）模式下后端会重启并换端口，需要把窗口导航到新的 OOBE 地址以重新连接。
    public void InjectOobeBackendUrl(string loopbackUrl)
        => Navigate(HashUrl(loopbackUrl, "/oobe"));

    public void UpdateMainWindowTitle(string gamePath)
    {
        var window = PhotinoWindowHolder.Current;
        if (window is null) return;
        window.Invoke(() =>
        {
            try
            {
                window.SetTitle($"MaiChartManager ({gamePath})");
            }
            catch (Exception e)
            {
                logger.LogError(e, "设置窗口标题失败");
            }
        });
    }

    // Linux 不做托盘。
    public void DisposeTrayIcon() => logger.LogDebug("DisposeTrayIcon：Linux 无托盘，空操作");

    // Linux 不做开机自启。
    public Task<bool> SetStartupEnabledAsync(bool enabled)
    {
        logger.LogInformation("SetStartupEnabledAsync：Linux 不支持开机自启，返回 false（请求值 {Enabled}）", enabled);
        return Task.FromResult(false);
    }

    // 语言切换由前端通过接口自行处理；Linux 原生窗口无需额外刷新。
    public void ReloadLocale(string locale) => logger.LogDebug("ReloadLocale：Linux 无需刷新原生 UI（{Locale}）", locale);

    // Linux 暂不实现 DPI 缩放上报，返回 1.0。
    public double GetTargetDpiScale() => 1.0;

    public void ExitApp()
    {
        var window = PhotinoWindowHolder.Current;
        if (window is null)
        {
            Environment.Exit(0);
            return;
        }
        window.Invoke(() =>
        {
            try { window.Close(); }
            catch (Exception e) { logger.LogError(e, "关闭窗口失败"); Environment.Exit(0); }
        });
    }
}

#endif
