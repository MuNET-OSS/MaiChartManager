using MaiChartManager.Controllers.Music;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualBasic.FileIO;
using Xabe.FFmpeg;

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

        var tmpDir = Directory.CreateTempSubdirectory();
        logger.LogInformation("Temp dir: {tmpDir}", tmpDir.FullName);

        try
        {
            // 输出路径
            var outVideoPath = Path.Combine(tmpDir.FullName, "out.ivf");
            var outputDatPath = Path.Combine(directory!, fileNameWithoutExt + ".dat");

            try
            {
                // 获取源视频信息
                var srcMedia = await FFmpeg.GetMediaInfo(inputFile);
                var vp9Encoding = MovieConvertController.HardwareAcceleration ==
                    MovieConvertController.HardwareAccelerationStatus.Enabled
                        ? "vp9_qsv"
                        : "vp9";

                var firstStream = srcMedia.VideoStreams.First().SetCodec(vp9Encoding);
                var conversion = FFmpeg.Conversions.New()
                    .AddStream(firstStream);

                // 处理缩放
                var vf = "";
                var scale = 1080;
                if (!noScale)
                {
                    vf = $"scale={scale}:-1,pad={scale}:{scale}:({scale}-iw)/2:({scale}-ih)/2:black";
                    conversion.AddParameter($"-vf {vf}");
                }

                conversion
                    .SetOutput(outVideoPath)
                    .AddParameter("-hwaccel dxva2", ParameterPosition.PreInput)
                    .UseMultiThread(true)
                    .AddParameter("-cpu-used 5");

                // 处理颜色空间
                if (yuv420p)
                    conversion.AddParameter("-pix_fmt yuv420p");

                logger.LogInformation("About to run FFMpeg with params: {params}", conversion.Build());

                // 添加进度回调
                conversion.OnProgress += async (sender, args) =>
                {
                    await Response.WriteAsync($"event: {VideoConvertEventType.Progress}\ndata: {args.Percent}\n\n");
                    await Response.Body.FlushAsync();
                };

                await conversion.Start();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to convert video");
                await Response.WriteAsync($"event: {VideoConvertEventType.Error}\ndata: 视频转换为 VP9 失败：{e.Message}\n\n");
                await Response.Body.FlushAsync();
                return;
            }

            // 检查输出文件
            if (!System.IO.File.Exists(outVideoPath) || new FileInfo(outVideoPath).Length == 0)
            {
                await Response.WriteAsync($"event: {VideoConvertEventType.Error}\ndata: 视频转换为 VP9 失败：输出文件不存在\n\n");
                await Response.Body.FlushAsync();
                return;
            }

            // 转换 IVF 到 USM (DAT)
            var outputFile = Path.Combine(tmpDir.FullName, "out.usm");
            try
            {
                WannaCRI.WannaCRI.CreateUsm(outVideoPath);
                if (!System.IO.File.Exists(outputFile) || new FileInfo(outputFile).Length == 0)
                {
                    throw new Exception("Output file not found or empty");
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to convert ivf to usm");
                await Response.WriteAsync($"event: {VideoConvertEventType.Error}\ndata: 视频转换为 USM 失败：{e.Message}\n\n");
                await Response.Body.FlushAsync();
                return;
            }

            // 复制到目标位置
            try
            {
                FileSystem.CopyFile(outputFile, outputDatPath, true);
                await Response.WriteAsync($"event: {VideoConvertEventType.Success}\ndata: {outputDatPath}\n\n");
                await Response.Body.FlushAsync();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to copy file");
                await Response.WriteAsync($"event: {VideoConvertEventType.Error}\ndata: 复制文件失败：{e.Message}\n\n");
                await Response.Body.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            await Response.WriteAsync($"event: {VideoConvertEventType.Error}\ndata: 转换失败：{ex.Message}\n\n");
            await Response.Body.FlushAsync();
        }
    }
}