namespace MaiChartManager.Platform;

public interface IShellService
{
    void RevealInFileManager(string path);
    void OpenUrl(string url);
    void OpenPath(string path);
}
