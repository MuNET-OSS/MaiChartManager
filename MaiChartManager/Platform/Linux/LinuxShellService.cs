using System.Diagnostics;
using MaiChartManager.Platform;
using Microsoft.Extensions.Logging;

namespace MaiChartManager.Platform.Linux;

/// <summary>通过 xdg-open / xdg-utils 实现 Linux 系统集成。</summary>
public class LinuxShellService(ILogger<LinuxShellService> logger) : IShellService
{
    public void RevealInFileManager(string path)
    {
        // Linux 文件管理器没有通用的"选中文件"功能；改为打开所在目录。
        var target = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path;
        XdgOpen(target);
    }

    public void OpenUrl(string url) => XdgOpen(url);

    public void OpenPath(string path) => XdgOpen(path);

    private void XdgOpen(string arg)
    {
        try
        {
            Process.Start(new ProcessStartInfo("xdg-open", $"\"{arg}\"") { UseShellExecute = false });
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to xdg-open {Arg}", arg);
        }
    }
}
