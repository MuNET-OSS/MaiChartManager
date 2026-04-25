using MaiChartManager.Models;
using MaiChartManager.Utils;
using MuConvert.chart;
using MuConvert.generator;
using MuConvert.maidata;
using MuConvert.parser;
using MuConvert.utils;
using Rationals;

namespace MaiChartManager.Controllers.Charts.Services;

public enum MessageLevel
{
    Info,
    Warning,
    Fatal
}

public record ImportChartMessage(string Message, MessageLevel Level)
{
    public static ImportChartMessage? FromAlert(Alert alert, int lv, Dictionary<int, int>? lineNoDict)
    {
        if (alert.Line != null && lineNoDict?.TryGetValue(lv, out var lineNo) == true)
            alert.Line += lineNo - 1; // 重写行号，以确保反映真实maidata中的行号
        switch (alert.Level)
        {
            case Alert.LEVEL.Error:
                return new ImportChartMessage(string.Format(Locale.ChartMaiLibParseError, lv, alert), MessageLevel.Fatal);
            case Alert.LEVEL.Warning:
                return new ImportChartMessage(string.Format(Locale.ChartPrefix, lv) + alert, MessageLevel.Warning);
            case Alert.LEVEL.Info:
                return new ImportChartMessage(string.Format(Locale.ChartPrefix, lv) + alert, MessageLevel.Info);
        }
        return null;
    }
};

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
public class MaidataImportService : IMaidataImportService
{
    private readonly ILogger<MaidataImportService> logger;

    public MaidataImportService(ILogger<MaidataImportService> logger)
    {
        this.logger = logger;
    }

    public static Dictionary<ShiftMethod, (double sec, decimal bpm, Rational bar)> CalcChartPadding(List<Chart> charts)
    {
        // 谱面导入时，会有两个地方涉及到时间的调整：
        // 1. 对谱面的调整。在下方的ImportMaidata函数中应用，对谱面进行相应的调整（chart.Shift）。
        // 2. 对音频的调整，通过向CueConverter.SetAudio API中传入padding参数，来对音频进行裁剪。
        //    - 详见Front/src/views/Charts/ImportCreateChartButton/ImportChartButton/index.tsx
        // 上述两个调整之间存在这样的关系：对音频的调整 一定等于 对谱面的调整 + 谱面本身蕴含的谱面相对于音频的偏移（即&first）。
        // 只要输入的谱面本身在simai的语义下正确，上述关系就必定是成立的。
        // PS：1. SetAudioApi和前端的显示文本是基于秒数的，但chart.Shift所需的参数是分数的小节时间+bpm。所以我们这里两种方式都进行计算、同时返回出来，各个API各取所需。
        // 2. 我们的代码里chartPadding为正表示谱面后移、音频开头相应加空白；而maidata的&first为正表示裁剪掉音频开头。因此实际计算中，应该满足的是audioPadding=chartPadding-&first。、
        // 因此，我们只需要在这里计算好每种ShiftMode下的 (1.对谱面的调整) chartPadding（秒为单位），发送给前端。前端-&first后作为 (2.对音频的调整) 发给SetAudioApi即可。
        
        // 首先计算一个概念：notePadding = bar - firstTiming
        // 其中，firstTiming是从谱面开头到谱面的第一押的时间。（PS：这里的谱面开头逻辑上的谱面开头，而非原始音频文件的开头）
        // 因此，notePadding = bar - firstTiming 其实是一个 **衡量第一押距离第二小节开头有多远的量** 。
        // 当它是正数时表示第一押在第二小节开头的前面，（所以需要增加一小节/延后谱面）。负数则表示第一押已经在在第二小节开头的后面了。
        // PS：如上所述，不同API所需的notePadding的单位不同，因此这里同时计算并返回秒数格式和分数小节格式的notePadding。
        var notePaddingOfEachChart = charts.Select(chart =>
        {
            var bpm = chart.StartBpm;
            var notePadding = (1 - chart.FirstNoteTime.InvariantBar).CanonicalForm;
            var sec = (double)(notePadding * (240 / (Rational)bpm));
            return (sec, bpm, notePadding);
        }).ToList();
        // 取notePadding的最大值作为整个谱的偏移量，这是因为如果有多张谱面，我们需要保证所有谱面的第一押都移出第一小节之外
        var notePadding = notePaddingOfEachChart.Max();
        
        // 接下来，根据notePadding所反映的（第一押所在位置的情况），为每种ShiftMode具体计算chartPadding：
        var result = new Dictionary<ShiftMethod, (double sec, decimal bpm, Rational bar)>();
        result[ShiftMethod.NoShift] = (0, notePadding.bpm, 0); // NoShift时，显然sec和bar格式的chartPadding都为0
        result[ShiftMethod.Legacy] = notePadding; // 由于notePadding的含义就是 *第一押距离第二小节开头的距离*，所以Legacy模式下为了把第一押对到第二小节开头上，所需的东西就是这个。
        // Bar模式下，（为了数值计算上的稳定），我们仅在notePadding > 0.01s的情况下才addBar。
        if (notePadding.sec > 0.01) result[ShiftMethod.Bar] = (240 / (double)notePadding.bpm, notePadding.bpm, 1); // 取值为1个小节，同时换算为s
        else result[ShiftMethod.Bar] = (0, notePadding.bpm, 0); // 否则，不对谱面做任何移动，等价于NoShift了。
        
        return result;
    }

    /** 根据maidata中定义的所有难度，将其映射到游戏中的难度。 **/
    private static Dictionary<int, int> MapMaidataLevelToGame(List<int> maidataLevels)
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
    
    public static Dictionary<int, int> MapMaidataLevelToGame(Maidata maidata) => MapMaidataLevelToGame(maidata.Levels.Select(x => x.Key).ToList());
    
    // 获取一个maidata文件中，每个 &inote_ 开头所对应的行号。
    public static Dictionary<int, int> GetLevelLineNo(string maidataText)
    {
        Dictionary<int, int> result = new();
        int lineNo = 0;
        int cur = -1; // 当前正在处理哪一个等级
        foreach (var line_ in maidataText.EnumerateLines())
        {
            var line = line_.ToString();
            lineNo++;
            if (line.StartsWith("&inote_") && line.IndexOf('=') is var p and >= 8 && int.TryParse(line[7..p], out var lv))
            {
                cur = lv; // 标记当前等级为pending状态
                line = line[(p+1)..];
            }
            if (cur != -1 && !string.IsNullOrWhiteSpace(line))
            { // 找到了当前等级的首个非空白行
                result[cur] = lineNo;
                cur = -1;
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
        var maiDataText = new StreamReader(stream).ReadToEnd();
        var maiData = new Maidata(maiDataText);
        var lineNoDict = GetLevelLineNo(maiDataText);
        
        var targetLevelMap = MapMaidataLevelToGame(maiData);
        if (targetLevelMap.Count == 0) // 没有能够被映射的谱面
        {
            errors.Add(new ImportChartMessage(Locale.MusicNoCharts, MessageLevel.Fatal));
            return new ImportChartResult(errors, true);
        }
        
        // 先执行第一步：Parser，因为可能涉及对Chart做出调整
        List<(int lv, int targetLevel, MaidataChart data, Chart chart, List<Alert> alerts)> parserOutput = [];
        foreach (var (lv, data) in maiData.Levels)
        {
            if (!targetLevelMap.ContainsKey(lv)) continue;
            if (isUtage && parserOutput.Count > 0) break; // 宴会场只导入第一个谱面
            
            var targetLevel = targetLevelMap[lv]; // 在MA2中的目标等级
            if (isUtage) targetLevel = 0;
            try
            {
                var parser = new SimaiParser(!isUtage && lv is 2 or 3, maiData.ClockCount);
                var (chart, alerts) = parser.Parse(data.Inote);
                parserOutput.Add((lv, targetLevel, data, chart, alerts));
            }
            catch (ConversionException e)
            {
                parserOutput.Add((lv, targetLevel, data, null!, e.Alerts));
                MergeAlertsIntoImportChartMessages();
                return new ImportChartResult(errors, true);
            }
        }
        
        var chartPaddingDict = CalcChartPadding(parserOutput.Select(x=>x.chart).ToList());
        var chartPadding = chartPaddingDict[shift]; // 当前所选择的模式所具体对应的chartPadding

        foreach (var c in music.Charts) { c.Enable = false; } // 先把所有难度标记为关闭（马上后面"第二步"的逻辑，会对存在的难度打开）
        
        float lastChartBpm = 0; // 最后一个谱面的bpm，当没有指定wholebpm时用作fallback
        // 再执行第二步
        foreach (var (lv, targetLevel, data, chart, alerts) in parserOutput)
        {
            var targetChart = music.Charts[targetLevel];
            targetChart.Path = $"{id:000000}_0{targetLevel}.ma2";
            
            #region 计算等级（定数）相关
            var levelNumStr = data.Level;
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
            #endregion

            string resultMA2;
            try
            {
                lastChartBpm = (float)chart.StartBpm;
                if (chartPadding.bar != 0)
                {
                    chart.Shift(chartPadding.bar, chartPadding.bpm);
                    logger.LogInformation($"通过chart.Shift应用了{chartPadding}小节的偏移");
                }
                var (r, alerts2) = new MA2Generator(isUtage).Generate(chart);
                resultMA2 = r;
                alerts.AddRange(alerts2);
            }
            catch (ConversionException e)
            {
                alerts.AddRange(e.Alerts);
                MergeAlertsIntoImportChartMessages();
                return new ImportChartResult(errors, true);
            }

            targetChart.Designer = data.NoteDesigner ?? maiData.GetValueOrDefault("des") ?? "";
            targetChart.MaxNotes = chart.Statistics.Total;
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(music.FilePath)!, targetChart.Path), resultMA2);
            targetChart.Enable = true;
        }

        if (!isReplacement)
        {
            // 只在新建时设定曲目信息，替换时不设定
            music.Name = maiData.Title;
            music.Artist = maiData.Artist;
            music.ShiftMethod = shift.ToString();
            music.Bpm = maiData.WholeBpm ?? lastChartBpm; // 优先使用&wholebpm，但如果不存在，则使用谱面开头声明的bpm
        }

        MergeAlertsIntoImportChartMessages();
        return new ImportChartResult(errors, false);

        void MergeAlertsIntoImportChartMessages()
        {
            foreach (var one in parserOutput)
            {
                foreach (var alert in one.alerts)
                {
                    var m = ImportChartMessage.FromAlert(alert, one.lv, lineNoDict);
                    if (m != null) errors.Add(m);
                }
            }
        }
    }
    
    // 正常Simai导入为MA2的逻辑已经不用这个了，但ReplaceChartApi中有一种情况是上传一个MA2文件、直接替换MA2文件，就还需要这个，所以得留着
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