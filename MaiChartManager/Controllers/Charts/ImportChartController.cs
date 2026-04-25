using MaiChartManager.Controllers.Charts.Services;
using MaiChartManager.Controllers.Music;
using Microsoft.AspNetCore.Mvc;
using MuConvert.chart;
using MuConvert.generator;
using MuConvert.maidata;
using MuConvert.parser;
using MuConvert.utils;

namespace MaiChartManager.Controllers.Charts;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api")]
public class ImportChartController(StaticSettings settings, ILogger<StaticSettings> logger, 
    MaidataImportService importService, LegacyMaidataImportService legacyMaidataImportService) : ControllerBase
{
    public record ImportChartCheckResult(bool Accept, IEnumerable<ImportChartMessage> Errors, Dictionary<ShiftMethod, float> chartPaddings, bool IsDx, string? Title, float first, CueConvertController.SetAudioPreviewRequest? previewTime);

    [HttpPost]
    public ImportChartCheckResult ImportChartCheck(IFormFile file, [FromForm] bool isReplacement = false)
    {
        var errors = new List<ImportChartMessage>();
        var fatal = false;

        if (isReplacement)
        {
            // 替换谱面的操作也需要检查的过程，但检查的逻辑和导入谱面时可以说是一模一样的，故直接共用逻辑
            // 唯一的区别是给用户一个警告，明确说明直接替换谱面功能的适用范围
            errors.Add(new ImportChartMessage(Locale.NotesReplacementWarning, MessageLevel.Warning));
        }

        try
        {
            using var stream = file.OpenReadStream();
            var maiDataText = new StreamReader(stream).ReadToEnd();
            var maiData = new Maidata(maiDataText);
            var lineNoDict = MaidataImportService.GetLevelLineNo(maiDataText);

            var title = maiData.Title;
            if (string.IsNullOrWhiteSpace(title))
            {
                errors.Add(new ImportChartMessage(Locale.MusicNoTitle, MessageLevel.Fatal));
                fatal = true;
            }

            var targetLevelMap = MaidataImportService.MapMaidataLevelToGame(maiData);

            # region 向前端返回，关于导入谱面的inote_映射到游戏中的难度的提示信息
            string[] levelNames = [Locale.DifficultyBasic, Locale.DifficultyAdvanced, Locale.DifficultyExpert, Locale.DifficultyMaster, Locale.DifficultyReMaster];
            string[] importAsMessages = [Locale.DifficultyImportedAsBasic, null!, null!, Locale.DifficultyImportedAsMaster, Locale.DifficultyImportedAsReMaster];
            
            string generalImportMessage = ""; // “将导入以下难度：” 的默认信息
            var extraImportMessages = new List<string>(); // “有一个难度为 {0} 的谱面，将导入为XX谱 ” 的信息
            foreach (var (lv, _) in maiData.Levels)
            {
                if (!targetLevelMap.TryGetValue(lv, out var targetLevel))
                { // 根据targetLevelMap返回的结果，该谱面应被忽略
                    extraImportMessages.Add(string.Format(Locale.DifficultyIgnored, lv));
                    continue;
                }
                if (2 <= lv && lv <= 6)
                {
                    generalImportMessage += levelNames[targetLevel] + " ";
                }
                else
                {
                    extraImportMessages.Add(string.Format(importAsMessages[targetLevel], lv));
                }
            }
            
            if (!string.IsNullOrEmpty(generalImportMessage))
            {
                errors.Add(new ImportChartMessage(Locale.ImportingDifficulties + generalImportMessage, MessageLevel.Info));
            }

            foreach (var message in extraImportMessages)
            {
                errors.Add(new ImportChartMessage(message, MessageLevel.Warning));
            }
            # endregion

            if (targetLevelMap.Count == 0) // 没有能够被映射的谱面
            {
                errors.Add(new ImportChartMessage(Locale.MusicNoCharts, MessageLevel.Fatal));
                fatal = true;
                return new ImportChartCheckResult(!fatal, errors, new Dictionary<ShiftMethod, float>(), false, title, 0, null);
            }

            var first = maiData.First;
            var isDx = false;
            List<Chart> resultCharts = [];
            List<SimaiSharp.Structures.MaiChart> legacyCharts = [];
            foreach (var (lv, data) in maiData.Levels)
            {
                if (!targetLevelMap.ContainsKey(lv)) continue;

                if (StaticSettings.Config.UseLegacyMaiLib)
                {
                    try
                    {
                        var chart = legacyMaidataImportService.TryParseChartSimaiSharp(data.Inote, lv, errors);
                        legacyCharts.Add(chart);

                        var candidate = legacyMaidataImportService.TryParseChart(data.Inote, chart, lv, errors);
                        if (candidate is null) throw new Exception(Locale.ChartParseGenericError);
                        isDx = isDx || candidate.IsDxChart;
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "解析谱面失败");
                        errors.Add(new ImportChartMessage(string.Format(Locale.ChartDifficultyParseFailed, lv), MessageLevel.Fatal));
                        fatal = true;
                    }
                    continue;
                }
                // else
                
                // 转谱，并记录期间的警告等返回信息
                List<Alert> alerts = [];
                try
                {
                    //                                                 ↓ 此处的参数应该不会影响 check 的结果
                    var (chart, alerts1) = new SimaiParser(false, maiData.ClockCount).Parse(data.Inote);
                    resultCharts.Add(chart);
                    alerts.AddRange(alerts1);
                    var (_, alerts2) = new MA2Generator().Generate(chart);
                    alerts.AddRange(alerts2);
                    isDx = isDx || chart.IsDxChart;
                }
                catch (ConversionException e)
                {
                    fatal = true;
                    alerts.AddRange(e.Alerts);
                }
                foreach (var alert in alerts)
                {
                    var m = ImportChartMessage.FromAlert(alert, lv, lineNoDict);
                    if (m != null) errors.Add(m);
                }
            }

            Dictionary<ShiftMethod, float> chartPaddingsSec = new();
            if (!fatal && resultCharts.Count > 0)
            { // 如果解析失败了、导致没有产生resultCharts，那么就不要执行CalcChartPadding，不然会抛异常
                var chartPaddings = MaidataImportService.CalcChartPadding(resultCharts);
                chartPaddingsSec = chartPaddings.ToDictionary(x => x.Key, x => (float)x.Value.sec);
            }

            if (StaticSettings.Config.UseLegacyMaiLib && !fatal && legacyCharts.Count > 0)
            {
                chartPaddingsSec = LegacyMaidataImportService.CalcChartPadding(legacyCharts, out _);
            }

            CueConvertController.SetAudioPreviewRequest? previewTime = null;
            var maidataDemo = maiData.Demo;
            if (maidataDemo != null)
            {
                var (start, len) = maidataDemo.Value;
                // 当只有demo_seek没有demo_len时，则把demo_len设为一个很大的数，表示preview直到音频结尾；SetAudioPreviewApi中会自动把实际的loopEnd限制到音频长度以内。
                previewTime = new CueConvertController.SetAudioPreviewRequest(start, start + (len??10000f));
            }
            
            return new ImportChartCheckResult(!fatal, errors, chartPaddingsSec, isDx, title, first, previewTime);
        }
        catch (Exception e)
        {
            logger.LogError(e, "解析谱面失败（大）");
            errors.Add(new ImportChartMessage(Locale.ChartParseFailedGlobal, MessageLevel.Fatal));
            fatal = true;
            return new ImportChartCheckResult(!fatal, errors, new Dictionary<ShiftMethod, float>(), false, "", 0, null);
        }
    }
    
    [HttpPost]
    // 创建完 Music 后调用
    public ImportChartResult ImportChart(
        [FromForm] int id,
        IFormFile file,
        [FromForm] bool ignoreLevelNum,
        [FromForm] int addVersionId,
        [FromForm] int genreId,
        [FromForm] int version,
        [FromForm] string assetDir,
        [FromForm] ShiftMethod shift,
        [FromForm] bool debug = false)
    {
        var music = settings.GetMusic(id, assetDir);
        IMaidataImportService service = StaticSettings.Config.UseLegacyMaiLib ? legacyMaidataImportService : importService;
        var importMaidataResult = service.ImportMaidata(music!, file, shift, ignoreLevelNum, debug);
        if (!importMaidataResult.Fatal)
        {
            music!.AddVersionId = addVersionId;
            music.GenreId = genreId;
            music.Version = version;
            music.Save();
            music.Refresh();
        }

        return importMaidataResult;
    }
}