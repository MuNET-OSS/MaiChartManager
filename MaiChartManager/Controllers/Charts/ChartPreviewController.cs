using Microsoft.AspNetCore.Mvc;
using MuConvert.mai;

namespace MaiChartManager.Controllers.Charts;

[ApiController]
[Route("MaiChartManagerServlet/[controller]Api/{assetDir}/{id:int}/{level:int}")]
public class ChartPreviewController(StaticSettings settings) : ControllerBase
{
    [HttpGet]
    public string Maidata(int id, int level, string assetDir)
    {
        var music = settings.GetMusic(id, assetDir);
        var chart = music?.Charts[level];
        if (chart == null)
        {
            return "No chart found";
        }

        var path = Path.Combine(Path.GetDirectoryName(music!.FilePath)!, chart.Path);
        if (!System.IO.File.Exists(path))
        {
            return "No chart found";
        }

        var ma2Content = System.IO.File.ReadAllText(path);
        var (cvtChart, _) = new MA2Parser().Parse(ma2Content);
        var (simai, _) = new SimaiGenerator().Generate(cvtChart);
        
        return $"""
                &first=0
                &lv_1=1
                &inote_1={simai}
                """;
    }
}
