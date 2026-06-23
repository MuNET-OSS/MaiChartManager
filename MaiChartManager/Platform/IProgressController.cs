namespace MaiChartManager.Platform;

public interface IProgressController
{
    /// 创建并显示一个可取消的批量进度会话；Dispose 时关闭。
    IProgressSession Begin(string title, string description, string cancelMessage);
}

public interface IProgressSession : IDisposable
{
    void Report(ulong value, ulong total, string? detail = null);
    bool IsCancelled { get; }
}
