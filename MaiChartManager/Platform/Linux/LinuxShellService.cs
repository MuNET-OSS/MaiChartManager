using System.Diagnostics;
using MaiChartManager.Platform;
using Microsoft.Extensions.Logging;

namespace MaiChartManager.Platform.Linux;

/// <summary>Shell integration for Linux via xdg-open / xdg-utils.</summary>
public class LinuxShellService(ILogger<LinuxShellService> logger) : IShellService
{
    public void RevealInFileManager(string path)
    {
        // No portable "select file" on Linux file managers; open the containing directory.
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
