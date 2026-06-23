using MaiChartManager.Platform;
using MaiChartManager.Utils;
using Microsoft.AspNetCore.Mvc;

namespace MaiChartManager.Controllers.Tools;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api")]
public class AudioConvertToolController(IDesktopDialogService dialogService) : ControllerBase
{
    [HttpPost]
    public IActionResult AudioConvertTool()
    {
        // 用平台原生文件对话框选音频文件（Windows=WinForms，Linux=Photino），转换逻辑跨平台。
        var inputFile = dialogService.PickFile(Locale.SelectAudioToConvert, Locale.AudioFileFilter);
        if (inputFile is null)
            return BadRequest(Locale.FileNotSelected);

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
            return BadRequest(string.Format(Locale.ConvertFailed, ex.Message));
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
            return BadRequest(string.Format(Locale.AcbNotFound, acbPath));
        }

        if (!System.IO.File.Exists(awbPath))
        {
            return BadRequest(string.Format(Locale.AwbNotFound, awbPath));
        }

        // 转换 ACB 到 WAV
        byte[] wavData = Audio.AcbToWav(acbPath);

        // 生成 MP3 输出路径
        string mp3Path = Path.Combine(directory, fileNameWithoutExt + ".mp3");

        // 将 WAV 数据转换为 MP3
        Audio.ConvertWavBytesToMp3(wavData, mp3Path);

        return Ok(new { message = Locale.ConvertSuccess, outputPath = mp3Path });
    }

    /// <summary>
    /// 将音频文件转换为 ACB/AWB
    /// </summary>
    private IActionResult ConvertToAcbAwb(string inputFile, string directory, string fileNameWithoutExt, string extension, bool ignoreGapless = false)
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
            Audio.ConvertToMai(actualInputFile, acbPath, forceUseNAudio: ignoreGapless);

            return Ok(new { message = Locale.ConvertSuccess, acbPath = acbPath, awbPath = awbPath });
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