using System.Text.Json;
using System.Threading.Channels;
using MaiChartManager.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualBasic.FileIO;

namespace MaiChartManager.Controllers.Tools;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api")]
public class VideoConvertToolController(ILogger<VideoConvertToolController> logger) : ControllerBase
{
    public enum VideoConvertEventType
    {
        Progress,
        Success,
        Error
    }

    [HttpPost]
    public async Task VideoConvertTool()
    {
        Response.Headers.Append("Content-Type", "text/event-stream");

        var dialog = new OpenFileDialog()
        {
            Title = Locale.SelectVideoToConvert,
            Filter = Locale.VideoFileFilter,
        };

        if (WinUtils.ShowDialog(dialog) != DialogResult.OK)
        {
            await Response.WriteAsync($"event: {VideoConvertEventType.Error}\ndata: {Locale.FileNotSelected}\n\n");
            await Response.Body.FlushAsync();
            return;
        }

        var inputFile = dialog.FileName;
        var directory = Path.GetDirectoryName(inputFile);
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(inputFile);
        var inputExt = Path.GetExtension(inputFile).ToLowerInvariant();

        try
        {
            // 检查是否是 USM/DAT 转 MP4
            if (inputExt is ".dat" or ".usm")
            {
                var outputMp4Path = Path.Combine(directory!, fileNameWithoutExt + ".mp4");

                await VideoConvert.ConvertUsmToMp4(
                    inputFile,
                    outputMp4Path,
                    async percent =>
                    {
                        await Response.WriteAsync($"event: {VideoConvertEventType.Progress}\ndata: {percent}\n\n");
                        await Response.Body.FlushAsync();
                    });

                await Response.WriteAsync($"event: {VideoConvertEventType.Success}\ndata: {outputMp4Path}\n\n");
                await Response.Body.FlushAsync();
            }
            else
            {
                // 普通视频转 USM/DAT
                var outputDatPath = Path.Combine(directory!, fileNameWithoutExt + ".dat");

                await VideoConvert.ConvertVideoToUsm(
                    inputFile,
                    outputDatPath,
                    noScale: StaticSettings.Config.NoScale,
                    yuv420p: StaticSettings.Config.Yuv420p,
                    async percent =>
                    {
                        await Response.WriteAsync($"event: {VideoConvertEventType.Progress}\ndata: {percent}\n\n");
                        await Response.Body.FlushAsync();
                    });

                await Response.WriteAsync($"event: {VideoConvertEventType.Success}\ndata: {outputDatPath}\n\n");
                await Response.Body.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to convert video");
            await Response.WriteAsync($"event: {VideoConvertEventType.Error}\ndata: {string.Format(Locale.ConvertFailed, ex.Message)}\n\n");
            await Response.Body.FlushAsync();
        }
    }

    public enum BatchConvertPvDirection
    {
        /// <summary>USM/DAT → MP4</summary>
        UsmToMp4,
        /// <summary>MP4 → USM/DAT</summary>
        Mp4ToUsm
    }

    public enum BatchConvertPvEventType
    {
        /// <summary>整体 + 当前文件进度，data 为 JSON</summary>
        Progress,
        /// <summary>单文件失败，仍然继续处理后续文件</summary>
        FileError,
        /// <summary>全部完成，data 为 "processed/total|failedCount"</summary>
        Success,
        /// <summary>致命错误，停止</summary>
        Error,
        /// <summary>被取消，data 为 "processed/total"</summary>
        Cancelled
    }

    private static readonly JsonSerializerOptions BatchJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private record BatchProgressPayload(int Processed, int Total, int FileProgress, string FileName, int Failed);
    private record BatchErrorPayload(string Code, string Message);

    private static class BatchConvertPvErrorCode
    {
        public const string NeedLicense = "NEED_LICENSE";
        public const string NoFiles = "NO_FILES";
        public const string FolderNotFound = "FOLDER_NOT_FOUND";
        public const string ConvertFailed = "CONVERT_FAILED";
    }

    /// <summary>
    /// 批量转换用户选择的文件夹内所有 PV：USM/DAT ↔ MP4。
    /// 使用 SSE 实时推送整体进度（已处理/总数）+ 当前文件进度。
    /// 客户端断开连接时通过 RequestAborted 触发取消，循环在下一个文件之间退出。
    /// 所有 SSE 写入通过单写者 Channel 串行化，避免 Xabe 同步进度事件触发的 async-void 写入交错。
    /// </summary>
    [HttpPost]
    public async Task BatchConvertPvTool([FromQuery] string folderPath, [FromQuery] BatchConvertPvDirection direction)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");

        // PV 转换属于赞助功能
        if (IapManager.License != IapManager.LicenseStatus.Active)
        {
            await WriteBatchError(BatchConvertPvErrorCode.NeedLicense, Locale.BatchConvertPvNeedLicense);
            return;
        }

        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            await WriteBatchError(BatchConvertPvErrorCode.FolderNotFound, Locale.BatchConvertPvFolderNotFound);
            return;
        }

        // 直接枚举用户选择的文件夹（不递归），按方向筛选源扩展名
        var sourceExtensions = direction == BatchConvertPvDirection.UsmToMp4
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".dat", ".usm" }
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp4" };

        var files = Directory.EnumerateFiles(folderPath)
            .Where(f => sourceExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            await WriteBatchError(BatchConvertPvErrorCode.NoFiles, Locale.BatchConvertPvNoFiles);
            return;
        }

        var total = files.Count;
        var processed = 0;
        var failedCount = 0;
        var cancellationToken = HttpContext.RequestAborted;

        // 单写者 Channel：所有 SSE 帧（不论来自循环还是 OnProgress）都进入这条队列
        var sseChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        var writer = WriteSseFrames(sseChannel.Reader, cancellationToken);

        try
        {
            foreach (var inputPath in files)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var fileName = Path.GetFileName(inputPath);
                await EnqueueProgress(sseChannel.Writer, processed, total, 0, fileName, failedCount);

                try
                {
                    var directory = Path.GetDirectoryName(inputPath)!;
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);

                    if (direction == BatchConvertPvDirection.UsmToMp4)
                    {
                        var outputPath = Path.Combine(directory, nameWithoutExt + ".mp4");
                        var snapshot = (Processed: processed, Failed: failedCount);
                        await VideoConvert.ConvertUsmToMp4(
                            inputPath,
                            outputPath,
                            percent => EnqueueProgressFireAndForget(sseChannel.Writer, snapshot.Processed, total, percent, fileName, snapshot.Failed));
                    }
                    else
                    {
                        // MP4 → USM（VP9）：先输出到临时文件，验证后再覆盖目标
                        var finalPath = Path.Combine(directory, nameWithoutExt + ".dat");
                        var tempPath = finalPath + ".tmp";
                        var snapshot = (Processed: processed, Failed: failedCount);
                        try
                        {
                            await VideoConvert.ConvertVideo(new VideoConvert.VideoConvertOptions
                            {
                                InputPath = inputPath,
                                OutputPath = tempPath,
                                NoScale = StaticSettings.Config.NoScale,
                                UseH264 = false,
                                UseYuv420p = StaticSettings.Config.Yuv420p,
                                Padding = 0,
                                TaskbarProgress = false,
                                OnProgress = percent => EnqueueProgressFireAndForget(sseChannel.Writer, snapshot.Processed, total, percent, fileName, snapshot.Failed)
                            });

                            if (!System.IO.File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
                            {
                                throw new Exception("Converted DAT is missing or empty");
                            }

                            // 取消检查必须在覆盖目标前，避免取消时仍然损毁已有 DAT
                            cancellationToken.ThrowIfCancellationRequested();

                            if (System.IO.File.Exists(finalPath))
                            {
                                System.IO.File.Delete(finalPath);
                            }
                            System.IO.File.Move(tempPath, finalPath);
                        }
                        catch
                        {
                            try { if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath); }
                            catch { /* ignored */ }
                            throw;
                        }
                    }

                    processed++;
                    await EnqueueProgress(sseChannel.Writer, processed, total, 100, fileName, failedCount);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception fileEx)
                {
                    logger.LogError(fileEx, "Failed to convert PV file {File}", inputPath);
                    failedCount++;
                    processed++;
                    await EnqueueEvent(sseChannel.Writer, BatchConvertPvEventType.FileError, $"{fileName}: {fileEx.Message}");
                    await EnqueueProgress(sseChannel.Writer, processed, total, 100, fileName, failedCount);
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                await EnqueueEvent(sseChannel.Writer, BatchConvertPvEventType.Cancelled, $"{processed}/{total}");
            }
            else
            {
                await EnqueueEvent(sseChannel.Writer, BatchConvertPvEventType.Success, $"{processed}/{total}|{failedCount}");
            }
        }
        catch (OperationCanceledException)
        {
            await EnqueueEvent(sseChannel.Writer, BatchConvertPvEventType.Cancelled, $"{processed}/{total}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Batch PV conversion failed");
            SentrySdk.CaptureException(ex);
            try
            {
                await EnqueueError(sseChannel.Writer, BatchConvertPvErrorCode.ConvertFailed, string.Format(Locale.ConvertFailed, ex.Message));
            }
            catch
            {
                // 客户端可能已断开
            }
        }
        finally
        {
            sseChannel.Writer.TryComplete();
            try
            {
                await writer;
            }
            catch
            {
                // writer 自己负责吞掉客户端断开异常
            }
        }
    }

    private static string SanitizeSseLine(string data) =>
        data.Replace("\r", " ").Replace("\n", " ");

    private static string CreateBatchErrorData(string code, string message) =>
        JsonSerializer.Serialize(new BatchErrorPayload(code, message), BatchJsonOptions);

    private async Task WriteBatchError(string code, string message)
    {
        await Response.WriteAsync($"event: {BatchConvertPvEventType.Error}\ndata: {SanitizeSseLine(CreateBatchErrorData(code, message))}\n\n");
        await Response.Body.FlushAsync();
    }

    private static ValueTask EnqueueEvent(ChannelWriter<string> writer, BatchConvertPvEventType eventType, string data) =>
        writer.WriteAsync($"event: {eventType}\ndata: {SanitizeSseLine(data)}\n\n");

    private static ValueTask EnqueueError(ChannelWriter<string> writer, string code, string message) =>
        EnqueueEvent(writer, BatchConvertPvEventType.Error, CreateBatchErrorData(code, message));

    private static ValueTask EnqueueProgress(ChannelWriter<string> writer, int processed, int total, int fileProgress, string fileName, int failed)
    {
        var payload = JsonSerializer.Serialize(new BatchProgressPayload(processed, total, fileProgress, fileName, failed), BatchJsonOptions);
        return writer.WriteAsync($"event: {BatchConvertPvEventType.Progress}\ndata: {payload}\n\n");
    }

    private static void EnqueueProgressFireAndForget(ChannelWriter<string> writer, int processed, int total, int fileProgress, string fileName, int failed)
    {
        var payload = JsonSerializer.Serialize(new BatchProgressPayload(processed, total, fileProgress, fileName, failed), BatchJsonOptions);
        // Channel 是无界的，TryWrite 同步入队，避免在 Xabe 的同步进度事件里 await
        writer.TryWrite($"event: {BatchConvertPvEventType.Progress}\ndata: {payload}\n\n");
    }

    private async Task WriteSseFrames(ChannelReader<string> reader, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var frame in reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await Response.WriteAsync(frame, cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "SSE frame write failed (client disconnected?)");
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
    }
}
