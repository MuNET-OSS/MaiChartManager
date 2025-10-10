using MaiChartManager.Utils;
using Microsoft.AspNetCore.Mvc;

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
    public async Task VideoConvertTool([FromQuery] bool noScale, [FromQuery] bool yuv420p)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");

        if (AppMain.BrowserWin is null)
        {
            await Response.WriteAsync($"event: {VideoConvertEventType.Error}\ndata: 浏览器窗口未初始化\n\n");
            await Response.Body.FlushAsync();
            return;
        }

        var dialog = new OpenFileDialog()
        {
            Title = "请选择要转换的视频文件",
            Filter = "视频或者图片|*.mp4;*.mov;*.avi;*.mkv;*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.webp;*.svg;*.dat;*.usm",
        };

        if (AppMain.BrowserWin.Invoke(() => dialog.ShowDialog(AppMain.BrowserWin)) != DialogResult.OK)
        {
            await Response.WriteAsync($"event: {VideoConvertEventType.Error}\ndata: 未选择文件\n\n");
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
                    noScale,
                    yuv420p,
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
            await Response.WriteAsync($"event: {VideoConvertEventType.Error}\ndata: 转换失败：{ex.Message}\n\n");
            await Response.Body.FlushAsync();
        }
    }
}