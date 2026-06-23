using MaiChartManager.Platform;

namespace MaiChartManager.Platform.Linux;

/// <summary>Linux 上的空操作任务栏进度（无 Windows 任务栏）。</summary>
public class NoopTaskbarProgress : ITaskbarProgress
{
    public void Set(ulong value, ulong total = 100) { }
    public void SetIndeterminate() { }
    public void Clear() { }
}
