#if WINDOWS
using System.Diagnostics;

namespace MaiChartManager.Platform.Windows;

/// <summary>通过 explorer.exe / ShellExecute 实现 Windows 系统集成。</summary>
public class WindowsShellService : IShellService
{
    public void RevealInFileManager(string path)
    {
        Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    public void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public void OpenPath(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
#endif
