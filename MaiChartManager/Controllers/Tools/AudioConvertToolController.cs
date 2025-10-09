using Microsoft.AspNetCore.Mvc;
using Sitreamai;

namespace MaiChartManager.Controllers.Tools;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api")]
public class AudioConvertToolController : ControllerBase
{
    [HttpPost]
    public IActionResult AudioConvertTool()
    {
        if (AppMain.BrowserWin is null) return BadRequest("浏览器窗口未初始化");

        var dialog = new OpenFileDialog()
        {
            Title = "请选择要转换的音频文件",
            Filter = "音频文件|*.wav;*.mp3;*.aac;*.ogg;*.flac;*.m4a;*.wma;*.ape;*.acb;*.awb;*.mp4",
        };

        if (AppMain.BrowserWin.Invoke(() => dialog.ShowDialog(AppMain.BrowserWin)) != DialogResult.OK)
            return BadRequest("未选择文件");

        var inputFile = dialog.FileName;
        var extension = Path.GetExtension(inputFile).ToLowerInvariant();
        var directory = Path.GetDirectoryName(inputFile);
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(inputFile);

        try
        {
            // 检查是否是 ACB 或 AWB 文件 - 转换为 MP3
            if (extension == ".acb" || extension == ".awb")
            {
                return ConvertAcbAwbToMp3(inputFile, directory, fileNameWithoutExt, extension);
            }

            // 其他格式转换为 ACB/AWB
            return ConvertToAcbAwb(inputFile, directory, fileNameWithoutExt, extension);
        }
        catch (Exception ex)
        {
            return BadRequest($"转换失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 将 ACB/AWB 转换为 MP3
    /// </summary>
    private IActionResult ConvertAcbAwbToMp3(string inputFile, string directory, string fileNameWithoutExt, string extension)
    {
        string acbPath;
        string awbPath;

        // 根据输入文件类型确定 ACB 和 AWB 路径
        if (extension == ".acb")
        {
            acbPath = inputFile;
            awbPath = Path.Combine(directory, fileNameWithoutExt + ".awb");
        }
        else // .awb
        {
            awbPath = inputFile;
            acbPath = Path.Combine(directory, fileNameWithoutExt + ".acb");
        }

        // 检查配对文件是否存在
        if (!System.IO.File.Exists(acbPath))
        {
            return BadRequest($"找不到配对的 ACB 文件: {acbPath}");
        }

        if (!System.IO.File.Exists(awbPath))
        {
            return BadRequest($"找不到配对的 AWB 文件: {awbPath}");
        }

        // 转换 ACB 到 WAV
        byte[] wavData = Audio.AcbToWav(acbPath);

        // 生成 MP3 输出路径
        string mp3Path = Path.Combine(directory, fileNameWithoutExt + ".mp3");

        // 将 WAV 数据转换为 MP3
        Audio.ConvertWavBytesToMp3(wavData, mp3Path);

        return Ok(new { message = "转换完成！", outputPath = mp3Path });
    }

    /// <summary>
    /// 将音频文件转换为 ACB/AWB
    /// </summary>
    private IActionResult ConvertToAcbAwb(string inputFile, string directory, string fileNameWithoutExt, string extension)
    {
        string tempAudioFile = null;

        try
        {
            string actualInputFile = inputFile;

            // 如果是 MP4 文件，先提取音轨
            if (extension == ".mp4")
            {
                tempAudioFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".wav");
                Audio.ExtractAudioFromMp4(inputFile, tempAudioFile);
                actualInputFile = tempAudioFile;
            }

            // 生成输出路径
            string acbPath = Path.Combine(directory, fileNameWithoutExt + ".acb");
            string awbPath = Path.Combine(directory, fileNameWithoutExt + ".awb");

            // 执行转换
            Audio.ConvertToMai(actualInputFile, acbPath);

            return Ok(new { message = "转换完成！", acbPath = acbPath, awbPath = awbPath });
        }
        finally
        {
            // 删除临时音频文件
            if (tempAudioFile != null && System.IO.File.Exists(tempAudioFile))
            {
                try
                {
                    System.IO.File.Delete(tempAudioFile);
                }
                catch
                {
                    // 忽略删除错误
                }
            }
        }
    }
}