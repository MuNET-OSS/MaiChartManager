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

    public static string GamePath { get; set; }
    public static string StreamingAssets => Path.Combine(GamePath, "Sinmai_Data", "StreamingAssets");

    public static IEnumerable<string> AssetsDirs => Directory.EnumerateDirectories(StreamingAssets)
        .Select(Path.GetFileName).Where(it => ADirRegex().IsMatch(it));

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
            var musicDir = Path.Combine(StreamingAssets, a, "music");
            if (!Directory.Exists(musicDir)) continue;

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

        _logger.LogInformation("Scan music list, found {0} music.", _musicList.Count);
    }

    public void ScanGenre()
    {
        GenreList.Clear();

        foreach (var a in AssetsDirs)
        {
            if (!Directory.Exists(Path.Combine(StreamingAssets, a, "musicGenre"))) continue;
            foreach (var genreDir in Directory.EnumerateDirectories(Path.Combine(StreamingAssets, a, "musicGenre"), "musicgenre*"))
            {
                if (!File.Exists(Path.Combine(genreDir, "MusicGenre.xml"))) continue;
                if (!Path.GetFileName(genreDir).StartsWith("musicgenre", StringComparison.InvariantCultureIgnoreCase)) continue;
                try
                {
                    var id = int.Parse(Path.GetFileName(genreDir).Substring("musicgenre".Length));
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

        _logger.LogInformation("Scan genre list, found {0} genre.", GenreList.Count);
    }

    public void ScanVersionList()
    {
        VersionList.Clear();
        foreach (var a in AssetsDirs)
        {
            if (!Directory.Exists(Path.Combine(StreamingAssets, a, "musicVersion"))) continue;
            foreach (var versionDir in Directory.EnumerateDirectories(Path.Combine(StreamingAssets, a, "musicVersion"), "musicversion*"))
            {
                if (!File.Exists(Path.Combine(versionDir, "MusicVersion.xml"))) continue;
                if (!Path.GetFileName(versionDir).StartsWith("musicversion", StringComparison.InvariantCultureIgnoreCase)) continue;
                try
                {
                    var id = int.Parse(Path.GetFileName(versionDir).Substring("musicversion".Length));
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

        _logger.LogInformation("Scan version list, found {VersionListCount} version.", VersionList.Count);
    }

    public void ScanAssetBundles()
    {
        AssetBundleJacketMap.Clear();
        PseudoAssetBundleJacketMap.Clear();
        foreach (var a in AssetsDirs)
        {
            if (!Directory.Exists(Path.Combine(StreamingAssets, a, @"AssetBundleImages\jacket"))) continue;
            foreach (var jacketFile in Directory.EnumerateFiles(Path.Combine(StreamingAssets, a, @"AssetBundleImages\jacket")))
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

        _logger.LogInformation($"Scan AssetBundles, found {AssetBundleJacketMap.Count} AssetBundles.");
    }

    public void ScanSoundData()
    {
        AcbAwb.Clear();
        foreach (var a in AssetsDirs)
        {
            if (!Directory.Exists(Path.Combine(StreamingAssets, a, "SoundData"))) continue;
            foreach (var sound in Directory.EnumerateFiles(Path.Combine(StreamingAssets, a, @"SoundData")))
            {
                AcbAwb[Path.GetFileName(sound).ToLower()] = sound;
            }
        }

        _logger.LogInformation($"Scan SoundData, found {AcbAwb.Count} SoundData.");
    }

    public void ScanMovieData()
    {
        MovieDataMap.Clear();
        foreach (var a in AssetsDirs)
        {
            if (!Directory.Exists(Path.Combine(StreamingAssets, a, "MovieData"))) continue;
            foreach (var dat in Directory.EnumerateFiles(Path.Combine(StreamingAssets, a, @"MovieData")))
            {
                if (!int.TryParse(Path.GetFileNameWithoutExtension(dat), out var id)) continue;
                MovieDataMap[id] = dat;
            }
        }

        _logger.LogInformation($"Scan MovieData, found {MovieDataMap.Count} MovieData.");
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