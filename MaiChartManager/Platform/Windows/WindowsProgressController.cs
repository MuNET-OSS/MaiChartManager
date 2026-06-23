#if WINDOWS
using Vanara.Windows.Forms;

namespace MaiChartManager.Platform.Windows;

public class WindowsProgressController : IProgressController
{
    public IProgressSession Begin(string title, string description, string cancelMessage)
        => new WindowsProgressSession(title, description, cancelMessage);
}

public sealed class WindowsProgressSession : IProgressSession
{
    private readonly ShellProgressDialog _dialog;

    public WindowsProgressSession(string title, string description, string cancelMessage)
    {
        _dialog = new ShellProgressDialog
        {
            AutoTimeEstimation = false,
            Title = title,
            Description = description,
            CancelMessage = cancelMessage,
            HideTimeRemaining = true,
        };
        _dialog.Start(AppMain.BrowserWin!);
    }

    public void Report(ulong value, ulong total, string? detail = null)
    {
        if (detail is not null) _dialog.Detail = detail;
        _dialog.UpdateProgress(value, total);
    }

    public bool IsCancelled => _dialog.IsCancelled;

    public void Dispose() => _dialog.Stop();
}
#endif
