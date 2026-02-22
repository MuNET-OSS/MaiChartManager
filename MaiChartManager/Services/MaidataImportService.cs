using System.Text.RegularExpressions;
using MaiChartManager.Models;
using MaiChartManager.Utils;
using MaiLib;
using SimaiSharp;
using SimaiSharp.Structures;

namespace MaiChartManager.Services;

public enum MessageLevel
{
    Info,
    Warning,
    Fatal
}

public record ImportChartMessage(string Message, MessageLevel Level);

public record ImportChartResult(IEnumerable<ImportChartMessage> Errors, bool Fatal);

// v1.1.2 新增
public enum ShiftMethod
{
    // 之前的办法，把第一押准确的对在第二小节的开头
    // noShiftChart = false, padding = MusicPadding
    Legacy,

    // 简单粗暴的办法，不需要让库来平移谱面，解决各种平移不兼容问题
    // 之前修库都白修了其实
    // bar - 休止符的长度 如果是正数，那就直接在前面加一个小节的空白
    // 判断一下 > 0.1 好了，因为 < 0.1 秒可以忽略不计
    // noShiftChart = true, padding = (bar - 休止符的长度 > 0.1 ? bar - first : 0)
    // bar - 休止符的长度 = MusicPadding + first
    Bar,

    // 把音频裁掉 &first 秒，完全不用动谱面
    // noShiftChart = true, padding = -first
    NoShift
}
public partial class MaidataImportService
{
    private readonly ILogger<MaidataImportService> logger;

    public MaidataImportService(ILogger<MaidataImportService> logger)
    {
        this.logger = logger;
    }

    [GeneratedRegex(@"^\([\d\.]+\)")]
    private static partial Regex BpmTagRegex();

    [GeneratedRegex(@"\{([\d\.]+)\}")]
    public static partial Regex MeasureTagRegex();

    private static string Add1Bar(string maidata)
    {
        var regex = BpmTagRegex();
        var bpm = regex.Match(maidata).Value;
        // 这里使用 {4},,,, 而不是 {1}, 因为要是谱面一开始根本没有写 {x} 的话，默认是 {4}。要是用了 {1}, 会覆盖默认的 {4}
        return string.Concat(bpm, "{4},,,,", maidata.AsSpan(bpm.Length));
    }

    [GeneratedRegex(@"(\d){")]
    private static partial Regex SimaiError1();

    [GeneratedRegex(@"\[(\d+)-(\d+)]")]
    private static partial Regex SimaiError2();

    [GeneratedRegex(@"(\d)\(")]
    private static partial Regex SimaiError3();

    [GeneratedRegex(@",[csbx\.\{\}],")]
    private static partial Regex SimaiError4();

    [GeneratedRegex(@"(\d)qx(\d)")]
    private static partial Regex SimaiError5();

    private static string FixChartSimaiSharp(string chart)
    {
        chart = chart.Replace("\n", "").Replace("\r", "").Replace("{{", "{").Replace("}}", "}");
        chart = SimaiError1().Replace(chart, "$1,{");
        chart = SimaiError3().Replace(chart, "$1,(");
        chart = SimaiError2().Replace(chart, "[$1:$2]");
        chart = SimaiError4().Replace(chart, ",,");
        chart = SimaiError5().Replace(chart, "$1xq$2");
        return chart;
    }

    public MaiChart TryParseChartSimaiSharp(string chartText, int level, List<ImportChartMessage> errors)
    {
        chartText = chartText.ReplaceLineEndings();
        try
        {
            return SimaiConvert.Deserialize(chartText);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "SimaiSharp 无法直接解析谱面");
        }

        try
        {
            var chart = SimaiConvert.Deserialize(FixChartSimaiSharp(chartText));
            errors.Add(new ImportChartMessage(string.Format(Locale.ChartFixedMinorErrors, level), MessageLevel.Info));
            return chart;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "SimaiSharp 无法解析修复后谱面");
            throw;
        }
    }

    [GeneratedRegex(@"\|\|.*$", RegexOptions.Multiline)]
    private static partial Regex SimaiCommentRegex();

    /*
     * 根据[simai文档](https://w.atwiki.jp/simai/pages/1002.html)，井号如果出现在[]或{}内是合法语法，并非注释。
     * 因此在尝试匹配注释时，应该排除掉#前面有未闭合的[或{的情况。
     */
    [GeneratedRegex(@"(?<!\[[^\]]*|\{[^\}]*)#.*$", RegexOptions.Multiline)]
    private static partial Regex SimaiCommentRegex2();

    [GeneratedRegex(@"Original Stack.*", RegexOptions.Singleline)]
    private static partial Regex MaiLibErrMsgRegex();

    public Chart? TryParseChart(string chartText, MaiChart? simaiSharpChart, int level, List<ImportChartMessage> errors)
    {
        chartText = chartText.ReplaceLineEndings();
        try
        {
            return new SimaiParser().ChartOfToken(new SimaiTokenizer().TokensFromText(chartText));
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "无法直接解析谱面");
        }

        try
        {
            var normalizedText = SimaiCommentRegex().Replace(chartText, "");
            normalizedText = SimaiCommentRegex2().Replace(normalizedText, "");
            return new SimaiParser().ChartOfToken(new SimaiTokenizer().TokensFromText(normalizedText));
        }
        catch (Exception)
        {
            // ignored
        }

        try
        {
            var normalizedText = FixChartSimaiSharp(chartText)
                // 不飞的星星
                .Replace("-?", "?-");
            // 移除注释
            normalizedText = SimaiCommentRegex().Replace(normalizedText, "");
            normalizedText = SimaiCommentRegex2().Replace(normalizedText, "");
            var tokens = new SimaiTokenizer().TokensFromText(normalizedText);
            for (var i = 0; i < tokens.Length; i++)
            {
                if (tokens[i].Contains("]b"))
                {
                    tokens[i] = tokens[i].Replace("]b", "]").Replace("[", "b[");
                }
            }

            var maiLibChart = new SimaiParser().ChartOfToken(tokens);
            errors.Add(new ImportChartMessage(string.Format(Locale.ChartFixedMinorErrors, level), MessageLevel.Info));
            return maiLibChart;
        }
        catch (Exception e)
        {
            errors.Add(new ImportChartMessage(string.Format(Locale.ChartMaiLibParseError, level, MaiLibErrMsgRegex().Replace(e.Message, "")), MessageLevel.Warning));
            logger.LogWarning(e, "无法在手动修正错误后解析谱面");
        }

        if (simaiSharpChart is null)
        {
            return null;
        }

        try
        {
            var reSerialized = SimaiConvert.Serialize(simaiSharpChart);
            reSerialized = reSerialized.Replace("{0}", "{4}");
            var maiLibChart = new SimaiParser().ChartOfToken(new SimaiTokenizer().TokensFromText(reSerialized));
            errors.Add(new ImportChartMessage(string.Format(Locale.ChartSimaiSharpFallback, level), MessageLevel.Warning));
            return maiLibChart;
        }
        catch (Exception e)
        {
            SentrySdk.CaptureException(e);
            errors.Add(new ImportChartMessage(string.Format(Locale.ChartParseFailed, level), MessageLevel.Fatal));
            return null;
        }
    }

    public static float CalcMusicPadding(MaiChart chart, float first)
    {
        // TimingChanges 对应的是所有的 {int}
        var bpm = chart.TimingChanges[0].tempo;
        // 一小节多长
        var bar = 60 / bpm * 4;

        // 第一押什么时候出来
        var firstTiming = chart.NoteCollections[0].time + first;
        return bar - firstTiming;
    }

    private record AllChartsEntry(string chartText, MaiChart simaiSharpChart);

    public ImportChartResult ImportMaidata(
        MusicXml music,
        IFormFile file,
        ShiftMethod shift,
        bool ignoreLevelNum,
        bool debug,
        bool isReplacement = false)
    {
        var id = music.Id;
        var isUtage = id > 100000;
        var errors = new List<ImportChartMessage>();
        using var stream = file.OpenReadStream();
        var kvps = new SimaiFile(stream).ToKeyValuePairs();
        var maiData = new Dictionary<string, string>();
        foreach (var (key, value) in kvps)
        {
            maiData[key] = value;
        }

        var allCharts = new Dictionary<int, AllChartsEntry>();
        for (var i = 2; i < 9; i++)
        {
            if (!string.IsNullOrWhiteSpace(maiData.GetValueOrDefault($"inote_{i}")))
            {
                allCharts.Add(i, new AllChartsEntry(maiData[$"inote_{i}"], TryParseChartSimaiSharp(maiData[$"inote_{i}"], i, errors)));
            }
        }

        if (!string.IsNullOrWhiteSpace(maiData.GetValueOrDefault("inote_0")))
        {
            allCharts.Add(0, new AllChartsEntry(maiData["inote_0"], TryParseChartSimaiSharp(maiData["inote_0"], 0, errors)));
        }

        float.TryParse(maiData.GetValueOrDefault("first"), out var first);
        // Mai 的歌曲是从两帧后开始播放的
        first -= 1 / 30f;

        var paddings = allCharts.Values.Select(chart => CalcMusicPadding(chart.simaiSharpChart, first)).ToList();
        // 音频前面被增加了多少
        var audioPadding = paddings.Max(); // bar - firstTiming = bar - 谱面前面休止符的时间 - &first
        var shouldAddBar = false;
        float chartPadding;
        switch (shift)
        {
            case ShiftMethod.Legacy:
                chartPadding = audioPadding + first;
                break;
            case ShiftMethod.Bar when audioPadding + first > 0.1:
                shouldAddBar = true;
                chartPadding = 0f;
                break;
            default:
                chartPadding = 0f;
                break;
        }

        if (shouldAddBar)
        {
            foreach (var (level, chart) in allCharts)
            {
                var newText = Add1Bar(chart.chartText);
                allCharts[level] = new AllChartsEntry(newText, TryParseChartSimaiSharp(newText, level, errors));
            }
        }

        foreach (var targetChart in music.Charts)
        {
            targetChart.Enable = false;
        }

        float bpm = 0f;
        foreach (var (level, chart) in allCharts)
        {
            // 宴会场只导入第一个谱面
            if (isUtage && music.Charts[0].Enable) break;

            // var levelPadding = CalcMusicPadding(chart, first);
            bpm = chart.simaiSharpChart.TimingChanges[0].tempo;
            // 一个小节多少秒
            var bar = 60 / bpm * 4;

            // 我们要让这个谱面真正的内容（忽略 first）延后多少
            // levelPadding 似乎不需要算，因为每个谱面真正的内容都是从同一个地方开始
            // 所以只要在前面加上 audioPadding + first 时间的休止符
            // 最早出音符的那个谱面的第一押之前一定是 1bar（小节）的休止符
            //       |_| levelPadding
            // |_______| audioPadding
            //       |________________| bar
            // |________________| bar
            // |-------|---|----|-----|-----
            // |       |   |    |     | 这个谱面的第一押
            // |       |   |    | 可能是另一个谱面难度的第一押，firstTiming，它可能导致 audioPadding > levelPadding
            // |       |   |____| 这一段是休止符
            // |       |   | 每个谱面真正的内容都是从这里开始
            // |       |___| first skip 掉的部分
            // |       | 原先音频的开头
            // | 加了 padding 的音频开头

            # region 设定 targetLevel

            var targetLevel = level - 2;

            // 处理非标准难度
            if (level is > 6 or < 1)
            {
                // 分给 3 4 0
                if (!music.Charts[3].Enable)
                {
                    targetLevel = 3;
                }
                else if (!music.Charts[4].Enable)
                {
                    targetLevel = 4;
                }
                else if (!music.Charts[0].Enable)
                {
                    targetLevel = 0;
                }
                else
                {
                    continue;
                }
            }

            if (isUtage) targetLevel = 0;

            # endregion

            var targetChart = music.Charts[targetLevel];
            targetChart.Path = $"{id:000000}_0{targetLevel}.ma2";
            var levelNumStr = maiData.GetValueOrDefault($"lv_{level}");
            if (!string.IsNullOrWhiteSpace(levelNumStr))
            {
                levelNumStr = levelNumStr.Replace("+", ".7");
            }

            float.TryParse(levelNumStr, out var levelNum);
            targetChart.LevelId = MaiUtils.GetLevelId((int)(levelNum * 10));
            // 忽略定数
            if (!ignoreLevelNum)
            {
                targetChart.Level = (int)Math.Floor(levelNum);
                targetChart.LevelDecimal = (int)Math.Floor(levelNum * 10 % 10);
            }

            targetChart.Designer = maiData.GetValueOrDefault($"des_{level}") ?? maiData.GetValueOrDefault("des") ?? "";
            var maiLibChart = TryParseChart(chart.chartText, chart.simaiSharpChart, level, errors);
            if (maiLibChart is null)
            {
                return new ImportChartResult(errors, true);
            }

            var originalConverted = maiLibChart.Compose(ChartEnum.ChartVersion.Ma2_104);

            if (debug)
            {
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(music.FilePath)!, targetChart.Path + ".afterSimaiSharp.txt"), SimaiConvert.Serialize(chart.simaiSharpChart));
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(music.FilePath)!, targetChart.Path + ".preShift.ma2"), originalConverted);
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(music.FilePath)!, targetChart.Path + ".preShift.txt"), maiLibChart.Compose(ChartEnum.ChartVersion.SimaiFes));
            }

            if (chartPadding != 0)
            {
                try
                {
                    maiLibChart.ShiftByOffset((int)Math.Round(chartPadding / bar * maiLibChart.Definition));
                }
                catch (Exception e)
                {
                    SentrySdk.CaptureEvent(new SentryEvent(e)
                    {
                        Message = Locale.ChartShiftByOffsetError
                    });
                    errors.Add(new ImportChartMessage(Locale.ChartShiftError, MessageLevel.Fatal));
                    return new ImportChartResult(errors, true);
                }
            }

            var shiftedConverted = maiLibChart.Compose(ChartEnum.ChartVersion.Ma2_104);

            if (shiftedConverted.Split('\n').Length != originalConverted.Split('\n').Length)
            {
                errors.Add(new ImportChartMessage(Locale.ChartNotesMissing, MessageLevel.Warning));
                logger.LogWarning("BUG! shiftedConverted: {shiftedLen}, originalConverted: {originalLen}", shiftedConverted.Split('\n').Length, originalConverted.Split('\n').Length);
            }

            // Just use T_NUM_ALL value in ma2 file
            targetChart.MaxNotes = ParseTNumAllFromMa2(shiftedConverted);
            // Fallback to maiLibChart if T_NUM_ALL not found
            if (targetChart.MaxNotes == 0) targetChart.MaxNotes = maiLibChart.AllNoteNum;

            File.WriteAllText(Path.Combine(Path.GetDirectoryName(music.FilePath)!, targetChart.Path), shiftedConverted);
            if (debug)
            {
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(music.FilePath)!, targetChart.Path + ".afterShift.txt"), maiLibChart.Compose(ChartEnum.ChartVersion.SimaiFes));
            }

            targetChart.Enable = true;
        }

        if (!isReplacement)
        {
            // 只在新建时设定曲目信息，替换时不设定
            music.Name = maiData["title"];
            music.Artist = maiData.GetValueOrDefault("artist") ?? "";
            music.ShiftMethod = shift.ToString();
            float wholebpm;
            if (float.TryParse(maiData.GetValueOrDefault("wholebpm"), out wholebpm))
                music.Bpm = wholebpm; // 优先使用&wholebpm
            else music.Bpm = bpm;     // 如果不存在，则使用谱面中开头声明的bpm
        }

        return new ImportChartResult(errors, false);
    }

    public static int ParseTNumAllFromMa2(string ma2Content)
    {
        var lines = ma2Content.Split('\n');
        // 从后往前读取，因为 T_NUM_ALL 在文件最后
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var trimmedLine = lines[i].Trim();
            if (trimmedLine.StartsWith("T_NUM_ALL", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmedLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[1], out int tNumAll))
                {
                    return tNumAll;
                }
            }
        }
        // Fallback to 0 in case
        return 0;
    }
}