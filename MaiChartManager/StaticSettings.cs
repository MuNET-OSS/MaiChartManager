using System.Text.RegularExpressions;
using System.Xml;
using AquaMai.Config.Interfaces;
using MaiChartManager.Models;
using MaiChartManager.Utils;

namespace MaiChartManager;

public partial class StaticSettings
{
    public static readonly string tempPath = Path.Combine(Path.GetTempPath(), "MaiChartManager");
    public static readonly string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MaiChartManager");
    public static readonly string exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
#if DEBUG
    public static readonly string wwwroot = Path.Combine(ProjectDir, "wwwroot");
    private static string ProjectDir => Path.GetDirectoryName(GetThisFilePath())!;
    private static string GetThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string? path = null) => path!;
#else
    public static readonly string wwwroot = Path.Combine(exeDir, "wwwroot");
#endif

    private static string _imageAssetsDir = "LocalAssets";
    private static string _movieAssetsDir = "LocalAssets";
    private static string _skinAssetsDir = "LocalAssets/Skins";
    public static string ImageAssetsDir => Path.Combine(GamePath, _imageAssetsDir);
    public static string MovieAssetsDir => Path.Combine(GamePath, _movieAssetsDir);
    public static string SkinAssetsDir => Path.Combine(GamePath, _skinAssetsDir);
    public static List<string> StartupErrorsList { get; } = new();

    public static Config Config { get; set; } = new();
    public static string CurrentLocale { get; set; } = "zh";

    private readonly ILogger<StaticSettings> _logger;
    private readonly Controllers.Mod.ModConfigService _modConfigService;

    public StaticSettings(ILogger<StaticSettings> logger, Controllers.Mod.ModConfigService modConfigService)
    {
        _logger = logger;
        _modConfigService = modConfigService;
        if (string.IsNullOrEmpty(GamePath)) return; // OOBE mode: skip scan
        try
        {
            GetGameVersion();
            RescanAll().GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "初始化数据目录时出错");
            SentrySdk.CaptureException(e);
            throw new InvalidOperationException(Locale.InitDataDirError, e);
        }
    }

    public async Task InitializeGameData()
    {
        try
        {
            GetGameVersion();
            await RescanAll();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "初始化数据目录时出错");
            SentrySdk.CaptureException(e);
            throw new InvalidOperationException(Locale.InitDataDirError, e);
        }
    }

    [GeneratedRegex(@"^[A-Z](\d{3})$")]
    public static partial Regex ADirRegex();

    // 默认空字符串而非 null：未配置游戏目录（OOBE 阶段）时，下游 Path.Combine(GamePath, ...)
    // 不会因 null 抛 ArgumentNullException（空字符串得到相对路径，后续 Directory/File.Exists 返回 false，优雅降级）。
    public static string GamePath { get; set; } = "";
    public static string StreamingAssets => Path.Combine(GamePath, "Sinmai_Data", "StreamingAssets");

    public static IEnumerable<string> AssetsDirs => Directory.Exists(StreamingAssets)
        ? Directory.EnumerateDirectories(StreamingAssets).Select(Path.GetFileName).Where(it => ADirRegex().IsMatch(it))
        : [];

    /// <summary>
    /// 在父目录下按名称大小写不敏感地解析子目录的真实路径，找不到返回 null。
    /// 用于兼容 Linux 大小写敏感文件系统：游戏目录在 Windows 下大小写随意（如 musicVersion / MusicVersion），
    /// 直接 Path.Combine 固定大小写会在 Linux 上匹配不到。优先尝试精确路径以避免多数情况下的额外枚举。
    /// </summary>
    public static string? ResolveSubDir(string parent, string name)
    {
        var exact = Path.Combine(parent, name);
        if (Directory.Exists(exact)) return exact;
        if (!Directory.Exists(parent)) return null;
        return Directory.EnumerateDirectories(parent)
            .FirstOrDefault(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));
    }

    public int gameVersion;
    private List<MusicXmlWithABJacket> _musicList = [];
    public static List<GenreXml> GenreList { get; set; } = [];
    public static List<VersionXml> VersionList { get; set; } = [];
    public static Dictionary<int, string> AssetBundleJacketMap { get; set; } = new();
    public static Dictionary<int, string> PseudoAssetBundleJacketMap { get; set; } = new();
    public static Dictionary<int, string> MovieDataMap { get; set; } = new();
    public static Dictionary<string, string> AcbAwb { get; set; } = new();

    public MusicXmlWithABJacket? GetMusic(int id, string assetDir)
    {
        return _musicList.FirstOrDefault(it => it.Id == id && it.AssetDir == assetDir);
    }

    public List<MusicXmlWithABJacket> GetMusicList()
    {
        return _musicList;
    }

    public async Task RescanAll()
    {
        GetGameVersion();
        StartupErrorsList.Clear();
        try
        {
            var config = await _modConfigService.GetCurrentAquaMaiConfig();
            UpdateAssetPathsFromAquaMaiConfig(config);
        }
        catch (Exception)
        {
            Console.WriteLine("无法获取 AquaMai 配置");
        }
        ScanMusicList();
        ScanGenre();
        ScanVersionList();
        ScanAssetBundles();
        ScanSoundData();
        ScanMovieData();
    }

    public void ScanMusicList()
    {
        _musicList.Clear();
        foreach (var a in AssetsDirs)
        {
            var musicDir = ResolveSubDir(Path.Combine(StreamingAssets, a), "music");
            if (musicDir is null) continue;

            foreach (var subDir in Directory.EnumerateDirectories(musicDir))
            {
                if (!File.Exists(Path.Combine(subDir, "Music.xml"))) continue;
                try
                {
                    var musicXml = new MusicXmlWithABJacket(Path.Combine(subDir, "Music.xml"), GamePath, a);
                    _musicList.Add(musicXml);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "加载乐曲数据 {SubDir} 失败", subDir);
                    SentrySdk.CaptureException(ex);
                    StartupErrorsList.Add(string.Format(Locale.LoadMusicDataFailed, subDir, ex.Message));
                }
            }
        }

        _logger.LogInformation("扫描音乐列表，共找到 {0} 首音乐。", _musicList.Count);
    }

    public void ScanGenre()
    {
        GenreList.Clear();

        foreach (var a in AssetsDirs)
        {
            // 大小写不敏感解析 musicGenre 目录；枚举全部子目录后用大小写不敏感的前缀过滤（不用 glob，避免 Linux 区分大小写匹配不到）。
            var genreParent = ResolveSubDir(Path.Combine(StreamingAssets, a), "musicGenre");
            if (genreParent is null) continue;
            foreach (var genreDir in Directory.EnumerateDirectories(genreParent))
            {
                var dirName = Path.GetFileName(genreDir);
                if (!dirName.StartsWith("musicgenre", StringComparison.InvariantCultureIgnoreCase)) continue;
                if (!File.Exists(Path.Combine(genreDir, "MusicGenre.xml"))) continue;
                try
                {
                    var id = int.Parse(dirName.Substring("musicgenre".Length));
                    var genreXml = new GenreXml(id, a, GamePath);

                    var existed = GenreList.Find(it => it.Id == id);
                    if (existed != null)
                    {
                        GenreList.Remove(existed);
                    }

                    GenreList.Add(genreXml);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "加载分类数据 {SubDir} 失败", genreDir);
                    SentrySdk.CaptureException(ex);
                    StartupErrorsList.Add(string.Format(Locale.LoadGenreDataFailed, genreDir, ex.Message));
                }
            }
        }

        _logger.LogInformation("扫描流派列表，共找到 {0} 个流派。", GenreList.Count);
    }

    public void ScanVersionList()
    {
        VersionList.Clear();
        foreach (var a in AssetsDirs)
        {
            // 大小写不敏感解析 musicVersion 目录；枚举全部子目录后用大小写不敏感前缀过滤（不用 glob）。
            var versionParent = ResolveSubDir(Path.Combine(StreamingAssets, a), "musicVersion");
            if (versionParent is null) continue;
            foreach (var versionDir in Directory.EnumerateDirectories(versionParent))
            {
                var dirName = Path.GetFileName(versionDir);
                if (!dirName.StartsWith("musicversion", StringComparison.InvariantCultureIgnoreCase)) continue;
                if (!File.Exists(Path.Combine(versionDir, "MusicVersion.xml"))) continue;
                try
                {
                    var id = int.Parse(dirName.Substring("musicversion".Length));
                    var versionXml = new VersionXml(id, a, GamePath);

                    var existed = VersionList.Find(it => it.Id == id);
                    if (existed != null)
                    {
                        VersionList.Remove(existed);
                    }

                    VersionList.Add(versionXml);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "加载版本数据 {SubDir} 失败", versionDir);
                    SentrySdk.CaptureException(ex);
                    StartupErrorsList.Add(string.Format(Locale.LoadVersionDataFailed, versionDir, ex.Message));
                }
            }
        }

        _logger.LogInformation("扫描版本列表，共找到 {VersionListCount} 个版本。", VersionList.Count);
    }

    public void ScanAssetBundles()
    {
        AssetBundleJacketMap.Clear();
        PseudoAssetBundleJacketMap.Clear();
        foreach (var a in AssetsDirs)
        {
            // 大小写不敏感解析 AssetBundleImages/jacket 两级目录（兼容 Linux）。
            var abImagesDir = ResolveSubDir(Path.Combine(StreamingAssets, a), "AssetBundleImages");
            var jacketDir = abImagesDir is null ? null : ResolveSubDir(abImagesDir, "jacket");
            if (jacketDir is null) continue;
            foreach (var jacketFile in Directory.EnumerateFiles(jacketDir))
            {
                if (!Path.GetFileName(jacketFile).StartsWith("ui_jacket_", StringComparison.InvariantCultureIgnoreCase)) continue;
                var idStr = Path.GetFileName(jacketFile).Substring("ui_jacket_".Length, 6);
                if (!int.TryParse(idStr, out var id)) continue;
                if (Path.GetExtension(jacketFile).ToLowerInvariant() == ".ab")
                    AssetBundleJacketMap[id] = jacketFile;
                else if (((string[])[".png", ".jpg", ".jpeg"]).Contains(Path.GetExtension(jacketFile).ToLowerInvariant()))
                    PseudoAssetBundleJacketMap[id] = jacketFile;
            }
        }

        _logger.LogInformation($"扫描 AssetBundle，共找到 {AssetBundleJacketMap.Count} 个 AssetBundle。");
    }

    public void ScanSoundData()
    {
        AcbAwb.Clear();
        foreach (var a in AssetsDirs)
        {
            var soundDir = ResolveSubDir(Path.Combine(StreamingAssets, a), "SoundData");
            if (soundDir is null) continue;
            foreach (var sound in Directory.EnumerateFiles(soundDir))
            {
                AcbAwb[Path.GetFileName(sound).ToLower()] = sound;
            }
        }

        _logger.LogInformation($"扫描 SoundData，共找到 {AcbAwb.Count} 个音频文件。");
    }

    public void ScanMovieData()
    {
        MovieDataMap.Clear();
        foreach (var a in AssetsDirs)
        {
            var movieDir = ResolveSubDir(Path.Combine(StreamingAssets, a), "MovieData");
            if (movieDir is null) continue;
            foreach (var dat in Directory.EnumerateFiles(movieDir))
            {
                if (!int.TryParse(Path.GetFileNameWithoutExtension(dat), out var id)) continue;
                MovieDataMap[id] = dat;
            }
        }

        _logger.LogInformation($"扫描 MovieData，共找到 {MovieDataMap.Count} 个视频文件。");
    }

    public void GetGameVersion()
    {
        try
        {
            var xmlDoc = new XmlDocument();
            xmlDoc.Load(Path.Combine(StreamingAssets, @"A000/DataConfig.xml"));
            if (!int.TryParse(xmlDoc.SelectSingleNode("/DataConfig/version/minor")?.InnerText, out gameVersion))
            {
                _logger.LogWarning("{message}", Locale.GameVersionNotFound);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, @"无法获取游戏版本号，可能是因为 A000\DataConfig.xml 找不到或者有错误");
            SentrySdk.CaptureException(e);
            _logger.LogWarning(e, "{message}", Locale.GameVersionError);
        }
    }

    public string GetFreeAssetDir()
    {
        var id = 0;
        // 找到下一个未被使用的名称
        foreach (var dir in AssetsDirs)
        {
            var strId = ADirRegex().Match(dir).Groups[1].Value;
            var num = int.Parse(strId);
            if (num > id) id = num;
        }

        id++;
        if (id > 999)
        {
            id = 999;
            while (AssetsDirs.Contains($"A{id:000}"))
            {
                id--;
            }
        }

        return $"A{id:000}";
    }

    public static void UpdateAssetPathsFromAquaMaiConfig(IConfig config)
    {
        var imageAssetsDir = config.GetEntryState("GameSystem.Assets.LoadLocalImages.ImageAssetsDir");
        // 旧版兼容
        var localAssetsDir = config.GetEntryState("GameSystem.Assets.LoadLocalImages.LocalAssetsDir");
        var movieAssetsDir = config.GetEntryState("GameSystem.Assets.MovieLoader.MovieAssetsDir");
        var skinAssetsDir = config.GetEntryState("Fancy.CustomSkins.SkinsDir");

        if (imageAssetsDir != null)
        {
            _imageAssetsDir = imageAssetsDir.Value.ToString();
        }
        else if (localAssetsDir != null)
        {
            _imageAssetsDir = localAssetsDir.Value.ToString();
        }

        if (movieAssetsDir != null)
        {
            _movieAssetsDir = movieAssetsDir.Value.ToString();
        }

        if (skinAssetsDir != null)
        {
            _skinAssetsDir = skinAssetsDir.Value.ToString();
        }

        Directory.CreateDirectory(ImageAssetsDir);
        Directory.CreateDirectory(MovieAssetsDir);
    }
}