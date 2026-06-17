using MaiChartManager.Platform;

namespace MaiChartManager.Platform.Linux;

/// <summary>No-op taskbar progress for Linux (no Windows taskbar).</summary>
public class NoopTaskbarProgress : ITaskbarProgress
{
    public void Set(ulong value, ulong total = 100) { }
    public void SetIndeterminate() { }
    public void Clear() { }
}
