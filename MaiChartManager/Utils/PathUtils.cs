namespace MaiChartManager.Utils;

public static class PathUtils
{
    /// 判断路径是否包含某个目录段（跨平台，忽略分隔符差异，大小写不敏感）
    public static bool ContainsSegment(string? path, string segment)
        => path is not null &&
           path.Replace('\\', '/').Contains($"/{segment}/", System.StringComparison.InvariantCultureIgnoreCase);

    /// <summary>
    /// 大小写不敏感地逐段解析路径，返回文件系统中实际存在的真实大小写路径。
    /// 用于兼容 Linux 大小写敏感文件系统：游戏目录/文件大小写可能与代码硬编码的不一致
    /// （如 musicVersion / musicversion、MusicVersion000001 / musicversion000001）。
    /// 某一段在文件系统中不存在时（例如写入尚不存在的新文件/目录），该段及之后按给定大小写直接拼接。
    /// </summary>
    public static string ResolveIgnoreCase(string basePath, params string[] segments)
    {
        var current = basePath;
        foreach (var seg in segments)
        {
            var exact = Path.Combine(current, seg);
            if (File.Exists(exact) || Directory.Exists(exact))
            {
                current = exact;
                continue;
            }

            string? match = null;
            if (Directory.Exists(current))
            {
                match = Directory.EnumerateFileSystemEntries(current)
                    .FirstOrDefault(e => string.Equals(Path.GetFileName(e), seg, System.StringComparison.OrdinalIgnoreCase));
            }

            current = match ?? exact;
        }

        return current;
    }
}
