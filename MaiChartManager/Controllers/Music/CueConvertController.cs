using System.Security.Cryptography;
using MaiChartManager.Attributes;
using MaiChartManager.Utils;
using Microsoft.AspNetCore.Mvc;
using Sitreamai;

namespace MaiChartManager.Controllers.Music;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api/{assetDir}/{id:int}")]
public class CueConvertController(StaticSettings settings, ILogger<CueConvertController> logger) : ControllerBase
{
    [NoCache]
    [HttpGet]
    public async Task<ActionResult> GetMusicWav(int id, string assetDir)
    {
        var cachePath = await AudioConvert.GetCachedWavPath(id);
        if (cachePath is null)
        {
            return NotFound();
        }

        return PhysicalFile(cachePath, "audio/wav", enableRangeProcessing: true);
    }

    [HttpPut]
    [DisableRequestSizeLimit]
    public void SetAudio(int id, [FromForm] float padding, IFormFile file, IFormFile? awb, IFormFile? preview, string assetDir)
    {
        id %= 10000;
        var targetAcbPath = Path.Combine(StaticSettings.StreamingAssets, assetDir, $@"SoundData\music{id:000000}.acb");
        var targetAwbPath = Path.Combine(StaticSettings.StreamingAssets, assetDir, $@"SoundData\music{id:000000}.awb");
        Directory.CreateDirectory(Path.GetDirectoryName(targetAcbPath));

        if (Path.GetExtension(file.FileName).Equals(".acb", StringComparison.InvariantCultureIgnoreCase))
        {
            if (awb is null) throw new Exception("acb 文件必须搭配 awb 文件");
            using var write = System.IO.File.Open(targetAcbPath, FileMode.Create);
            file.CopyTo(write);
            using var writeAwb = System.IO.File.Open(targetAwbPath, FileMode.Create);
            awb.CopyTo(writeAwb);
        }
        else
        {
            Audio.ConvertToMai(file.FileName, targetAcbPath, padding, file.OpenReadStream(), preview?.FileName, preview?.OpenReadStream());
        }

        StaticSettings.AcbAwb[$"music{id:000000}.acb"] = targetAcbPath;
        StaticSettings.AcbAwb[$"music{id:000000}.awb"] = targetAwbPath;
    }

    public record SetAudioPreviewRequest(double StartTime, double EndTime);

    [HttpPost]
    public async Task SetAudioPreview(int id, [FromBody] SetAudioPreviewRequest request, string assetDir)
    {
        if (IapManager.License != IapManager.LicenseStatus.Active) return;
        id %= 10000;
        var cachePath = await AudioConvert.GetCachedWavPath(id);
        var targetAcbPath = StaticSettings.AcbAwb[$"music{id:000000}.acb"];
        if (cachePath is null) throw new Exception("音频文件不存在");

        var loopStart = TimeSpan.FromSeconds(request.StartTime);
        var loopEnd = TimeSpan.FromSeconds(request.EndTime);

        var acbBytes = await CriUtils.CreateAcbWithPreview(cachePath, await System.IO.File.ReadAllBytesAsync(StaticSettings.AcbAwb[$"music{id:000000}.awb"]), loopStart, loopEnd);
        await System.IO.File.WriteAllBytesAsync(targetAcbPath, acbBytes);
    }

    [HttpGet]
    public CriUtils.AudioPreviewTime GetAudioPreviewTime(int id, string assetDir)
    {
        id %= 10000;
        try
        {
            return CriUtils.GetAudioPreviewTime(StaticSettings.AcbAwb[$"music{id:000000}.acb"]);
        }
        catch
        {
            return new CriUtils.AudioPreviewTime(-1, -1);
        }
    }
}