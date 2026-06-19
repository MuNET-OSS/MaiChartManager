using System.Collections.Concurrent;

namespace MaiChartManager.Utils;

/// <summary>
/// 收集 ffmpeg 运行期间输出到 stderr 的日志行（ffmpeg 几乎所有诊断信息都走 stderr）。
/// 迁移到 FFMpegCore 后，不再有 Xabe 的 IConversion / OnDataReceived 事件，
/// 改为把 <see cref="AddLine"/> 直接挂到 FFMpegArgumentProcessor 的 NotifyOnError 上。
/// </summary>
public sealed class FfmpegLogCollector
{
    private const int MaxLines = 400;
    private readonly ConcurrentQueue<string> lines = new();

    /// <summary>
    /// 接收一行 ffmpeg 输出。用法：<c>processor.NotifyOnError(collector.AddLine)</c>。
    /// </summary>
    public void AddLine(string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return;
        lines.Enqueue(data);
        while (lines.Count > MaxLines && lines.TryDequeue(out _))
        {
        }
    }

    public string GetLog() => string.Join(Environment.NewLine, lines);
}

public static class FfmpegDiagnostics
{
    public static string CreateDetail(Exception exception, string? ffmpegLog = null)
    {
        var parts = new List<string>();

        // FFMpegCore 的异常不携带 Xabe 的 InputParameters，关键信息全在收集到的 ffmpeg 日志里。
        if (!string.IsNullOrWhiteSpace(ffmpegLog))
        {
            parts.Add("FFmpeg output:");
            parts.Add(ffmpegLog);
        }
        else if (!string.IsNullOrWhiteSpace(exception.Message))
        {
            parts.Add(exception.Message);
        }

        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    public static string CreateSummary(Exception exception, string? ffmpegLog = null)
    {
        var source = string.IsNullOrWhiteSpace(ffmpegLog) ? exception.Message : ffmpegLog;
        var lines = source
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsUsefulLine)
            .ToList();

        if (lines.Count == 0)
        {
            return exception.Message;
        }

        return lines[^1];
    }

    private static bool IsUsefulLine(string line)
    {
        if (line.StartsWith("ffmpeg version ", StringComparison.OrdinalIgnoreCase)) return false;
        if (line.StartsWith("configuration:", StringComparison.OrdinalIgnoreCase)) return false;
        if (line.StartsWith("libav", StringComparison.OrdinalIgnoreCase)) return false;
        if (line.StartsWith("built with ", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }
}
