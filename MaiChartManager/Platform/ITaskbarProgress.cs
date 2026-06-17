namespace MaiChartManager.Platform;

public interface ITaskbarProgress
{
    void Set(ulong value, ulong total = 100);
    void SetIndeterminate();
    void Clear();
}
