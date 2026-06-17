namespace MaiChartManager.Utils;

public static class PathUtils
{
    /// 判断路径是否包含某个目录段（跨平台，忽略分隔符差异，大小写不敏感）
    public static bool ContainsSegment(string? path, string segment)
        => path is not null &&
           path.Replace('\\', '/').Contains($"/{segment}/", System.StringComparison.InvariantCultureIgnoreCase);
}
