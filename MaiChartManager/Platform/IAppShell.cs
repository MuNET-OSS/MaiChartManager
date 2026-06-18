namespace MaiChartManager.Platform;

/// <summary>
/// Web 控制器使用的桌面外壳 / 原生窗口操作接口。
/// 在 Windows 上委托给 WinForms（AppLifecycleManager / AppMain / Browser / Application / UWP StartupTask）。
/// 在 Linux 上为空操作或返回默认值（第三阶段 Photino 会接入真正的原生行为）。
/// </summary>
public interface IAppShell
{
    /// <summary>显示（或聚焦并刷新）指定回环地址的主浏览器窗口。</summary>
    void ShowBrowser(string loopbackUrl);

    /// <summary>切换到指定回环地址的 OOBE / 模式切换窗口。</summary>
    void GoToModeSwitch(string loopbackUrl, string hash = "/set-mode");

    /// <summary>关闭并释放 OOBE 浏览器窗口（若存在）。</summary>
    void CloseOobeBrowser();

    /// <summary>向 OOBE 浏览器窗口注入（可能已更新的）后端地址。</summary>
    void InjectOobeBackendUrl(string loopbackUrl);

    /// <summary>根据当前游戏路径更新主窗口标题。</summary>
    void UpdateMainWindowTitle(string gamePath);

    /// <summary>显示 / 隐藏托盘图标（导出模式 + 开机启动模式）。</summary>
    void DisposeTrayIcon();

    /// <summary>启用或禁用系统"开机自启"任务，成功返回 true。</summary>
    Task<bool> SetStartupEnabledAsync(bool enabled);

    /// <summary>将语言区域变更应用到原生 UI（窗口装饰、内嵌库等）。</summary>
    void ReloadLocale(string locale);

    /// <summary>主窗口的 DPI 缩放比例，用于报告默认 UI 缩放值。</summary>
    double GetTargetDpiScale();

    /// <summary>退出整个应用程序。</summary>
    void ExitApp();
}
