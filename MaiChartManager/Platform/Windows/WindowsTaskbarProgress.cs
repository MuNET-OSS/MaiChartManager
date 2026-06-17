#if WINDOWS
using MaiChartManager.Utils;

namespace MaiChartManager.Platform.Windows;

/// <summary>Windows taskbar progress, delegating to the existing Vanara-backed WinUtils helpers.</summary>
public class WindowsTaskbarProgress : ITaskbarProgress
{
    public void Set(ulong value, ulong total = 100) => WinUtils.SetTaskbarProgress(value, total);
    public void SetIndeterminate() => WinUtils.SetTaskbarProgressIndeterminate();
    public void Clear() => WinUtils.ClearTaskbarProgress();
}
#endif
