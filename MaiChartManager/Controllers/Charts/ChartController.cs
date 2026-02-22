using MaiChartManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace MaiChartManager.Controllers.Charts;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api/{assetDir}/{id:int}/{level:int}")]
public class ChartController(StaticSettings settings, ILogger<StaticSettings> logger, MaidataImportService importService) : ControllerBase
{
    [HttpPost]
    public void EditChartLevel(int id, int level, [FromBody] int value, string assetDir)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music != null)
        {
            var chart = music.Charts[level];
            if (chart != null)
            {
                chart.Level = value;
            }
        }
    }

    [HttpPost]
    public void EditChartLevelDisplay(int id, int level, [FromBody] int value, string assetDir)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music != null)
        {
            var chart = music.Charts[level];
            if (chart != null)
            {
                chart.LevelId = value;
            }
        }
    }

    [HttpPost]
    public void EditChartLevelDecimal(int id, int level, [FromBody] int value, string assetDir)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music != null)
        {
            var chart = music.Charts[level];
            if (chart != null)
            {
                chart.LevelDecimal = value;
            }
        }
    }

    [HttpPost]
    public void EditChartDesigner(int id, int level, [FromBody] string value, string assetDir)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music != null)
        {
            var chart = music.Charts[level];
            if (chart != null)
            {
                chart.Designer = value;
            }
        }
    }

    [HttpPost]
    public void EditChartNoteCount(int id, int level, [FromBody] int value, string assetDir)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music != null)
        {
            var chart = music.Charts[level];
            if (chart != null)
            {
                chart.MaxNotes = value;
            }
        }
    }

    [HttpPost]
    public void EditChartEnable(int id, int level, [FromBody] bool value, string assetDir)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music != null)
        {
            var chart = music.Charts[level];
            if (chart != null)
            {
                chart.Enable = value;
            }
            if (level == 4)
            {
                music.SubLockType = 0;
            }
        }
    }

    [HttpPost]
    public ImportChartResult ReplaceChart(
        int id,
        int level,
        IFormFile file,
        string assetDir,
        [FromForm] ShiftMethod shift)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music == null || file == null) return new ImportChartResult([new ImportChartMessage(Locale.FileUploadFailed, MessageLevel.Fatal)], true);
        if (file.FileName.EndsWith(".ma2"))
        {
            var targetChart = music.Charts[level];
            targetChart.Path = $"{id:000000}_0{level}.ma2";
            using var stream = System.IO.File.Open(Path.Combine(StaticSettings.StreamingAssets, assetDir, "music", $"music{id:000000}", targetChart.Path), FileMode.Create);
            file.CopyTo(stream);
            targetChart.Problems.Clear();

            // 检查新谱面ma2的音符数量是否有变化，如果有修正之
            string fileContent;
            using (var reader = new StreamReader(file.OpenReadStream()))
            {
                fileContent = reader.ReadToEnd();
            }
            var newMaxNotes = MaidataImportService.ParseTNumAllFromMa2(fileContent);
            if (newMaxNotes != 0 && targetChart.MaxNotes != newMaxNotes)
            {
                targetChart.MaxNotes = newMaxNotes;
            }
            music.Save();

            return new ImportChartResult([], false);
        }
        else if (file.FileName.EndsWith("maidata.txt"))
        {
            if (level != -1) return new ImportChartResult([new ImportChartMessage(Locale.MaidataReplaceAllOnly, MessageLevel.Fatal)], true);
            // 通过此前的谱面的定数是否为0，判断是否需要ignoreLevelNum
            bool ignoreLevelNum = true;
            foreach (var chart in music.Charts)
            {
                if (music.Id < 100000 && chart.Enable && chart.Level > 0) ignoreLevelNum = false;
            }
            var importResult = importService.ImportMaidata(music, file, shift, ignoreLevelNum, false, true);
            if (!importResult.Fatal)
            {
                music.Save();
                music.Refresh();
            }

            return importResult;
        }
        else return new ImportChartResult([new ImportChartMessage(Locale.UnsupportedChartFormat, MessageLevel.Fatal)], true);
    }
}