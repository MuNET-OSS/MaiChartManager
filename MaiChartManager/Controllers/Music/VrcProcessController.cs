using MaiChartManager.Utils;
using Microsoft.AspNetCore.Mvc;
using FFMpegCore;

namespace MaiChartManager.Controllers.Music;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api")]
public class VrcProcessController(StaticSettings settings, ILogger<VrcProcessController> logger) : ControllerBase
{
    [HttpPost]
    public void GenAllMusicPreviewMp3ForVrc([FromForm] string targetDir, [FromForm] int maxConcurrency)
    {
        Task.Run(async () =>
        {
            // using var semaphore = new SemaphoreSlim(maxConcurrency);
            // var tasks = new List<Task>();
            var allAcb = StaticSettings.AcbAwb.Where(x => x.Key.StartsWith("music") && x.Key.EndsWith(".acb")).ToDictionary();
            foreach (var key in allAcb.Keys)
            {
                // await semaphore.WaitAsync();
                // tasks.Add(Task.Run(async () =>
                // {
                try
                {
                    var musicId = int.Parse(key[5..^4]);
                    var previewTime = CriUtils.GetAudioPreviewTime(allAcb[key]);
                    var wav = await AudioConvert.GetCachedWavPath(musicId);

                    if (wav is null)
                    {
                        logger.LogWarning("音频文件不存在 {musicId}", musicId);
                        continue;
                    }
                    if (previewTime.EndTime < previewTime.StartTime)
                    {
                        logger.LogWarning("previewTime.EndTime < previewTime.StartTime {musicId} {endTime} {startTime}", musicId, previewTime.EndTime, previewTime.StartTime);
                    }
                    var mp3Path = Path.Combine(targetDir, $"{musicId}.mp3");
                    // logger.LogInformation("转换中 {musicId}", musicId);

                    // 原本用 Xabe 的 FFmpeg.Conversions.FromSnippet.Split(...).SetOutputFormat(Format.mp3)，
                    // 它对每个音视频流加 PostInput 的 -ss/-t（从源截取 [start, start+duration)），
                    // 加 -map，再设输出格式 mp3。这里输入是单音轨 wav（无 SetCodec → 不显式指定 -c:a，
                    // 由 ffmpeg 按 mp3 容器选默认编码器）。
                    // 等价命令行：ffmpeg -i <wav> -ss <start> -t <duration> -map 0:0 -f mp3 <mp3Path>
                    // 时间格式沿用 Xabe 的 H:MM:SS.mmm，保证参数一致。
                    var start = TimeSpan.FromSeconds(previewTime.StartTime);
                    var duration = TimeSpan.FromSeconds(previewTime.EndTime - previewTime.StartTime);

                    await FFMpegArguments
                        .FromFileInput(wav, verifyExists: false)
                        .OutputToFile(mp3Path, overwrite: true, o => o.WithCustomArgument(
                            $"-ss {FormatFFmpegTime(start)} -t {FormatFFmpegTime(duration)} -map 0:0 -f mp3"))
                        .ProcessAsynchronously();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "处理音频文件出错 {key}", key);
                }
                //     finally
                //     {
                //         semaphore.Release(); // 释放信号量
                //     }
                // }));
            }

            // await Task.WhenAll(tasks);
        });
    }

    /// <summary>
    /// 把 TimeSpan 格式化为 ffmpeg 时间字符串 H:MM:SS.mmm，
    /// 与原 Xabe TimeExtensions.ToFFmpeg 行为一致（"{0:D}:{1:D2}:{2:D2}.{3:D3}"，
    /// 参数为 (int)TotalHours, Minutes, Seconds, Milliseconds）。
    /// </summary>
    private static string FormatFFmpegTime(TimeSpan time) =>
        $"{(int)time.TotalHours:D}:{time.Minutes:D2}:{time.Seconds:D2}.{time.Milliseconds:D3}";
}