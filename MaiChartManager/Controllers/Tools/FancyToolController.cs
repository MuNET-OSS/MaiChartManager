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

    /// <summary>
    /// 同一个曲目的文件操作串行化：避免两个请求交错提交，留下音画 / 伪装不匹配的文件。
    /// 用固定数量的「分段锁」而不是按 id 建字典，内存恒定（不同 id 偶尔落到同一段，可以接受）。
    /// </summary>
    private static readonly SemaphoreSlim[] FileGates =
        Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private static SemaphoreSlim GetFileGate(int id) => FileGates[(uint)id % (uint)FileGates.Length];

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
    /// 开场音频预览：把 Enter_&lt;id&gt;.acb/.awb 解成 wav 给前端播放（浏览器放不了 acb）。
    /// 解码结果按 acb 的修改时间缓存在临时目录，避免每次 Range 请求都重新解码。
    /// </summary>
    [HttpGet]
    public ActionResult GetIntroAudioFile(int id)
    {
        var acbPath = Path.Combine(IntroMovieDir, $"Enter_{id}.acb");
        var awbPath = Path.Combine(IntroMovieDir, $"Enter_{id}.awb");
        if (!System.IO.File.Exists(acbPath) || !System.IO.File.Exists(awbPath)) return NotFound();

        var wavPath = GetCachedIntroAudio(acbPath, id);
        if (wavPath is null) return NotFound();

        return PhysicalFile(wavPath, "audio/wav", enableRangeProcessing: true);
    }

    private string? GetCachedIntroAudio(string acbPath, int id)
    {
        try
        {
            var cacheDir = Path.Combine(StaticSettings.tempPath, "IntroAudioPreview");
            Directory.CreateDirectory(cacheDir);
            var stamp = System.IO.File.GetLastWriteTimeUtc(acbPath).Ticks;
            var wavPath = Path.Combine(cacheDir, $"Enter_{id}_{stamp}.wav");
            if (System.IO.File.Exists(wavPath)) return wavPath;

            // acb 换了就清掉同一首曲子旧的缓存
            foreach (var old in Directory.EnumerateFiles(cacheDir, $"Enter_{id}_*.wav").ToList())
            {
                try
                {
                    System.IO.File.Delete(old);
                }
                catch
                {
                    // 忽略清理失败
                }
            }

            var wavBytes = Audio.AcbToWav(acbPath);
            System.IO.File.WriteAllBytes(wavPath, wavBytes);
            return wavPath;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "开场音频预览解码失败: {id}", id);
            return null;
        }
    }

    /// <summary>
    /// 上传并转换开场视频。side 为空表示左右屏播放同一个视频，L / R 表示只替换其中一侧。
    /// 视频、音频、旧布局清理作为整体提交：任一步失败都会回滚到操作前的状态。
    /// </summary>
    [HttpPost]
    [DisableRequestSizeLimit]
    public async Task<FancyToolResult> SetIntroMovie([FromForm] int id, [FromForm] string? side, IFormFile file)
    {
        if (id <= 0) return new FancyToolResult(false, "invalid-id");

        var gate = GetFileGate(id);
        await gate.WaitAsync();
        try
        {
            return await SetIntroMovieCore(id, side, file);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<FancyToolResult> SetIntroMovieCore(int id, string? side, IFormFile file)
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
        var tx = new IntroFileTransaction(Path.Combine(tmpDir.FullName, "backup"));
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
                // 硬件编码器默认画质差别很大（h264_mf 默认很糊），这里按选中的编码器给画质参数
                ExtraArgs = VideoConvert.H264QualityArgs(VideoConvert.H264Encoder),
            });

            if (!System.IO.File.Exists(tmpVideoPath)) return new FancyToolResult(false, "convert-failed");

            // 音轨只生成到临时目录，等视频布局确定后和视频一起提交
            var audio = await BuildIntroAudio(inputPath, id, tmpDir.FullName);

            // 这次会动到的文件先全部备份，失败时整体回滚
            foreach (var path in IntroFilePaths(id)) tx.Track(path);

            // 清理与新布局冲突的旧视频（单文件与 L/R 不能共存）
            string[] conflictingFiles = normalizedSide is null
                ?
                [
                    Path.Combine(IntroMovieDir, VideoFileName(id, "L")),
                    Path.Combine(IntroMovieDir, VideoFileName(id, "R")),
                ]
                : [Path.Combine(IntroMovieDir, VideoFileName(id, null))];
            if (!DeleteFilesWithRetry(conflictingFiles))
            {
                tx.Rollback();
                return new FancyToolResult(false, "delete-failed");
            }

            MoveFileWithRetry(tmpVideoPath, targetPath);

            if (normalizedSide is not null)
            {
                // 只替换一侧时，另一侧还没有的话先复制一份，保证左右都在
                var otherSide = normalizedSide == "L" ? "R" : "L";
                var otherPath = Path.Combine(IntroMovieDir, VideoFileName(id, otherSide));
                if (!System.IO.File.Exists(otherPath)) PlatformFile.CopyFile(targetPath, otherPath);
            }

            // 音频必须和视频配套：这次没有音轨就把旧的 acb/awb 一起清掉
            var acbPath = Path.Combine(IntroMovieDir, $"Enter_{id}.acb");
            var awbPath = Path.Combine(IntroMovieDir, $"Enter_{id}.awb");
            if (audio is not null)
            {
                MoveFileWithRetry(audio.AcbPath, acbPath);
                MoveFileWithRetry(audio.AwbPath, awbPath);
            }
            else if (!DeleteFilesWithRetry(acbPath, awbPath))
            {
                tx.Rollback();
                return new FancyToolResult(false, "delete-failed");
            }

            return new FancyToolResult(true, null);
        }
        catch (Exception e)
        {
            logger.LogError(e, "转换开场视频失败: {id}", id);
            tx.Rollback();
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
    /// 替换 / 新增开场音频。左右屏共用一套音频（Enter_&lt;id&gt;.acb / .awb），
    /// 任意 ffmpeg 能解码的音频都会先解成 wav，再封装成 acb/awb。
    /// </summary>
    [HttpPost]
    [DisableRequestSizeLimit]
    public async Task<FancyToolResult> SetIntroAudio([FromForm] int id, IFormFile file)
    {
        if (id <= 0) return new FancyToolResult(false, "invalid-id");

        var gate = GetFileGate(id);
        await gate.WaitAsync();
        try
        {
            return await SetIntroAudioCore(id, file);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<FancyToolResult> SetIntroAudioCore(int id, IFormFile file)
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
        var tx = new IntroFileTransaction(Path.Combine(tmpDir.FullName, "backup"));
        try
        {
            Directory.CreateDirectory(IntroMovieDir);
            var inputPath = Path.Combine(tmpDir.FullName, "input" + Path.GetExtension(file.FileName));
            await using (var stream = System.IO.File.Create(inputPath))
            {
                await file.CopyToAsync(stream);
            }

            var audio = await BuildIntroAudio(inputPath, id, tmpDir.FullName);
            if (audio is null) return new FancyToolResult(false, "invalid-audio");

            var acbPath = Path.Combine(IntroMovieDir, $"Enter_{id}.acb");
            var awbPath = Path.Combine(IntroMovieDir, $"Enter_{id}.awb");
            tx.Track(acbPath);
            tx.Track(awbPath);

            MoveFileWithRetry(audio.AcbPath, acbPath);
            MoveFileWithRetry(audio.AwbPath, awbPath);

            return new FancyToolResult(true, null);
        }
        catch (Exception e)
        {
            logger.LogError(e, "写入开场音频失败: {id}", id);
            tx.Rollback();
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
    public async Task<FancyToolResult> DeleteIntroAudio(int id)
    {
        if (id <= 0) return new FancyToolResult(false, "invalid-id");

        var gate = GetFileGate(id);
        await gate.WaitAsync();
        try
        {
            return DeleteIntroAudioCore(id);
        }
        finally
        {
            gate.Release();
        }
    }

    private static FancyToolResult DeleteIntroAudioCore(int id)
    {
        var gamePathError = EnsureGamePath();
        if (gamePathError is not null) return new FancyToolResult(false, gamePathError);

        var ok = DeleteFilesWithRetry(
            Path.Combine(IntroMovieDir, $"Enter_{id}.acb"),
            Path.Combine(IntroMovieDir, $"Enter_{id}.awb"));
        return new FancyToolResult(ok, ok ? null : "delete-failed");
    }

    private sealed record IntroAudioFiles(string AcbPath, string AwbPath);

    /// <summary>
    /// 从上传的文件里提取音轨，生成到临时目录（acb/awb 成对）。
    /// 没有音轨或转换失败返回 null，由调用方决定是否清掉旧的 acb/awb。
    /// </summary>
    private async Task<IntroAudioFiles?> BuildIntroAudio(string inputPath, int id, string tmpDir)
    {
        var wavPath = Path.Combine(tmpDir, "audio.wav");
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

            return new IntroAudioFiles(tmpAcbPath, tmpAwbPath);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "开场音频生成失败，按静音处理: {id}", id);
            return null;
        }
    }

    private static string[] IntroFilePaths(int id) =>
    [
        Path.Combine(IntroMovieDir, VideoFileName(id, null)),
        Path.Combine(IntroMovieDir, VideoFileName(id, "L")),
        Path.Combine(IntroMovieDir, VideoFileName(id, "R")),
        Path.Combine(IntroMovieDir, $"Enter_{id}.acb"),
        Path.Combine(IntroMovieDir, $"Enter_{id}.awb"),
    ];

    /// <summary>
    /// 开场文件事务：先把这次会改动/删除的文件备份到临时目录，
    /// 失败时整体回滚（原本有的还原，原本没有的删掉），避免留下半套文件。
    /// </summary>
    private sealed class IntroFileTransaction(string backupDir)
    {
        private readonly List<(string Path, string BackupPath, bool Existed)> _entries = [];
        private bool _rolledBack;

        public void Track(string path)
        {
            if (_entries.Any(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase))) return;

            var existed = System.IO.File.Exists(path);
            var backupPath = Path.Combine(backupDir, Guid.NewGuid().ToString("N"));
            if (existed)
            {
                Directory.CreateDirectory(backupDir);
                System.IO.File.Copy(path, backupPath, true);
            }

            _entries.Add((path, backupPath, existed));
        }

        public void Rollback()
        {
            if (_rolledBack) return;
            _rolledBack = true;

            foreach (var (path, backupPath, existed) in _entries)
            {
                try
                {
                    if (existed)
                    {
                        if (System.IO.File.Exists(backupPath)) System.IO.File.Copy(backupPath, path, true);
                    }
                    else if (System.IO.File.Exists(path))
                    {
                        System.IO.File.Delete(path);
                    }
                }
                catch (Exception)
                {
                    // 尽力回滚，单个文件失败不影响其它文件
                }
            }
        }
    }
    [HttpPost]
    public async Task<FancyToolResult> DeleteIntroMovie(int id)
    {
        if (id <= 0) return new FancyToolResult(false, "invalid-id");

        var gate = GetFileGate(id);
        await gate.WaitAsync();
        try
        {
            return DeleteIntroMovieCore(id);
        }
        finally
        {
            gate.Release();
        }
    }

    private static FancyToolResult DeleteIntroMovieCore(int id)
    {
        var gamePathError = EnsureGamePath();
        if (gamePathError is not null) return new FancyToolResult(false, gamePathError);

        var ok = DeleteFilesWithRetry(
            Path.Combine(IntroMovieDir, VideoFileName(id, null)),
            Path.Combine(IntroMovieDir, VideoFileName(id, "L")),
            Path.Combine(IntroMovieDir, VideoFileName(id, "R")),
            Path.Combine(IntroMovieDir, $"Enter_{id}.acb"),
            Path.Combine(IntroMovieDir, $"Enter_{id}.awb"));
        return new FancyToolResult(ok, ok ? null : "delete-failed");
    }

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

    /// <summary>
    /// 删除文件（Windows 走回收站）。被占用时（例如前端还在预览这个视频、句柄没释放）重试几次，
    /// 全部失败才返回 false，让前端提示「文件被占用」而不是静默失败。
    /// </summary>
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
    public async Task<FancyToolResult> SetCamouflage([FromForm] int id, [FromForm] string? name, [FromForm] string? artist,
        [FromForm] string? noteDesigner, [FromForm] bool hideNoteDesigner, [FromForm] double unlockScore)
    {
        if (id <= 0) return new FancyToolResult(false, "invalid-id");

        var gate = GetFileGate(id);
        await gate.WaitAsync();
        try
        {
            return SetCamouflageCore(id, name, artist, noteDesigner, hideNoteDesigner, unlockScore);
        }
        finally
        {
            gate.Release();
        }
    }

    private FancyToolResult SetCamouflageCore(int id, string? name, string? artist,
        string? noteDesigner, bool hideNoteDesigner, double unlockScore)
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

        var gate = GetFileGate(id);
        await gate.WaitAsync();
        try
        {
            return await SetCamouflageJacketCore(id, file);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<FancyToolResult> SetCamouflageJacketCore(int id, IFormFile file)
    {
        if (id <= 0) return new FancyToolResult(false, "invalid-id");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png")) return new FancyToolResult(false, "invalid-image");

        var gamePathError = EnsureGamePath();
        if (gamePathError is not null) return new FancyToolResult(false, gamePathError);

        var tmpPath = Path.Combine(CamouflageDir, $"{id}_jacket{ext}.tmp");
        try
        {
            Directory.CreateDirectory(CamouflageDir);
            var targetPath = Path.Combine(CamouflageDir, $"{id}_jacket{ext}");

            // 先写临时文件再覆盖目标，避免写到一半把已有封面弄坏
            await using (var stream = System.IO.File.Create(tmpPath))
            {
                await file.CopyToAsync(stream);
            }
            MoveFileWithRetry(tmpPath, targetPath);

            // 同一首曲子只保留一张封面；清理失败不影响新封面，只记日志
            foreach (var otherExt in new[] { ".jpg", ".jpeg", ".png" })
            {
                var otherPath = Path.Combine(CamouflageDir, $"{id}_jacket{otherExt}");
                if (!string.Equals(otherPath, targetPath, StringComparison.OrdinalIgnoreCase) &&
                    !DeleteFilesWithRetry(otherPath))
                    logger.LogWarning("旧封面清理失败: {path}", otherPath);
            }

            return new FancyToolResult(true, null);
        }
        catch (Exception e)
        {
            logger.LogError(e, "写入曲目伪装封面失败: {id}", id);
            return new FancyToolResult(false, "write-failed");
        }
        finally
        {
            try
            {
                if (System.IO.File.Exists(tmpPath)) System.IO.File.Delete(tmpPath);
            }
            catch
            {
                // 忽略清理失败
            }
        }
    }

    [HttpPost]
    public async Task<FancyToolResult> DeleteCamouflage(int id)
    {
        if (id <= 0) return new FancyToolResult(false, "invalid-id");

        var gate = GetFileGate(id);
        await gate.WaitAsync();
        try
        {
            return DeleteCamouflageCore(id);
        }
        finally
        {
            gate.Release();
        }
    }

    private static FancyToolResult DeleteCamouflageCore(int id)
    {
        var gamePathError = EnsureGamePath();
        if (gamePathError is not null) return new FancyToolResult(false, gamePathError);

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
