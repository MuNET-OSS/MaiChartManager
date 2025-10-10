using MaiChartManager.Utils;
using Microsoft.AspNetCore.Mvc;

namespace MaiChartManager.Controllers.Music;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api/{assetDir}/{id:int}")]
public class MovieConvertController(ILogger<MovieConvertController> logger) : ControllerBase
{
    public enum SetMovieEventType
    {
        Progress,
        Success,
        Error
    }

    [HttpPut]
    [DisableRequestSizeLimit]
    public async Task SetMovie(int id, [FromForm] double padding, IFormFile file, [FromForm] bool noScale, [FromForm] bool h264, [FromForm] bool yuv420p, string assetDir)
    {
        id %= 10000;

        if (Path.GetExtension(file.FileName).Equals(".dat", StringComparison.InvariantCultureIgnoreCase))
        {
            var targetPath = Path.Combine(StaticSettings.StreamingAssets, assetDir, $@"MovieData\{id:000000}.dat");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await using var stream = System.IO.File.Open(targetPath, FileMode.Create);
            await file.CopyToAsync(stream);
            StaticSettings.MovieDataMap[id] = targetPath;
            return;
        }

        if (IapManager.License != IapManager.LicenseStatus.Active) return;
        Response.Headers.Append("Content-Type", "text/event-stream");
        
        var tmpDir = Directory.CreateTempSubdirectory();
        logger.LogInformation("Temp dir: {tmpDir}", tmpDir.FullName);

        try
        {
            // 保存上传的文件到临时目录
            var srcFilePath = Path.Combine(tmpDir.FullName, Path.GetFileName(file.FileName));
            await using (var srcFileStream = System.IO.File.OpenWrite(srcFilePath))
            {
                await file.CopyToAsync(srcFileStream);
            }

            // 目标路径
            var targetPath = Path.Combine(StaticSettings.StreamingAssets, assetDir, $@"MovieData\{id:000000}.{(h264 ? "mp4" : "dat")}");

            // 使用工具类转换视频
            await VideoConvert.ConvertVideo(new VideoConvert.VideoConvertOptions
            {
                InputPath = srcFilePath,
                OutputPath = targetPath,
                NoScale = noScale,
                UseH264 = h264,
                UseYuv420p = yuv420p,
                Padding = padding,
                ContentType = file.ContentType,
                OnProgress = async percent =>
                {
                    await Response.WriteAsync($"event: {SetMovieEventType.Progress}\ndata: {percent}\n\n");
                    await Response.Body.FlushAsync();
                }
            });

            StaticSettings.MovieDataMap[id] = targetPath;
            await Response.WriteAsync($"event: {SetMovieEventType.Success}\ndata: {SetMovieEventType.Success}\n\n");
            await Response.Body.FlushAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to convert video");
            SentrySdk.CaptureException(e);
            await Response.WriteAsync($"event: {SetMovieEventType.Error}\ndata: 转换失败：{e.Message}\n\n");
            await Response.Body.FlushAsync();
        }
        finally
        {
            // 清理临时目录
            try
            {
                tmpDir.Delete(true);
            }
            catch
            {
                // 忽略清理错误
            }
        }
    }
}