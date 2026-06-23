namespace MaiChartManager.Platform;

/// <summary>
/// 跨平台文件删除助手。
/// Windows 走 Microsoft.VisualBasic 的回收站删除（与原行为一致）；
/// Linux 直接永久删除（无回收站概念，且 VisualBasic 的回收站/对话框选项在 Linux 运行时会抛异常）。
/// </summary>
public static class PlatformFile
{
    /// 删除文件：Windows 送回收站，Linux 直接删除。文件不存在时静默忽略。
    public static void DeleteFile(string path)
    {
#if WINDOWS
        if (File.Exists(path))
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
#else
        if (File.Exists(path)) File.Delete(path);
#endif
    }

    /// 删除目录（递归）：Windows 送回收站，Linux 直接删除。目录不存在时静默忽略。
    /// showDialog=true 时 Windows 显示删除进度对话框（对应原 UIOption.AllDialogs）。
    public static void DeleteDirectory(string path, bool showDialog = false)
    {
#if WINDOWS
        if (Directory.Exists(path))
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path,
                showDialog ? Microsoft.VisualBasic.FileIO.UIOption.AllDialogs : Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
#else
        if (Directory.Exists(path)) Directory.Delete(path, true);
#endif
    }

    /// 永久删除目录（递归，不进回收站）。对应原 DeleteDirectoryOption.DeleteAllContents。
    public static void DeleteDirectoryPermanent(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }

    /// 复制文件（跨平台）。
    public static void CopyFile(string source, string dest, bool overwrite = true)
        => File.Copy(source, dest, overwrite);

    /// 移动文件（跨平台）。
    public static void MoveFile(string source, string dest, bool overwrite = true)
        => File.Move(source, dest, overwrite);

    /// 移动目录（跨平台）。
    public static void MoveDirectory(string source, string dest)
        => Directory.Move(source, dest);

    /// 递归复制目录（跨平台）。dest 已存在时合并，文件按 overwrite 覆盖。
    public static void CopyDirectory(string source, string dest, bool overwrite = true)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite);
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)), overwrite);
    }
}
