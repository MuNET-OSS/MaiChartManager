using MaiChartManager.Models;
using MaiChartManager.Platform;
using MusicXml = MaiChartManager.Models.MusicXml;

namespace MaiChartManager.Controllers.Music;

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
    public const string LicenseRequired = "license-required";
    public const string InvalidMusicDirectory = "invalid-music-directory";
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
    public const int UtageIdOffset = 100000;

    /// <summary>
    /// 找出唯一启用的难度槽位。返回 null 表示通过，否则是错误码。
    /// </summary>
    public static string? FindSingleEnabledChart(MusicXmlWithABJacket music, out int index)
    {
        index = -1;
        var enabledIndexes = new List<int>();
        for (var i = 0; i < music.Charts.Length; i++)
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
        => SwapWithBasic(music, sourceIndex, sourceIsEnabled: false, logger);

    /// <summary>
    /// MoveToBasic 的逆操作，用于失败时回滚。
    /// </summary>
    public static string? RestoreFromBasic(MusicXmlWithABJacket music, int sourceIndex, ILogger logger)
        => SwapWithBasic(music, sourceIndex, sourceIsEnabled: true, logger);

    private static string? SwapWithBasic(MusicXmlWithABJacket music, int sourceIndex, bool sourceIsEnabled,
        ILogger logger)
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
        // 源难度必须真的有谱面文件，否则转换后 Basic 的内容会和 XML 对不上
        if (!File.Exists(Path.Combine(musicDir, otherChart.Path))) return GenreChangeError.ChartFileNotFound;

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

    private static bool IsInsideMusicDir(string musicDir, string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(musicDir, path));
            var root = Path.GetFullPath(musicDir) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
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
        var tempFile = Path.Combine(musicDir, $".mcmswap-{Guid.NewGuid():N}.tmp");

        var journal = new List<(string From, string To)>();

        try
        {
            // 1. 源谱面先挪到临时名，给旧的 Basic 谱面腾位置
            Move(otherFile, tempFile);
            // 2. 旧的 Basic 谱面挪到源难度的文件名
            if (File.Exists(basicFile)) Move(basicFile, otherFile);
            // 3. 临时文件挪到 Basic 的文件名
            Move(tempFile, basicFile);
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
