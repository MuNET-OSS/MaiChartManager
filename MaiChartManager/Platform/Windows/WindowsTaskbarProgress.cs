#if WINDOWS
using MaiChartManager.Utils;

namespace MaiChartManager.Platform.Windows;

/// <summary>Windows 任务栏进度，委托给基于 Vanara 的 WinUtils 辅助方法。</summary>
public class WindowsTaskbarProgress : ITaskbarProgress
{
    public void Set(ulong value, ulong total = 100) => WinUtils.SetTaskbarProgress(value, total);
    public void SetIndeterminate() => WinUtils.SetTaskbarProgressIndeterminate();
    public void Clear() => WinUtils.ClearTaskbarProgress();
}
#endif
