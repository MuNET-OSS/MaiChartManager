#if !WINDOWS
using MaiChartManager.Platform;
using Microsoft.Extensions.Logging;
using Photino.NET;

namespace MaiChartManager.Platform.Linux;

/// <summary>
/// Linux 平台基于 Photino 原生对话框的 <see cref="IDesktopDialogService"/> 实现。
/// 底层走 WebKitGTK，弹出 GTK 原生的文件/文件夹选择与消息框。
/// </summary>
/// <remarks>
/// Photino 的对话框是 <see cref="PhotinoWindow"/> 的实例方法，且必须在窗口的 UI 线程上执行。
/// Controller 在 Kestrel 的请求线程调用本服务，因此这里通过 <see cref="PhotinoWindow.Invoke"/>
/// 把调用 marshal 到 UI 线程，并用 <see cref="ManualResetEventSlim"/> 阻塞等待结果返回。
/// </remarks>
public class PhotinoDialogService(ILogger<PhotinoDialogService> logger) : IDesktopDialogService
{
    public string? PickFolder(string? title = null)
    {
        var window = PhotinoWindowHolder.Current;
        if (window is null)
        {
            // 理论上不会发生：窗口在 LinuxProgram.Main 创建后即赋值。
            logger.LogWarning("PickFolder：Photino 主窗口尚未就绪，返回 null。title={Title}", title);
            return null;
        }

        string[]? result = null;
        var done = new ManualResetEventSlim();
        // 必须在 UI 线程调用 ShowOpenFolder。
        window.Invoke(() =>
        {
            try
            {
                // ShowOpenFolder(string title, string defaultPath, bool multiSelect)
                result = window.ShowOpenFolder(title ?? "", null, false);
            }
            catch (Exception e)
            {
                logger.LogError(e, "PickFolder：弹出文件夹选择对话框失败。");
            }
            finally
            {
                done.Set();
            }
        });
        done.Wait();
        return result is { Length: > 0 } ? result[0] : null;
    }

    public string? PickFile(string? title = null, string? filter = null)
    {
        var window = PhotinoWindowHolder.Current;
        if (window is null)
        {
            logger.LogWarning("PickFile：Photino 主窗口尚未就绪，返回 null。title={Title}", title);
            return null;
        }

        string[]? result = null;
        var done = new ManualResetEventSlim();
        window.Invoke(() =>
        {
            try
            {
                // 忽略 WinForms 风格的 filter 字符串，传 null 表示允许所有文件，
                // 避免 WinForms→Photino 的 filter 格式转换出错。
                // ShowOpenFile(string title, string defaultPath, bool multiSelect, (string,string[])[] filters)
                result = window.ShowOpenFile(title ?? "", null, false, null);
            }
            catch (Exception e)
            {
                logger.LogError(e, "PickFile：弹出文件选择对话框失败。");
            }
            finally
            {
                done.Set();
            }
        });
        done.Wait();
        return result is { Length: > 0 } ? result[0] : null;
    }

    public bool Confirm(string message, string title, bool defaultResult = false)
    {
        var window = PhotinoWindowHolder.Current;
        if (window is null)
        {
            logger.LogWarning("Confirm：Photino 主窗口尚未就绪，返回默认值 {Default}。title={Title}", defaultResult, title);
            return defaultResult;
        }

        var confirmed = false;
        var done = new ManualResetEventSlim();
        window.Invoke(() =>
        {
            try
            {
                // ShowMessage(string title, string text, PhotinoDialogButtons buttons, PhotinoDialogIcon icon)
                var ret = window.ShowMessage(title, message, PhotinoDialogButtons.YesNo, PhotinoDialogIcon.Question);
                confirmed = ret == PhotinoDialogResult.Yes;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Confirm：弹出确认对话框失败，回退到默认值 {Default}。", defaultResult);
                confirmed = defaultResult;
            }
            finally
            {
                done.Set();
            }
        });
        done.Wait();
        return confirmed;
    }

    public void ShowError(string message, string title)
    {
        var window = PhotinoWindowHolder.Current;
        if (window is null)
        {
            logger.LogError("ShowError ({Title}): {Message}", title, message);
            return;
        }

        var done = new ManualResetEventSlim();
        window.Invoke(() =>
        {
            try
            {
                window.ShowMessage(title, message, PhotinoDialogButtons.Ok, PhotinoDialogIcon.Error);
            }
            catch (Exception e)
            {
                logger.LogError(e, "ShowError：弹出错误对话框失败。原始消息 ({Title}): {Message}", title, message);
            }
            finally
            {
                done.Set();
            }
        });
        done.Wait();
    }
}
#endif
