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
            Title = "请选择要转换的 MP4 视频文件",
            Filter = "MP4 视频文件|*.mp4",
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
        var outputDatPath = Path.Combine(directory!, fileNameWithoutExt + ".dat");

        try
        {
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
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to convert video");
            await Response.WriteAsync($"event: {VideoConvertEventType.Error}\ndata: 转换失败：{ex.Message}\n\n");
            await Response.Body.FlushAsync();
        }
    }
}