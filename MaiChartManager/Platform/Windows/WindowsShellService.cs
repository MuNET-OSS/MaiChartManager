#if WINDOWS
using System.Diagnostics;

namespace MaiChartManager.Platform.Windows;

/// <summary>Windows shell integration via explorer.exe / ShellExecute.</summary>
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
