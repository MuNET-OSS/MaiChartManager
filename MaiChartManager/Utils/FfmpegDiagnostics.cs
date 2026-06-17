using System.Collections.Concurrent;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Exceptions;

namespace MaiChartManager.Utils;

public sealed class FfmpegLogCollector
{
    private const int MaxLines = 400;
    private readonly ConcurrentQueue<string> lines = new();

    public void Attach(IConversion conversion)
    {
        conversion.OnDataReceived += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.Data)) return;
            lines.Enqueue(args.Data);
            while (lines.Count > MaxLines && lines.TryDequeue(out var _))
            {
            }
        };
    }

    public string GetLog() => string.Join(Environment.NewLine, lines);
}

public static class FfmpegDiagnostics
{
    public static string CreateDetail(Exception exception, string? ffmpegLog = null)
    {
        var parts = new List<string>();

        if (exception is ConversionException conversionException &&
            !string.IsNullOrWhiteSpace(conversionException.InputParameters))
        {
            parts.Add("FFmpeg parameters:");
            parts.Add(conversionException.InputParameters);
        }

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
