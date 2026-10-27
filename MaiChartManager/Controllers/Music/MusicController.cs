using System.Diagnostics;
using MaiChartManager.Models;
using MaiChartManager.Platform;
using MaiChartManager.Utils;
using Microsoft.AspNetCore.Mvc;
using MusicXml = MaiChartManager.Models.MusicXml;

namespace MaiChartManager.Controllers.Music;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api/{assetDir}/{id:int}")]
public class MusicController(StaticSettings settings, ILogger<MusicController> logger, IShellService shellService) : ControllerBase
{
    [HttpGet]
    public MusicXmlWithABJacket? GetMusicDetail(int id, string assetDir)
    {
        return settings.GetMusic(id, assetDir);
    }

    [HttpPost]
    public void EditMusicName(int id, [FromBody] string value, string assetDir)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music != null)
        {
            music.Name = value;
        }
    }
    
    [HttpPost]
    public string EditMusicSortName(int id, [FromBody] string value, string assetDir)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music != null)
        {
            music.SortName = value;
            return music.SortName; // 因为music.SortName的setter中会对内容做格式化、确保符合要求。所以把转化的结果返回前端供前端更新。
        }

        return "";
    }

    [HttpPost]
    public void EditMusicArtist(int id, [FromBody] string value, string assetDir)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music != null)
        {
            music.Artist = value;
        }
    }

    [HttpPost]
    public void EditMusicUtageKanji(int id, [FromBody] string value, string assetDir)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music != null)
        {
            music.UtageKanji = value;
        }
    }

    [HttpPost]
    // Utage 备注
    public void EditMusicComment(int id, [FromBody] string value, string assetDir)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music != null)
        {
            music.Comment = value;
        }
    }

    [HttpPost]
    public void EditMusicBpm(int id, [FromBody] float value, string assetDir)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music != null)
        {
            music.Bpm = value;
        }
    }

    [HttpPost]
    public void EditMusicVersion(int id, [FromBody] int value, string assetDir)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music != null)
        {
            music.Version = value;
        }
    }

    [HttpPost]
    public void EditMusicGenre(int id, [FromBody] int value, string assetDir)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music != null)
        {
            music.GenreId = value;
        }
    }

    [HttpPost]
    public void EditMusicAddVersion(int id, [FromBody] int value, string assetDir)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music != null)
        {
            music.AddVersionId = value;
        }
    }

    [HttpPost]
    public void EditMusicLong(int id, [FromBody] bool value, string assetDir)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music != null)
        {
            music.LongMusic = value;
        }
    }

    /// <summary>
    /// 把唯一启用的难度谱面转换成 Basic（绿谱）谱面。
    /// 宴会场只识别 Basic 槽位的谱面，所以把普通乐曲改成宴会场之前需要先把选中的难度整体挪到 Basic 槽位。
    /// 返回空字符串表示成功，否则是错误信息。
    /// </summary>
    [HttpPost]
    public string ConvertSingleChartToBasic(int id, string assetDir)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music is null) return "Music not found";

        var enabledIndexes = Enumerable.Range(0, 5).Where(index => music.Charts[index].Enable).ToList();
        if (enabledIndexes.Count == 0) return "No enabled chart";
        if (enabledIndexes.Count > 1) return "Multiple charts enabled";

        var sourceIndex = enabledIndexes[0];
        if (sourceIndex == 0) return ""; // 已经是 Basic 谱面，无需转换

        var musicDir = Path.GetDirectoryName(music.FilePath);
        if (string.IsNullOrWhiteSpace(musicDir) || !Directory.Exists(musicDir)) return "Invalid music directory";

        var sourceChart = music.Charts[sourceIndex];
        var targetChart = music.Charts[0];

        // 交换两个槽位的谱面文件：源难度的谱面挪到 Basic 的文件名（_00），
        // 旧的 Basic 谱面挪到源难度的文件名，这样两个槽位的 path 都仍然指向真实存在的文件。
        // 注意：path 本身不动，槽位还是指向自己原来的文件名。
        foreach (var suffix in new[] { "", "_L", "_R" })
        {
            var sourcePath = Path.Combine(musicDir, sourceChart.Path.Replace(".ma2", suffix + ".ma2"));
            var targetPath = Path.Combine(musicDir, targetChart.Path.Replace(".ma2", suffix + ".ma2"));
            if (!System.IO.File.Exists(sourcePath)) continue;
            var tempPath = sourcePath + ".mcmswap";
            PlatformFile.MoveFile(sourcePath, tempPath);
            if (System.IO.File.Exists(targetPath)) PlatformFile.MoveFile(targetPath, sourcePath);
            PlatformFile.MoveFile(tempPath, targetPath);
        }

        // 把原难度的谱面信息搬到 Basic 槽位并启用 Basic，原难度禁用
        var level = sourceChart.Level;
        var levelDecimal = sourceChart.LevelDecimal;
        var levelId = sourceChart.LevelId;
        var maxNotes = sourceChart.MaxNotes;
        var designer = sourceChart.Designer;

        targetChart.Level = level;
        targetChart.LevelDecimal = levelDecimal;
        targetChart.LevelId = levelId;
        targetChart.MaxNotes = maxNotes;
        targetChart.Designer = designer ?? "";
        targetChart.Enable = true;

        sourceChart.Enable = false;

        music.Save();
        music.Refresh();
        return "";
    }

    [HttpPost]
    public void SaveMusic(int id, string assetDir)
    {
        var music = settings.GetMusic(id, assetDir);
        music?.Save();
    }

    [HttpDelete]
    public void DeleteMusic(int id, string assetDir)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music != null)
        {
            music.Delete();
            settings.GetMusicList().Remove(music);
        }
    }

    [HttpPost]
    public string AddMusic(int id, string assetDir)
    {
        if (settings.GetMusic(id, assetDir) is not null)
        {
            return Locale.MusicIdExists;
        }

        var music = MusicXmlWithABJacket.CreateNew(id, StaticSettings.GamePath, assetDir);
        settings.GetMusicList().Add(music);

        return "";
    }

    [HttpPut]
    public string SetMusicJacket(int id, IFormFile file, string assetDir)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!MusicXml.jacketExtensions.Contains(ext[1..]))
        {
            return Locale.UnsupportedImageFormat;
        }

        var music = settings.GetMusic(id, assetDir);
        if (music is null) return "Music not found!";
        music.DeleteJacket(); // 删除老的jacket

        var abiDir = Path.Combine(StaticSettings.StreamingAssets, assetDir, "AssetBundleImages", "jacket");
        Directory.CreateDirectory(abiDir);
        
        if (StaticSettings.Config.ConvertJacketToAssetBundle)
        { // 将图片转为AssetBundle
            using var buffer = new MemoryStream();
            file.CopyTo(buffer);
            var imageBytes = buffer.ToArray();

            var assetBundleDir = Path.GetDirectoryName(abiDir)!;
            var resultAbPath = AssetBundleCreator.CreateMusicJacketAssetBundles(
                imageBytes, assetBundleDir, music.NonDxId);
            StaticSettings.AssetBundleJacketMap[music.NonDxId] = resultAbPath;
        }
        else
        {
            var path = Path.Combine(abiDir, $"ui_jacket_{music.NonDxId:000000}{ext}");
            using var write = System.IO.File.Open(path, FileMode.Create);
            file.CopyTo(write);
            write.Close();
            music.JacketPath = path;
        }

        return "";
    }


    [HttpGet]
    public ActionResult GetJacket(int id, string assetDir)
    {
        var music = settings.GetMusic(id, assetDir);
        var data = ImageConvert.GetMusicJacketPngData(music);
        if (data is null)
        {
            return NotFound();
        }

        return File(data, "image/png");
    }

    [HttpPost]
    public void RequestOpenExplorer(int id, string assetDir)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music != null)
        {
            shellService.RevealInFileManager(music.FilePath);
        }
    }

    [HttpPost]
    public void RequestOpenXml(int id, string assetDir)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music != null)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = music.FilePath,
                UseShellExecute = true
            });
        }
    }
}
