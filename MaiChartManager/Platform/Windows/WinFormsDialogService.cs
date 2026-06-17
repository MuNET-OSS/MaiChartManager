#if WINDOWS
using System.Windows.Forms;
using MaiChartManager.Utils;

namespace MaiChartManager.Platform.Windows;

/// <summary>
/// WinForms-backed dialog service. Mirrors the original WinUtils.ShowDialog +
/// per-controller FolderBrowserDialog/OpenFileDialog/MessageBox usage.
/// </summary>
public class WinFormsDialogService : IDesktopDialogService
{
    public string? PickFolder(string? title = null)
    {
        using var dialog = new FolderBrowserDialog
        {
            ShowNewFolderButton = false,
        };
        if (title is not null) dialog.Description = title;
        return WinUtils.ShowDialog(dialog) == DialogResult.OK ? dialog.SelectedPath : null;
    }

    public string? PickFile(string? title = null, string? filter = null)
    {
        using var dialog = new OpenFileDialog();
        if (title is not null) dialog.Title = title;
        if (filter is not null) dialog.Filter = filter;
        return WinUtils.ShowDialog(dialog) == DialogResult.OK ? dialog.FileName : null;
    }

    public bool Confirm(string message, string title, bool defaultResult = false)
    {
        var owner = AppMain.ActiveForm ?? AppMain.BrowserWin;
        if (owner == null)
            return MessageBox.Show(message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        return owner.Invoke(() =>
            MessageBox.Show(owner, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes);
    }

    public void ShowError(string message, string title)
    {
        var owner = AppMain.ActiveForm ?? AppMain.BrowserWin;
        if (owner == null)
        {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        owner.Invoke(() => MessageBox.Show(owner, message, title, MessageBoxButtons.OK, MessageBoxIcon.Error));
    }
}
#endif
