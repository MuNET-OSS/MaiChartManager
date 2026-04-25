using MaiChartManager.Models;

namespace MaiChartManager.Controllers.Charts.Services;

public interface IMaidataImportService
{
    ImportChartResult ImportMaidata(MusicXml music, IFormFile file, ShiftMethod shift, bool ignoreLevelNum, bool debug, bool isReplacement = false);
}