using MaiChartManager.Models;
using MaiChartManager.Platform;
using MusicXml = MaiChartManager.Models.MusicXml;

namespace MaiChartManager.Controllers.Music.Services;

/// <summary>
/// 流派切换接口的错误码，前端据此显示本地化文案。
/// </summary>
internal static class GenreChangeError
{
    public const string MusicNotFound = "music-not-found";
    public const string NoEnabledChart = "no-enabled-chart";
    public const string MultipleEnabledCharts = "multiple-enabled-charts";
    public const string DoublePlayerChart = "double-player-chart";
    public const string TargetIdExists = "target-id-exists";
    public const string NoAvailableId = "no-available-id";
    public const string UtageIdNotRestorable = "utage-id-not-restorable";
    public const string LicenseRequired = "license-required";
    public const string InvalidMusicDirectory = "invalid-music-directory";
    public const string IdChangeFailed = "id-change-failed";
    public const string InvalidChartPath = "invalid-chart-path";
    public const string ChartFileNotFound = "chart-file-not-found";
    public const string SwapFailed = "swap-failed";
    public const string Unexpected = "unexpected";
}

/// <summary>
/// 乐曲在「宴会场」与其他流派之间切换时的谱面转换工具。
/// 宴会场只识别 Basic（绿谱）槽位的谱面，所以切到宴会场时要把唯一启用的难度整体挪到 Basic 槽位。
/// </summary>
internal static class UtageConverter
{
    public const int UtageGenreId = 107;

    /// <summary>
    /// 宴会场 ID 的起点。宴会场 ID 的规则是 `序号 * 10000 + 原曲 ID 的后四位`，序号从 10 开始，
    /// 所以同一首歌可以同时拥有 100xxx / 110xxx / 120xxx…… 多条宴会场谱面。
    /// </summary>
    public const int UtageIdStart = 100_000;

    /// <summary>
    /// 宴会场序号的最后一个。再往后就进 OocMod 的 7 位自定义曲目命名空间了，不算宴会场。
    /// </summary>
    public const int UtageMaxSeq = 99;

    /// <summary>
    /// 难度槽位数量（Basic ~ Re:Master）。Music.xml 里的第 6 个槽位不是难度，统计启用难度时要排除。
    /// </summary>
    public const int DifficultyCount = 5;

    /// <summary>是否为宴会场 ID（6 位，后四位是原曲 ID）</summary>
    public static bool IsUtageId(int id) => id is >= UtageIdStart and < 1_000_000;

    /// <summary>
    /// 宴会场 ID 对应的普通曲 ID（序号 10、11 分别对应 0、10000 号段）。
    /// 返回 null 表示序号太大、推不出普通曲 ID，只能让用户手动指定。
    /// </summary>
    public static int? RestoreNormalId(int utageId)
    {
        var restored = utageId - UtageIdStart;
        return IsUtageId(restored) ? null : restored;
    }

    /// <summary>
    /// 找出该曲下一个可用的宴会场 ID（后四位保持不变，只顺延序号）。
    /// 用来避开「目标 ID 已被别的宴会场谱面占用」，这样就不需要覆盖掉已有乐曲。
    /// 返回 null 表示序号已经用完。
    /// </summary>
    public static int? FindFreeUtageId(int musicId, Func<int, bool> isOccupied)
    {
        var baseId = musicId % 10000;
        for (var seq = UtageIdStart / 10000; seq <= UtageMaxSeq; seq++)
        {
            var candidate = seq * 10000 + baseId;
            if (!isOccupied(candidate)) return candidate;
        }

        return null;
    }

    /// <summary>
    /// 找出唯一启用的难度槽位。返回 null 表示通过，否则是错误码。
    /// 只数 Basic~Re:Master 这前 5 个槽位，和前端展示的难度口径保持一致。
    /// </summary>
    public static string? FindSingleEnabledChart(MusicXmlWithABJacket music, out int index)
    {
        index = -1;
        var enabledIndexes = new List<int>();
        for (var i = 0; i < Math.Min(music.Charts.Length, DifficultyCount); i++)
        {
            if (music.Charts[i].Enable) enabledIndexes.Add(i);
        }

        if (enabledIndexes.Count == 0) return GenreChangeError.NoEnabledChart;
        if (enabledIndexes.Count > 1) return GenreChangeError.MultipleEnabledCharts;

        index = enabledIndexes[0];
        return null;
    }

    /// <summary>
    /// 把 sourceIndex 槽位的谱面搬到 Basic 槽位，并把原难度禁用。已是 Basic 时不做任何事。
    /// 返回 null 表示成功，否则是错误码。
    /// </summary>
    public static string? MoveToBasic(MusicXmlWithABJacket music, int sourceIndex, ILogger logger)
        => SwapWithBasic(music, sourceIndex, sourceIsEnabled: false, requireSourceFile: true, logger);

    /// <summary>
    /// MoveToBasic 的逆操作，用于失败时回滚。
    /// 回滚时源难度的谱面文件可能已经不存在（它被挪到了 Basic），所以不校验文件。
    /// </summary>
    public static string? RestoreFromBasic(MusicXmlWithABJacket music, int sourceIndex, ILogger logger)
        => SwapWithBasic(music, sourceIndex, sourceIsEnabled: true, requireSourceFile: false, logger);

    private static string? SwapWithBasic(MusicXmlWithABJacket music, int sourceIndex, bool sourceIsEnabled,
        bool requireSourceFile, ILogger logger)
    {
        if (sourceIndex <= 0 || sourceIndex >= music.Charts.Length) return null;

        var musicDir = Path.GetDirectoryName(music.FilePath);
        if (string.IsNullOrWhiteSpace(musicDir) || !Directory.Exists(musicDir))
            return GenreChangeError.InvalidMusicDirectory;

        var basicChart = music.Charts[0];
        var otherChart = music.Charts[sourceIndex];

        // 谱面路径必须落在乐曲目录内，防止 XML 里的 ../ 之类内容去动别的文件
        if (!IsInsideMusicDir(musicDir, basicChart.Path) || !IsInsideMusicDir(musicDir, otherChart.Path))
            return GenreChangeError.InvalidChartPath;
        // 宴会场特有的双人谱（_L / _R）不能这样转，需要先改成普通宴会场乐曲
        if (HasSideChart(musicDir, otherChart.Path)) return GenreChangeError.DoublePlayerChart;
        // 源难度必须真的有谱面文件，否则转换后 Basic 的内容会和 XML 对不上。
        // 回滚方向不做这个校验：转换后源难度的文件已经挪走了。
        if (requireSourceFile && !File.Exists(Path.Combine(musicDir, otherChart.Path)))
            return GenreChangeError.ChartFileNotFound;

        var error = SwapChartFiles(musicDir, basicChart.Path, otherChart.Path, logger);
        if (error is not null) return error;

        // path 不动（槽位仍指向自己原来的文件名），只互换谱面信息
        SwapChartInfo(basicChart, otherChart);
        basicChart.Enable = !sourceIsEnabled;
        otherChart.Enable = sourceIsEnabled;

        music.Save();
        music.Refresh();
        return null;
    }

    private static bool HasSideChart(string musicDir, string path)
        => File.Exists(Path.Combine(musicDir, WithSuffix(path, "_L")))
           || File.Exists(Path.Combine(musicDir, WithSuffix(path, "_R")));

    /// <summary>
    /// 乐曲是否存在宴会场特有的双人谱（_L / _R）。这类谱面无法和普通谱面互相转换。
    /// </summary>
    public static bool HasSideCharts(MusicXmlWithABJacket music)
    {
        if (music.UtagePlayStyle == 1) return true;

        var musicDir = Path.GetDirectoryName(music.FilePath);
        if (string.IsNullOrWhiteSpace(musicDir) || !Directory.Exists(musicDir)) return false;

        foreach (var chart in music.Charts)
        {
            if (HasSideChart(musicDir, chart.Path)) return true;
        }

        return false;
    }

    private static bool IsInsideMusicDir(string musicDir, string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(musicDir, path));
            var root = Path.GetFullPath(musicDir) + Path.DirectorySeparatorChar;
            // Windows 文件系统大小写不敏感，Linux 敏感，比较方式要和平台一致
            return fullPath.StartsWith(root,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception)
        {
            // 非法路径字符等，直接当作不合法处理
            return false;
        }
    }

    private static void SwapChartInfo(MusicXml.Chart a, MusicXml.Chart b)
    {
        (a.Level, b.Level) = (b.Level, a.Level);
        (a.LevelDecimal, b.LevelDecimal) = (b.LevelDecimal, a.LevelDecimal);
        (a.LevelId, b.LevelId) = (b.LevelId, a.LevelId);
        (a.MaxNotes, b.MaxNotes) = (b.MaxNotes, a.MaxNotes);
        (a.Designer, b.Designer) = (b.Designer, a.Designer);
    }

    /// <summary>
    /// 互换两个槽位的谱面文件：源难度的谱面挪到 Basic 的文件名，旧的 Basic 谱面挪到源难度的文件名，
    /// 保证交换后两个槽位的 path 都仍然指向真实存在的文件。
    /// 任意一步失败都会按逆序回滚已经做过的移动。
    /// </summary>
    private static string? SwapChartFiles(string musicDir, string basicPath, string otherPath, ILogger logger)
    {
        var basicFile = Path.Combine(musicDir, basicPath);
        var otherFile = Path.Combine(musicDir, otherPath);
        var basicExists = File.Exists(basicFile);
        var otherExists = File.Exists(otherFile);

        // 两边都没有文件就没法交换（正常情况下调用方已经拦掉了）
        if (!basicExists && !otherExists) return GenreChangeError.ChartFileNotFound;

        var tempFile = Path.Combine(musicDir, $".mcmswap-{Guid.NewGuid():N}.tmp");

        var journal = new List<(string From, string To)>();

        try
        {
            // 1. 源谱面先挪到临时名，给旧的 Basic 谱面腾位置
            if (otherExists) Move(otherFile, tempFile);
            // 2. 旧的 Basic 谱面挪到源难度的文件名
            if (basicExists) Move(basicFile, otherFile);
            // 3. 临时文件挪到 Basic 的文件名
            if (otherExists) Move(tempFile, basicFile);
            return null;
        }
        catch (Exception e)
        {
            logger.LogError(e, "交换谱面文件失败，正在回滚");
            Rollback();
            return GenreChangeError.SwapFailed;
        }

        void Move(string from, string to)
        {
            // 不覆盖：出现同名文件说明状态异常，宁可失败走回滚
            PlatformFile.MoveFile(from, to, false);
            journal.Add((from, to));
        }

        void Rollback()
        {
            for (var i = journal.Count - 1; i >= 0; i--)
            {
                var (from, to) = journal[i];
                try
                {
                    if (File.Exists(to)) PlatformFile.MoveFile(to, from, false);
                }
                catch (Exception rollbackError)
                {
                    logger.LogError(rollbackError, "回滚谱面文件失败: {From} -> {To}", to, from);
                }
            }
        }
    }

    private static string WithSuffix(string path, string suffix) => path.Replace(".ma2", suffix + ".ma2");
}
