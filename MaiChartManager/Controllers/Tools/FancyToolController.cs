using MaiChartManager.Platform;
using MaiChartManager.Utils;
using Microsoft.AspNetCore.Mvc;
using Tomlyn;
using Tomlyn.Model;

namespace MaiChartManager.Controllers.Tools;

/// <summary>
/// 配合 AquaMai 的两个花活功能的编辑工具：
/// <list type="bullet">
/// <item>开场视频 CustomIntroCinematic：LocalAssets/IntroMovies/Enter_&lt;id&gt;.mp4（或 Enter_&lt;id&gt;_L/_R.mp4 左右分开）</item>
/// <item>曲目伪装 TrackCamouflage：LocalAssets/Camouflages/&lt;id&gt;.toml + &lt;id&gt;_jacket.jpg/png</item>
/// </list>
/// </summary>
[ApiController]
[Route("MaiChartManagerServlet/[action]Api")]
public class FancyToolController(ILogger<FancyToolController> logger) : ControllerBase
{
    private static readonly TomlModelOptions TomlOptions = new() { ConvertPropertyName = name => name };

    private static string IntroMovieDir => Path.Combine(StaticSettings.GamePath, "LocalAssets", "IntroMovies");
    private static string CamouflageDir => Path.Combine(StaticSettings.GamePath, "LocalAssets", "Camouflages");

    public record FancyToolResult(bool Success, string? Error);

    public record IntroMovieInfo(bool HasSingle, bool HasLeft, bool HasRight, bool HasAudio,
        string? SinglePath, string? LeftPath, string? RightPath);

    public record CamouflageInfoDto(string? Name, string? Artist, string? NoteDesigner, bool HideNoteDesigner,
        double UnlockScore, bool HasJacket, string? JacketPath);

    public class CamouflageModel
    {
        public string Name { get; set; } = "";
        public string Artist { get; set; } = "";
        public string? NoteDesigner { get; set; }
        public bool HideNoteDesigner { get; set; }
        public double UnlockScore { get; set; }
    }

    #region 开场视频

    [HttpGet]
    public IntroMovieInfo GetIntroMovie(int id)
    {
        var singlePath = ExistingPath(VideoFileName(id, null));
        var leftPath = ExistingPath(VideoFileName(id, "L"));
        var rightPath = ExistingPath(VideoFileName(id, "R"));
        return new IntroMovieInfo(singlePath is not null, leftPath is not null, rightPath is not null,
            System.IO.File.Exists(Path.Combine(IntroMovieDir, $"Enter_{id}.acb"))
            && System.IO.File.Exists(Path.Combine(IntroMovieDir, $"Enter_{id}.awb")),
            singlePath, leftPath, rightPath);
    }

    /// <summary>
    /// 取开场视频文件供前端预览。未指定 side 时依次取单文件 / 左 / 右。
    /// </summary>
    [HttpGet]
    public ActionResult GetIntroMovieFile(int id, string? side)
    {
        var path = side?.ToUpperInvariant() switch
        {
            "L" => ExistingPath(VideoFileName(id, "L")),
            "R" => ExistingPath(VideoFileName(id, "R")),
            _ => ExistingPath(VideoFileName(id, null))
                 ?? ExistingPath(VideoFileName(id, "L"))
                 ?? ExistingPath(VideoFileName(id, "R")),
        };

        if (path is null) return NotFound();
        // enableRangeProcessing：<video> 拖动进度条需要 Range 请求
        // 注意必须用 PhysicalFile：ControllerBase.File(string) 会把参数当成 wwwroot 下的虚拟路径，绝对路径会直接 500
        return PhysicalFile(path, "video/mp4", enableRangeProcessing: true);
    }

    /// <summary>
    /// 上传并转换开场视频。side 为空表示左右屏播放同一个视频，L / R 表示只替换其中一侧。
    /// </summary>
    [HttpPost]
    [DisableRequestSizeLimit]
    public async Task<FancyToolResult> SetIntroMovie([FromForm] int id, [FromForm] string? side, IFormFile file)
    {
        if (id <= 0) return new FancyToolResult(false, "invalid-id");
        var gamePathError = EnsureGamePath();
        if (gamePathError is not null) return new FancyToolResult(false, gamePathError);
#if WINDOWS
        if (IapManager.License != IapManager.LicenseStatus.Active)
            return new FancyToolResult(false, "license-required");
#endif

        var normalizedSide = string.IsNullOrWhiteSpace(side) ? null : side.ToUpperInvariant();
        if (normalizedSide is not (null or "L" or "R")) return new FancyToolResult(false, "invalid-side");

        var tmpDir = Directory.CreateTempSubdirectory();
        try
        {
            var inputPath = Path.Combine(tmpDir.FullName, "input" + Path.GetExtension(file.FileName));
            await using (var stream = System.IO.File.Create(inputPath))
            {
                await file.CopyToAsync(stream);
            }

            Directory.CreateDirectory(IntroMovieDir);
            var targetPath = Path.Combine(IntroMovieDir, VideoFileName(id, normalizedSide));
            var tmpVideoPath = Path.Combine(tmpDir.FullName, "output.mp4");

            // 统一转成 H.264 MP4：游戏里的 VideoPlayer 只保证能播这种编码
            await VideoConvert.ConvertVideo(new VideoConvert.VideoConvertOptions
            {
                InputPath = inputPath,
                OutputPath = tmpVideoPath,
                UseH264 = true,
                NoScale = true, // 保持原分辨率，游戏里按比例缩放
                Padding = 0,
                ContentType = file.ContentType,
                TaskbarProgress = false,
                // 保留一份音轨，前端预览才听得到声音；游戏里 AquaMai 会把视频音轨静音，声音走 acb/awb
                KeepAudio = true,
                // 硬件编码器默认画质差别很大（h264_mf 默认很糊），这里按选中的编码器给画质参数
                ExtraArgs = VideoConvert.H264QualityArgs(VideoConvert.H264Encoder),
            });

            if (!System.IO.File.Exists(tmpVideoPath)) return new FancyToolResult(false, "convert-failed");

            // mp4 只保留画面（VideoConvert 用 -map 0:0 丢掉了音轨）；音轨另外提取成 Enter_<id>.acb/.awb，
            // 游戏里由 AquaMai 单独播放，这样视频静音、声音走 acb/awb。
            await CreateIntroAudio(inputPath, id, tmpDir.FullName);

            MoveFileWithRetry(tmpVideoPath, targetPath);

            if (normalizedSide is null)
            {
                // 单文件与左右分开的写法不能共存
                DeleteFilesWithRetry(
                    Path.Combine(IntroMovieDir, VideoFileName(id, "L")),
                    Path.Combine(IntroMovieDir, VideoFileName(id, "R")));
            }
            else
            {
                DeleteFilesWithRetry(Path.Combine(IntroMovieDir, VideoFileName(id, null)));
                // 只替换一侧时，另一侧还没有的话先复制一份，保证左右都在
                var otherSide = normalizedSide == "L" ? "R" : "L";
                var otherPath = Path.Combine(IntroMovieDir, VideoFileName(id, otherSide));
                if (!System.IO.File.Exists(otherPath)) PlatformFile.CopyFile(targetPath, otherPath);
            }

            return new FancyToolResult(true, null);
        }
        catch (Exception e)
        {
            logger.LogError(e, "转换开场视频失败: {id}", id);
            return new FancyToolResult(false, "convert-failed");
        }
        finally
        {
            try
            {
                tmpDir.Delete(true);
            }
            catch
            {
                // 忽略清理失败
            }
        }
    }

    /// <summary>
    /// 从上传的文件里提取音轨，生成 Enter_&lt;id&gt;.acb / .awb。
    /// 开场 mp4 本身是静音的，游戏里由 acb/awb 出声（AquaMai 的 CustomIntroCinematic 会调 SoundManager 播放）。
    /// 没有音轨或转换失败时清掉旧的 acb/awb，避免出现「新视频配旧音频」。
    /// </summary>
    /// <summary>
    /// 替换 / 新增开场音频。左右屏共用一套音频（Enter_&lt;id&gt;.acb / .awb），
    /// 任意 ffmpeg 能解码的音频都会先解成 wav，再封装成 acb/awb。
    /// </summary>
    [HttpPost]
    [DisableRequestSizeLimit]
    public async Task<FancyToolResult> SetIntroAudio([FromForm] int id, IFormFile file)
    {
        if (id <= 0) return new FancyToolResult(false, "invalid-id");
        var gamePathError = EnsureGamePath();
        if (gamePathError is not null) return new FancyToolResult(false, gamePathError);
#if WINDOWS
        if (IapManager.License != IapManager.LicenseStatus.Active)
            return new FancyToolResult(false, "license-required");
#endif
        if (file is null || file.Length <= 0) return new FancyToolResult(false, "invalid-audio");

        var tmpDir = Directory.CreateTempSubdirectory();
        try
        {
            Directory.CreateDirectory(IntroMovieDir);
            var inputPath = Path.Combine(tmpDir.FullName, "input" + Path.GetExtension(file.FileName));
            await using (var stream = System.IO.File.Create(inputPath))
            {
                await file.CopyToAsync(stream);
            }

            // CreateIntroAudio 内部已做「先转临时文件再搬过去」，失败时返回 false
            if (!await CreateIntroAudio(inputPath, id, tmpDir.FullName, deleteOnFailure: false))
                return new FancyToolResult(false, "invalid-audio");

            return new FancyToolResult(true, null);
        }
        catch (Exception e)
        {
            logger.LogError(e, "写入开场音频失败: {id}", id);
            return new FancyToolResult(false, "write-failed");
        }
        finally
        {
            try
            {
                tmpDir.Delete(true);
            }
            catch
            {
                // 忽略清理失败
            }
        }
    }

    /// <summary>
    /// 只删掉开场音频，保留视频（左右屏共用的那一套 acb/awb）。
    /// </summary>
    [HttpPost]
    public FancyToolResult DeleteIntroAudio(int id)
    {
        var ok = DeleteFilesWithRetry(
            Path.Combine(IntroMovieDir, $"Enter_{id}.acb"),
            Path.Combine(IntroMovieDir, $"Enter_{id}.awb"));
        return new FancyToolResult(ok, ok ? null : "delete-failed");
    }

    private async Task<bool> CreateIntroAudio(string inputPath, int id, string tmpDir, bool deleteOnFailure = true)
    {
        var acbPath = Path.Combine(IntroMovieDir, $"Enter_{id}.acb");
        var awbPath = Path.Combine(IntroMovieDir, $"Enter_{id}.awb");
        var wavPath = Path.Combine(tmpDir, "audio.wav");
        // 先在临时目录里生成，两个文件都成功后再搬过去
        var tmpAcbPath = Path.Combine(tmpDir, $"Enter_{id}.acb");
        var tmpAwbPath = Path.Combine(tmpDir, $"Enter_{id}.awb");

        try
        {
            Audio.ExtractAudioFromMp4(inputPath, wavPath);
            if (!System.IO.File.Exists(wavPath) || new FileInfo(wavPath).Length <= 44)
                throw new InvalidOperationException("no audio track in source file");

            // HCA 编码比较慢，别占着请求线程
            await Task.Run(() => Audio.ConvertToMai(wavPath, tmpAcbPath));

            if (!System.IO.File.Exists(tmpAcbPath) || !System.IO.File.Exists(tmpAwbPath))
                throw new InvalidOperationException("acb/awb not generated");

            MoveFileWithRetry(tmpAcbPath, acbPath);
            MoveFileWithRetry(tmpAwbPath, awbPath);
            return true;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "开场视频音频生成失败，按静音处理: {id}", id);
            // 换视频时音画必须配套，所以没音轨就清掉旧的；用户单独传音频失败则保留原来的
            if (deleteOnFailure)
            {
                DeleteFilesWithRetry(acbPath, awbPath);
            }
            return false;
        }
    }

    [HttpPost]
    public FancyToolResult DeleteIntroMovie(int id)
    {
        var ok = DeleteFilesWithRetry(
            Path.Combine(IntroMovieDir, VideoFileName(id, null)),
            Path.Combine(IntroMovieDir, VideoFileName(id, "L")),
            Path.Combine(IntroMovieDir, VideoFileName(id, "R")),
            Path.Combine(IntroMovieDir, $"Enter_{id}.acb"),
            Path.Combine(IntroMovieDir, $"Enter_{id}.awb"));
        return new FancyToolResult(ok, ok ? null : "delete-failed");
    }

    /// <summary>
    /// 删除文件（Windows 走回收站）。被占用时（例如前端还在预览这个视频、句柄没释放）重试几次，
    /// 全部失败才返回 false，让前端提示「文件被占用」而不是静默失败。
    /// </summary>
    /// <summary>
    /// 覆盖式移动文件：目标被占用（例如前端还在预览旧文件）时重试几次。
    /// </summary>
    private static void MoveFileWithRetry(string source, string dest)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                PlatformFile.MoveFile(source, dest);
                return;
            }
            catch (Exception) when (attempt < 10)
            {
                Thread.Sleep(150);
            }
        }
    }

    private static bool DeleteFilesWithRetry(params string[] paths)
    {
        // 先等占用消失：这一步很便宜（只是尝试以独占方式打开文件），避免回收站删除在被占用时卡很久
        var deadline = DateTime.UtcNow.AddSeconds(3);
        var pending = paths.Where(System.IO.File.Exists).ToList();
        while (pending.Count > 0 && DateTime.UtcNow < deadline && pending.Any(IsFileLocked))
        {
            Thread.Sleep(100);
        }

        var ok = true;
        foreach (var path in pending)
        {
            if (!System.IO.File.Exists(path)) continue;
            if (!TryDeleteFile(path)) ok = false;
        }

        return ok;
    }

    /// <summary>
    /// 文件是否还被别人占着（能独占打开就说明没被占用）。
    /// </summary>
    private static bool IsFileLocked(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool TryDeleteFile(string path)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (!System.IO.File.Exists(path)) return true;
            if (IsFileLocked(path))
            {
                Thread.Sleep(100);
                continue;
            }

            try
            {
                PlatformFile.DeleteFile(path);
                if (!System.IO.File.Exists(path)) return true;
            }
            catch (Exception)
            {
                // 交给下面的兜底
            }

            Thread.Sleep(100);
        }

        // 兜底：回收站删除失败时直接删，至少别让用户卡在「删不掉」
        try
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            return !System.IO.File.Exists(path);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string VideoFileName(int id, string? side) => side is null ? $"Enter_{id}.mp4" : $"Enter_{id}_{side}.mp4";

    private static string? ExistingPath(string fileName)
    {
        var path = Path.Combine(IntroMovieDir, fileName);
        return System.IO.File.Exists(path) ? path : null;
    }

    public record IntroMovieEntry(int Id, bool HasSingle, bool HasLeft, bool HasRight, bool HasAudio);

    /// <summary>
    /// 列出 IntroMovies 目录里已有的开场视频。
    /// </summary>
    [HttpGet]
    public IEnumerable<IntroMovieEntry> GetIntroMovieList()
    {
        if (!Directory.Exists(IntroMovieDir)) return [];

        var ids = new HashSet<int>();
        foreach (var file in Directory.EnumerateFiles(IntroMovieDir, "*.mp4"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!name.StartsWith("Enter_", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = name[6..].Split('_');
            if (parts.Length is < 1 or > 2) continue;
            if (int.TryParse(parts[0], out var id) && id > 0) ids.Add(id);
        }

        return ids.OrderBy(id => id).Select(id => new IntroMovieEntry(
            id,
            System.IO.File.Exists(Path.Combine(IntroMovieDir, VideoFileName(id, null))),
            System.IO.File.Exists(Path.Combine(IntroMovieDir, VideoFileName(id, "L"))),
            System.IO.File.Exists(Path.Combine(IntroMovieDir, VideoFileName(id, "R"))),
            System.IO.File.Exists(Path.Combine(IntroMovieDir, $"Enter_{id}.acb"))
            && System.IO.File.Exists(Path.Combine(IntroMovieDir, $"Enter_{id}.awb")))).ToList();
    }

    #endregion

    #region 曲目伪装

    [HttpGet]
    public CamouflageInfoDto GetCamouflage(int id)
    {
        var path = Path.Combine(CamouflageDir, $"{id}.toml");
        var model = new CamouflageModel();
        if (System.IO.File.Exists(path))
        {
            try
            {
                model = Toml.ToModel<CamouflageModel>(System.IO.File.ReadAllText(path), options: TomlOptions)
                        ?? new CamouflageModel();
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "解析曲目伪装配置失败: {path}", path);
            }
        }

        var jacketPath = FindJacket(id);
        return new CamouflageInfoDto(model.Name, model.Artist, model.NoteDesigner, model.HideNoteDesigner,
            model.UnlockScore, jacketPath is not null, jacketPath);
    }

    /// <summary>
    /// 取伪装封面供前端预览。
    /// </summary>
    [HttpGet]
    public ActionResult GetCamouflageJacket(int id)
    {
        var path = FindJacket(id);
        if (path is null) return NotFound();

        var contentType = Path.GetExtension(path).ToLowerInvariant() == ".png" ? "image/png" : "image/jpeg";
        return PhysicalFile(path, contentType, enableRangeProcessing: true);
    }

    [HttpPost]
    public FancyToolResult SetCamouflage([FromForm] int id, [FromForm] string? name, [FromForm] string? artist,
        [FromForm] string? noteDesigner, [FromForm] bool hideNoteDesigner, [FromForm] double unlockScore)
    {
        if (id <= 0) return new FancyToolResult(false, "invalid-id");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(artist))
            return new FancyToolResult(false, "invalid-text");

        var gamePathError = EnsureGamePath();
        if (gamePathError is not null) return new FancyToolResult(false, gamePathError);

        var model = new CamouflageModel
        {
            Name = name.Trim(),
            Artist = artist.Trim(),
            NoteDesigner = noteDesigner?.Trim() ?? "",
            HideNoteDesigner = hideNoteDesigner,
            UnlockScore = unlockScore,
        };

        try
        {
            Directory.CreateDirectory(CamouflageDir);
            WriteAllTextAtomic(Path.Combine(CamouflageDir, $"{id}.toml"),
                Toml.FromModel(model, TomlOptions));
            return new FancyToolResult(true, null);
        }
        catch (Exception e)
        {
            logger.LogError(e, "写入曲目伪装配置失败: {id}", id);
            return new FancyToolResult(false, "write-failed");
        }
    }

    [HttpPost]
    [DisableRequestSizeLimit]
    public async Task<FancyToolResult> SetCamouflageJacket([FromForm] int id, IFormFile file)
    {
        if (id <= 0) return new FancyToolResult(false, "invalid-id");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png")) return new FancyToolResult(false, "invalid-image");

        var gamePathError = EnsureGamePath();
        if (gamePathError is not null) return new FancyToolResult(false, gamePathError);

        try
        {
            Directory.CreateDirectory(CamouflageDir);
            var targetPath = Path.Combine(CamouflageDir, $"{id}_jacket{ext}");
            // 同一首曲子只保留一张封面
            foreach (var otherExt in new[] { ".jpg", ".jpeg", ".png" })
            {
                var otherPath = Path.Combine(CamouflageDir, $"{id}_jacket{otherExt}");
                if (!string.Equals(otherPath, targetPath, StringComparison.OrdinalIgnoreCase))
                    DeleteFilesWithRetry(otherPath);
            }

            await using var stream = System.IO.File.Create(targetPath);
            await file.CopyToAsync(stream);
            return new FancyToolResult(true, null);
        }
        catch (Exception e)
        {
            logger.LogError(e, "写入曲目伪装封面失败: {id}", id);
            return new FancyToolResult(false, "write-failed");
        }
    }

    [HttpPost]
    public FancyToolResult DeleteCamouflage(int id)
    {
        var ok = DeleteFilesWithRetry(
            [
                Path.Combine(CamouflageDir, $"{id}.toml"),
                .. new[] { ".jpg", ".jpeg", ".png" }.Select(ext => Path.Combine(CamouflageDir, $"{id}_jacket{ext}")),
            ]);
        return new FancyToolResult(ok, ok ? null : "delete-failed");
    }

    private static string? FindJacket(int id)
    {
        foreach (var ext in new[] { ".jpg", ".jpeg", ".png" })
        {
            var path = Path.Combine(CamouflageDir, $"{id}_jacket{ext}");
            if (System.IO.File.Exists(path)) return path;
        }

        return null;
    }

    public record CamouflageEntry(int Id, string? Name, string? Artist, bool HasJacket);

    /// <summary>
    /// 列出 Camouflages 目录里已有的曲目伪装。
    /// </summary>
    [HttpGet]
    public IEnumerable<CamouflageEntry> GetCamouflageList()
    {
        if (!Directory.Exists(CamouflageDir)) return [];

        var result = new List<CamouflageEntry>();
        foreach (var file in Directory.EnumerateFiles(CamouflageDir, "*.toml"))
        {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out var id) || id <= 0) continue;

            var model = new CamouflageModel();
            try
            {
                model = Toml.ToModel<CamouflageModel>(System.IO.File.ReadAllText(file), options: TomlOptions)
                        ?? new CamouflageModel();
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "解析曲目伪装配置失败: {path}", file);
            }

            result.Add(new CamouflageEntry(id, model.Name, model.Artist, FindJacket(id) is not null));
        }

        return result.OrderBy(entry => entry.Id).ToList();
    }

    private static void WriteAllTextAtomic(string path, string content)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            System.IO.File.WriteAllText(tempPath, content);
            System.IO.File.Move(tempPath, path, true);
        }
        finally
        {
            if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath);
        }
    }

    #endregion

    private static string? EnsureGamePath()
        => string.IsNullOrWhiteSpace(StaticSettings.GamePath) || !Directory.Exists(StaticSettings.GamePath)
            ? "game-path-missing"
            : null;
}
