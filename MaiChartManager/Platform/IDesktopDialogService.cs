namespace MaiChartManager.Platform;

public interface IDesktopDialogService
{
    string? PickFolder(string? title = null);
    string? PickFile(string? title = null, string? filter = null);
    bool Confirm(string message, string title, bool defaultResult = false);
    void ShowError(string message, string title);
}
