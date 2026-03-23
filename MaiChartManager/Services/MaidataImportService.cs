using System.Globalization;
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

    private static string Add1Bar(string maidata, float bpm)
    {
        var regex = BpmTagRegex();
        var bpmStr = regex.Match(maidata).Value;
        if (Math.Abs(float.Parse(bpmStr[1..^1], CultureInfo.InvariantCulture) - bpm) < 0.001f) // 本质是比较maidata中声明的(bpm)和我们参数传进来的bpm是否不等。以防浮点运算误差使用了range比较。
        {
            // 如果相等的话，则把新的小节插在bpmStr后面即可
            // 这里使用 {4},,,, 而不是 {1}, 因为要是谱面一开始根本没有写 {x} 的话，默认是 {4}。要是用了 {1}, 会覆盖默认的 {4}
            return string.Concat(bpmStr, "{4},,,,", maidata.AsSpan(bpmStr.Length));
        }
        else
        {
            return string.Concat($"({bpm})", "{4},,,,", maidata);
        }
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
    
    [GeneratedRegex(@"(\[[\d\.#:]+\])([bxfh]+)")]
    private static partial Regex SimaiError6(); // 类似1h[2:1]bx这种写法是不标准的，需要改为1hbx[2:1]

    private static string FixChartSimaiSharp(string chart)
    {
        chart = chart.Replace("\n", "").Replace("\r", "").Replace("{{", "{").Replace("}}", "}");
        chart = SimaiError1().Replace(chart, "$1,{");
        chart = SimaiError3().Replace(chart, "$1,(");
        chart = SimaiError2().Replace(chart, "[$1:$2]");
        chart = SimaiError4().Replace(chart, ",,");
        chart = SimaiError5().Replace(chart, "$1xq$2");
        chart = SimaiError6().Replace(chart, "$2$1");
        chart = chart.Replace("-?", "?-"); // 不飞的星星
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
            // 移除注释
            chartText = SimaiCommentRegex().Replace(chartText, "");
            chartText = SimaiCommentRegex2().Replace(chartText, "");
            return new SimaiParser().ChartOfToken(new SimaiTokenizer().TokensFromText(chartText));
        }
        catch (Exception)
        {
            // ignored
        }

        try
        {
            var normalizedText = FixChartSimaiSharp(chartText);
            var tokens = new SimaiTokenizer().TokensFromText(normalizedText);
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

    public static Dictionary<ShiftMethod, float> CalcChartPadding(List<MaiChart> charts, out float addBarBpm)
    {
        // 谱面导入时，会有两个地方涉及到时间的调整：
        // 1. 对谱面的调整。在下方的ImportMaidata函数中应用，对谱面进行相应的调整（用simaisharp移动一定的时间，或加上一小节）。
        // 2. 对音频的调整，通过向CueConverter.SetAudio API中传入padding参数，来对音频进行裁剪。
        //    - 详见Front/src/views/Charts/ImportCreateChartButton/ImportChartButton/index.tsx
        // 上述两个调整之间存在这样的关系：对音频的调整 一定等于 对谱面的调整 + 谱面本身蕴含的谱面相对于音频的偏移（即&first）。
        // 只要输入的谱面本身在simai的语义下正确，上述关系就必定是成立的。
        // PS：我们的代码里chartPadding为正表示谱面后移、音频开头相应加空白；而maidata的&first为正表示裁剪掉音频开头。因此实际计算中，应该满足的是audioPadding=chartPadding-&first。、
        // 因此，我们只需要在这里计算好每种ShiftMode下的 (1.对谱面的调整) chartPadding，发送给前端。前端-&first后作为 (2.对音频的调整) 发给SetAudioApi即可。
        
        // 首先计算一个概念：notePadding = bar - firstTiming
        // 其中，firstTiming是从谱面开头到谱面的第一押的时间。（PS：这里的谱面开头指的是经过&first修正后的、逻辑上的谱面开头。=原始音频文件的开头+&first修正。）
        // 因此，notePadding = bar - firstTiming 其实是一个 **衡量第一押距离第二小节开头有多远的量** 。
        // 当它是正数时表示第一押在第二小节开头的前面，（所以需要增加一小节/延后谱面）。负数则表示第一押已经在在第二小节开头的后面了。
        // PS：由于Bar模式下计算chartPadding时将会用到bpm，这里把notePadding和bpm一起返回
        var notePaddingOfEachChart = charts.Select(chart =>
        {
            var bpm = chart.TimingChanges[0].tempo;
            var bar = 60 / bpm * 4;
            
            var firstTiming = chart.NoteCollections[0].time; // 从谱面开头到谱面的第一押的时间
            var notePadding = bar - firstTiming;
            return (notePadding, bpm);
        }).ToList();
        
        // 取notePadding的最大值作为整个谱的偏移量，这是因为如果有多张谱面，我们需要保证所有谱面的第一押都移出第一小节之外
        var (notePadding, bpm) = notePaddingOfEachChart.Max();

        addBarBpm = 0f;
        var result = new Dictionary<ShiftMethod, float>();
        // 接下来为每种ShiftMode具体计算chartPadding：
        result[ShiftMethod.NoShift] = 0f; // NoShift时，显然
        
        // 由于notePadding的含义就是 *第一押距离第二小节开头的距离*，所以Legacy模式下为了把第一押对到第二小节开头上，所需的东西就是这个。
        result[ShiftMethod.Legacy] = notePadding; 
        
        // Bar模式下，（为了数值计算上的稳定），我们仅在notePadding > 0.01的情况下才addBar。
        if (notePadding > 0.01)
        {
            result[ShiftMethod.Bar] = 60 / bpm * 4; // 取值为bpm所对应的bar长度
            addBarBpm = bpm;
        }
        else
        {
            result[ShiftMethod.Bar] = 0f; // 否则，不对谱面做任何移动，等价于NoShift了。
        }
        
        return result;
    }

    private record AllChartsEntry(string chartText, MaiChart simaiSharpChart);
    
    [GeneratedRegex(@"^(CLK_DEF\t)(\d+\s*)$", RegexOptions.Multiline)]
    private static partial Regex ClockDefRegex();
    
    [GeneratedRegex(@"^(MET\t\d+\t\d+\t\d+\t)(\d+\s*)$", RegexOptions.Multiline)]
    private static partial Regex FirstMetLineRegex();
    
    private static string ApplyClockCountToFirstMetLine(string ma2Content, int clockCount)
    {
        ma2Content = ClockDefRegex().Replace(ma2Content, $"${{1}}{96 * clockCount}", 1);
        ma2Content = FirstMetLineRegex().Replace(ma2Content, $"${{1}}{clockCount}", 1);
        return ma2Content;
    }

    /** 根据maidata中定义的所有难度，将其映射到游戏中的难度。 **/
    public static Dictionary<int, int> MapMaidataLevelToGame(List<int> maidataLevels)
    {
        var result = new Dictionary<int, int>();
        var gameLevels = new bool[5];
        
        // 先映射标准难度谱面 绿红黄紫白
        for (int lv = 2; lv <= 6; lv++)
        {
            if (!maidataLevels.Contains(lv)) continue;
            var targetLevel = lv - 2;
            result.Add(lv, targetLevel);
            gameLevels[targetLevel] = true;
        }

        // 再映射非标准难度
        var nonStandardMappings = new[]
        {
            new { Levels = new[] { 7, 8 }, Targets = new[] { 3, 4, 0 } }, // lv7和8的匹配顺序：紫，白，绿
            new { Levels = new[] { 0 },    Targets = new[] { 0, 3, 4 } }  // lv0的匹配顺序：绿，紫，白
        };
        foreach (var mapping in nonStandardMappings)
        {
            foreach (var lv in mapping.Levels)
            {
                if (!maidataLevels.Contains(lv)) continue;
                foreach (var targetLevel in mapping.Targets)
                {
                    if (!gameLevels[targetLevel])
                    {
                        result.Add(lv, targetLevel);
                        gameLevels[targetLevel] = true;
                        break;
                    }
                }
            }
        }

        return result;
    }
    
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
            if (key.StartsWith("inote") || key.StartsWith("lv") || key.StartsWith("first") ||
                key.StartsWith("wholebpm")) maiData[key] = maiData[key].Trim();
        }

        var allCharts = new Dictionary<int, AllChartsEntry>();
        for (var i = 0; i < 9; i++)
        {
            if (i == 1) continue;
            if (!string.IsNullOrWhiteSpace(maiData.GetValueOrDefault($"inote_{i}")))
            {
                allCharts.Add(i, new AllChartsEntry(maiData[$"inote_{i}"], TryParseChartSimaiSharp(maiData[$"inote_{i}"], i, errors)));
            }
        }

        float.TryParse(maiData.GetValueOrDefault("first"), out var first);

        var chartPaddingDict = CalcChartPadding(allCharts.Values.Select(entry => entry.simaiSharpChart).ToList(), out var addBarBpm);
        var chartPadding = chartPaddingDict[shift]; // 当前所选择的模式所具体对应的chartPadding
        
        if (shift == ShiftMethod.Bar && chartPadding > 0)
        {
            foreach (var (level, chart) in allCharts)
            {
                var newText = Add1Bar(chart.chartText, addBarBpm);
                allCharts[level] = new AllChartsEntry(newText, TryParseChartSimaiSharp(newText, level, errors));
            }
            chartPadding = 0f; // 已经add1Bar过了，所以要防止后面的逻辑再次调用simaisharp的谱面平移
        }

        foreach (var targetChart in music.Charts)
        {
            targetChart.Enable = false;
        }

        float bpm = 0f;
        var targetLevelMap = MapMaidataLevelToGame(allCharts.Keys.ToList());
        foreach (var (level, chart) in allCharts)
        {
            // 宴会场只导入第一个谱面
            if (isUtage && music.Charts[0].Enable) break;

            // var levelPadding = CalcMusicPadding(chart, first);
            bpm = chart.simaiSharpChart.TimingChanges[0].tempo;
            // 一个小节多少秒
            var bar = 60 / bpm * 4;

            if (!targetLevelMap.TryGetValue(level, out var targetLevel)) continue; // 字典里没查到、说明这个难度是“被忽略的难度”
            if (isUtage) targetLevel = 0;
            bool touchSizeBig = !isUtage && level is 2 or 3; // 对绿谱和黄谱，设置为大的 touchSize

            var targetChart = music.Charts[targetLevel];
            targetChart.Path = $"{id:000000}_0{targetLevel}.ma2";
            var levelNumStr = maiData.GetValueOrDefault($"lv_{level}");
            if (!string.IsNullOrWhiteSpace(levelNumStr))
            {
                if (isUtage && !char.IsDigit(levelNumStr[0]))
                {
                    music.UtageKanji = levelNumStr.Substring(0, 1);
                    levelNumStr = levelNumStr.Substring(1).Replace("?", ""); // 为了处理类似“奏13+?”这种情况，留下13+给后面的逻辑处理
                }
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

            if (touchSizeBig)
            {
                foreach (var note in maiLibChart.Notes)
                {
                    if (note.NoteType is NoteEnum.NoteType.TTP or NoteEnum.NoteType.THO) note.TouchSize = "L1";
                }
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
            
            // 如果maidata中声明了clock_count，应用之，修改CLOCK_DEF和MET
            if (int.TryParse(maiData.GetValueOrDefault("clock_count"), out var clockCount))
            {
                if (clockCount > 0) shiftedConverted = ApplyClockCountToFirstMetLine(shiftedConverted, clockCount);
                else errors.Add(new ImportChartMessage("the \"&clock_count\" value in this maidata is invalid thus ignored.", MessageLevel.Warning));
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