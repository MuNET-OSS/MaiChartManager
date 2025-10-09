using System.Diagnostics;
using AssetStudio;
using MaiChartManager.Models;
using MaiChartManager.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualBasic.FileIO;
using Sitreamai.Models;

namespace MaiChartManager.Controllers.Music;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api/{assetDir}/{id:int}")]
public class MusicController(StaticSettings settings, ILogger<MusicController> logger) : ControllerBase
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
            return "当前资源目录里已经存在这个 ID 了";
        }

        var music = MusicXmlWithABJacket.CreateNew(id, StaticSettings.GamePath, assetDir);
        settings.GetMusicList().Add(music);

        return "";
    }

    [HttpPut]
    public string SetMusicJacket(int id, IFormFile file, string assetDir)
    {
        var nonDxId = id % 10000;
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!MusicXml.jacketExtensions.Contains(ext[1..]))
        {
            return "不支持的图片格式";
        }

        var music = settings.GetMusic(id, assetDir);
        while (music?.JacketPath is not null && System.IO.File.Exists(music.JacketPath))
        {
            FileSystem.DeleteFile(music.JacketPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }

        var abiDir = Path.Combine(StaticSettings.StreamingAssets, assetDir, @"AssetBundleImages\jacket");
        Directory.CreateDirectory(abiDir);
        var path = Path.Combine(abiDir, $"ui_jacket_{nonDxId:000000}{ext}");
        using var write = System.IO.File.Open(path, FileMode.Create);
        file.CopyTo(write);
        write.Close();
        if (music is not null)
            music.JacketPath = path;
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
            Process.Start("explorer.exe", $"/select,\"{music.FilePath}\"");
        }
    }
}