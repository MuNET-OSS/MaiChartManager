using MaiChartManager.Platform;
using Microsoft.Extensions.Logging;

namespace MaiChartManager.Platform.Linux;

/// <summary>
/// Linux 无头模式的对话框服务。当前无头宿主不支持原生文件选择对话框；
/// 第三阶段将替换为基于 Photino 的对话框。
/// </summary>
public class HeadlessDialogService(ILogger<HeadlessDialogService> logger) : IDesktopDialogService
{
    public string? PickFolder(string? title = null)
    {
        logger.LogWarning("PickFolder is not supported on this platform (headless). title={Title}", title);
        return null;
    }

    public string? PickFile(string? title = null, string? filter = null)
    {
        logger.LogWarning("PickFile is not supported on this platform (headless). title={Title}", title);
        return null;
    }

    public bool Confirm(string message, string title, bool defaultResult = false)
    {
        logger.LogWarning("Confirm dialog not supported on this platform (headless), returning default {Default}. title={Title} message={Message}",
            defaultResult, title, message);
        return defaultResult;
    }

    public void ShowError(string message, string title)
    {
        logger.LogError("ShowError ({Title}): {Message}", title, message);
    }
}
