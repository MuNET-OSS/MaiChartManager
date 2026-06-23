#if !WINDOWS
using Photino.NET;

namespace MaiChartManager.Platform.Linux;

/// <summary>
/// 持有当前 Photino 主窗口引用，供 Linux 平台服务（对话框等）使用。
/// </summary>
public static class PhotinoWindowHolder
{
    public static PhotinoWindow? Current { get; set; }
}
#endif
