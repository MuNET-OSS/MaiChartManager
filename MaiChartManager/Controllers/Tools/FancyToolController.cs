using System.Collections.Concurrent;
using System.Threading.Channels;
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
/// 授权校验放在前端打开工具窗口时做，这里不再逐个接口判断。
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

    /// <summary>
    /// 开场视频与曲目伪装的并集，供左侧列表一屏展示。
    /// 两个功能本来就是一套演出（视频 + 伪装 + 解锁门槛），分开列会让用户看不出哪首歌配了哪一半。
    /// </summary>
    public record FancyEntry(int Id, bool HasSingle, bool HasLeft, bool HasRight, bool HasAudio,
        bool HasCamouflage, string? CamouflageName, string? CamouflageArtist, bool HasJacket);

    public class CamouflageModel
    {
        public string Name { get; set; } = "";
        public string Artist { get; set; } = "";
        public string? NoteDesigner { get; set; }
        public bool HideNoteDesigner { get; set; }
        public double UnlockScore { get; set; }
    }

    #region 列表

    /// <summary>
    /// 列出所有做过开场视频 / 曲目伪装的曲目（并集）。
    /// </summary>
    [HttpGet]
    public IEnumerable<FancyEntry> GetFancyList()
    {
        var introMovies = ReadIntroMovieEntries().ToDictionary(entry => entry.Id);
        // 同一曲目可能有两个只会互相覆盖的文件名（1.toml / 01.toml），
        // 直接 ToDictionary 会抛异常让整个列表 500，这里取第一条就行
        var camouflages = ReadCamouflageEntries()
            .GroupBy(entry => entry.Id)
            .ToDictionary(group => group.Key, group => group.First());

        return introMovies.Keys.Union(camouflages.Keys)
            .OrderBy(id => id)
            .Select(id =>
            {
                introMovies.TryGetValue(id, out var movie);
                camouflages.TryGetValue(id, out var camouflage);
                return new FancyEntry(
                    id,
                    movie?.HasSingle ?? false,
                    movie?.HasLeft ?? false,
                    movie?.HasRight ?? false,
                    movie?.HasAudio ?? false,
                    camouflage is not null,
                    camouflage?.Name,
                    camouflage?.Artist,
                    camouflage?.HasJacket ?? false);
            })
            .ToList();
    }

    private sealed record IntroMovieRow(int Id, bool HasSingle, bool HasLeft, bool HasRight, bool HasAudio);

    private sealed record CamouflageRow(int Id, string? Name, string? Artist, bool HasJacket);

    /// <summary>
    /// 扫描 IntroMovies 目录。文件名规则与 AquaMai 侧一致：Enter_&lt;id&gt;.mp4 或 Enter_&lt;id&gt;_L/_R.mp4。
    /// </summary>
    private IEnumerable<IntroMovieRow> ReadIntroMovieEntries()
    {
        if (!Directory.Exists(IntroMovieDir)) return [];

        var ids = new HashSet<int>();
        foreach (var file in Directory.EnumerateFiles(IntroMovieDir, "*.mp4"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!name.StartsWith("Enter_", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = name[6..].Split('_');
            if (parts.Length is < 1 or > 2) continue;
            // 第三段只允许 L / R，其它（例如手工留下的 .tmp 中间产物）都不算数
            if (parts.Length == 2 && !parts[1].Equals("L", StringComparison.OrdinalIgnoreCase)
                                  && !parts[1].Equals("R", StringComparison.OrdinalIgnoreCase)) continue;
            if (int.TryParse(parts[0], out var id) && id > 0) ids.Add(id);
        }

        return ids.Select(id => new IntroMovieRow(
            id,
            System.IO.File.Exists(Path.Combine(IntroMovieDir, VideoFileName(id, null))),
            System.IO.File.Exists(Path.Combine(IntroMovieDir, VideoFileName(id, "L"))),
            System.IO.File.Exists(Path.Combine(IntroMovieDir, VideoFileName(id, "R"))),
            HasIntroAudio(id)));
    }

    private IEnumerable<CamouflageRow> ReadCamouflageEntries()
    {
        if (!Directory.Exists(CamouflageDir)) return [];

        var result = new List<CamouflageRow>();
        foreach (var file in Directory.EnumerateFiles(CamouflageDir, "*.toml"))
        {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out var id) || id <= 0) continue;

            CamouflageModel? model;
            try
            {
                model = Toml.ToModel<CamouflageModel>(System.IO.File.ReadAllText(file), options: TomlOptions);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "解析曲目伪装配置失败: {path}", file);
                model = null;
            }

            result.Add(new CamouflageRow(id, model?.Name, model?.Artist, FindJacket(id) is not null));
        }

        return result;
    }

    #endregion

    #region 开场视频

    [HttpGet]
    public IntroMovieInfo GetIntroMovie(int id)
    {
        var singlePath = ExistingPath(VideoFileName(id, null));
        var leftPath = ExistingPath(VideoFileName(id, "L"));
        var rightPath = ExistingPath(VideoFileName(id, "R"));
        return new IntroMovieInfo(singlePath is not null, leftPath is not null, rightPath is not null,
            HasIntroAudio(id), singlePath, leftPath, rightPath);
    }

    private static bool HasIntroAudio(int id)
        => System.IO.File.Exists(Path.Combine(IntroMovieDir, $"Enter_{id}.acb"))
           && System.IO.File.Exists(Path.Combine(IntroMovieDir, $"Enter_{id}.awb"));

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
        if (!HasIntroAudio(id)) return NotFound();

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
    /// 转码要几十秒到几分钟，所以走 SSE 回报进度（Progress / Success / Error，Error 的 data 是错误码）。
    /// </summary>
    [HttpPost]
    [DisableRequestSizeLimit]
    public async Task SetIntroMovie([FromForm] int id, [FromForm] string? side, IFormFile file)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");

        // 单写者队列：进度回调来自 ffmpeg 的读取线程，直接写 Response 会和请求线程上的收尾事件交错，
        // 帧的先后顺序也没有保证（HttpResponse 不是线程安全的）
        var frames = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        var writerTask = WriteSseFrames(frames.Reader);

        var gate = GetFileGate(id);
        await gate.WaitAsync();
        try
        {
            // 转码占 0-90%，剩下的留给出音频（HCA 编码没有进度回调）
            var result = await SetIntroMovieCore(id, side, file,
                percent => frames.Writer.TryWrite(SseFrame("Progress", (percent * 9 / 10).ToString())));
            await frames.Writer.WriteAsync(
                SseFrame(result.Success ? "Success" : "Error", result.Success ? "ok" : result.Error ?? "write-failed"));
        }
        catch (Exception e)
        {
            logger.LogError(e, "转换开场视频失败: {id}", id);
            await frames.Writer.WriteAsync(SseFrame("Error", "convert-failed"));
        }
        finally
        {
            gate.Release();
            frames.Writer.TryComplete();
        }

        await writerTask;
    }

    private static string SseFrame(string eventName, string data) => $"event: {eventName}\ndata: {data}\n\n";

    private async Task WriteSseFrames(ChannelReader<string> reader)
    {
        try
        {
            await foreach (var frame in reader.ReadAllAsync(HttpContext.RequestAborted))
            {
                await Response.WriteAsync(frame, HttpContext.RequestAborted);
                await Response.Body.FlushAsync(HttpContext.RequestAborted);
            }
        }
        catch (Exception)
        {
            // 客户端可能中途断开（关窗口），写失败不用管
        }
    }

    private async Task<FancyToolResult> SetIntroMovieCore(int id, string? side, IFormFile file, Action<int> onProgress)
    {
        if (id <= 0) return new FancyToolResult(false, "invalid-id");
        var gamePathError = EnsureGamePath();
        if (gamePathError is not null) return new FancyToolResult(false, gamePathError);

        var normalizedSide = string.IsNullOrWhiteSpace(side) ? null : side.ToUpperInvariant();
        if (normalizedSide is not (null or "L" or "R")) return new FancyToolResult(false, "invalid-side");

        // 上传的原始文件只给 ffmpeg 读，不参与 rename，放在系统临时目录就行
        var uploadDir = Directory.CreateTempSubdirectory();
        Directory.CreateDirectory(IntroMovieDir);
        using var tx = new FancyFileTransaction(IntroMovieDir, logger);
        try
        {
            var inputPath = Path.Combine(uploadDir.FullName, "input" + Path.GetExtension(file.FileName));
            await using (var stream = System.IO.File.Create(inputPath))
            {
                await file.CopyToAsync(stream);
            }

            // 转码输出直接写进事务的 stage 目录（在 IntroMovies 里面），
            // 最后只是一次同目录 rename，跨盘也不会退化成几百 MB 的整文件复制
            var stagedVideoPath = tx.StagePath($"Enter_{id}.mp4");

            // 统一转成 H.264 MP4：游戏里的 VideoPlayer 只保证能播这种编码
            await VideoConvert.ConvertVideo(new VideoConvert.VideoConvertOptions
            {
                InputPath = inputPath,
                OutputPath = stagedVideoPath,
                UseH264 = true,
                NoScale = true, // 保持原分辨率，游戏里按比例缩放
                Padding = 0,
                ContentType = file.ContentType,
                TaskbarProgress = false,
                // 硬件编码器的默认画质差别很大（h264_mf 默认很糊），开场视频要按高质量转
                Quality = VideoConvert.Quality.High,
                OnProgress = onProgress,
            });

            if (!System.IO.File.Exists(stagedVideoPath)) return new FancyToolResult(false, "convert-failed");

            var audio = await BuildIntroAudio(inputPath, id, tx.StageDirPath);

            // 从单文件切到分屏时，旧单文件一会儿要给另一侧用，先复制一份进 stage（同卷，很便宜）
            var previousSingle = normalizedSide is null ? null : SnapshotSingleMovie(id, tx);

            // 只备份真正会被覆盖的文件，失败时整体还原。
            // 整段替换要连左右分屏的残留一起清掉；只换一侧时另一侧必须原地不动，
            // 否则下面「对侧不存在就补一份」会把它当成缺失，把这次上传的视频复制过去，两侧就变成同一个了。
            var filesToBackup = normalizedSide is null
                ? VideoFilePaths(id)
                :
                [
                    Path.Combine(IntroMovieDir, VideoFileName(id, null)),
                    Path.Combine(IntroMovieDir, VideoFileName(id, normalizedSide)),
                ];
            foreach (var path in filesToBackup)
            {
                if (!tx.Backup(path)) return new FancyToolResult(false, "file-locked");
            }

            // 音频是两边共用的：只换一侧时新视频没音轨就保留原来的 acb/awb，不跟着视频一起清掉
            if (normalizedSide is null || audio is not null)
            {
                foreach (var path in AudioFilePaths(id))
                {
                    if (!tx.Backup(path)) return new FancyToolResult(false, "file-locked");
                }
            }

            var targetPath = Path.Combine(IntroMovieDir, VideoFileName(id, normalizedSide));
            tx.Place(stagedVideoPath, targetPath);

            if (normalizedSide is not null)
            {
                // 只替换一侧时，另一侧还没有就先补一份，保证左右都在（游戏要求 L、R 同时存在）。
                // 从单文件切到分屏时，另一侧要用原来那个单文件——否则两边都会变成这次上传的视频。
                var otherSide = normalizedSide == "L" ? "R" : "L";
                var otherPath = Path.Combine(IntroMovieDir, VideoFileName(id, otherSide));
                if (!System.IO.File.Exists(otherPath))
                {
                    // 复制也走事务：后面任何一步失败，这份多出来的文件要跟着一起撤掉
                    var otherStagePath = tx.StagePath("other-side.mp4");
                    PlatformFile.CopyFile(previousSingle ?? targetPath, otherStagePath);
                    tx.Place(otherStagePath, otherPath);
                }
            }

            // 整段替换视频时音频必须和视频配套（旧的已经被备份走，提交即删除）；只换一侧时音频保留
            if (audio is not null)
            {
                tx.Place(audio.AcbPath, Path.Combine(IntroMovieDir, $"Enter_{id}.acb"));
                tx.Place(audio.AwbPath, Path.Combine(IntroMovieDir, $"Enter_{id}.awb"));
            }

            tx.Commit();
            return new FancyToolResult(true, null);
        }
        catch (Exception e)
        {
            logger.LogError(e, "转换开场视频失败: {id}", id);
            return new FancyToolResult(false, e is FancyFileLockedException ? "file-locked" : "convert-failed");
        }
        finally
        {
            try
            {
                uploadDir.Delete(true);
            }
            catch
            {
                // 忽略清理失败
            }
        }
    }

    /// <summary>
    /// 把当前的「左右屏同一个视频」复制一份到 stage，供分屏补齐另一侧时使用。
    /// </summary>
    private static string? SnapshotSingleMovie(int id, FancyFileTransaction tx)
    {
        var singlePath = Path.Combine(IntroMovieDir, VideoFileName(id, null));
        if (!System.IO.File.Exists(singlePath)) return null;

        var snapshotPath = tx.StagePath("previous-single.mp4");
        PlatformFile.CopyFile(singlePath, snapshotPath);
        return snapshotPath;
    }

    /// <summary>
    /// 替换 / 新增开场音频。左右屏共用一套音频（Enter_&lt;id&gt;.acb / .awb），
    /// 任意 ffmpeg 能解码的音频都会先解成 wav，再封装成 acb/awb。
    /// </summary>
    [HttpPost]
    [DisableRequestSizeLimit]
    public async Task<FancyToolResult> SetIntroAudio([FromForm] int id, IFormFile file)
    {
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
        if (file is null || file.Length <= 0) return new FancyToolResult(false, "invalid-audio");

        var uploadDir = Directory.CreateTempSubdirectory();
        Directory.CreateDirectory(IntroMovieDir);
        using var tx = new FancyFileTransaction(IntroMovieDir, logger);
        try
        {
            var inputPath = Path.Combine(uploadDir.FullName, "input" + Path.GetExtension(file.FileName));
            await using (var stream = System.IO.File.Create(inputPath))
            {
                await file.CopyToAsync(stream);
            }

            var audio = await BuildIntroAudio(inputPath, id, tx.StageDirPath);
            if (audio is null) return new FancyToolResult(false, "invalid-audio");

            var acbPath = Path.Combine(IntroMovieDir, $"Enter_{id}.acb");
            var awbPath = Path.Combine(IntroMovieDir, $"Enter_{id}.awb");
            if (!tx.Backup(acbPath) || !tx.Backup(awbPath)) return new FancyToolResult(false, "file-locked");

            tx.Place(audio.AcbPath, acbPath);
            tx.Place(audio.AwbPath, awbPath);

            tx.Commit();
            return new FancyToolResult(true, null);
        }
        catch (Exception e)
        {
            logger.LogError(e, "写入开场音频失败: {id}", id);
            return new FancyToolResult(false, e is FancyFileLockedException ? "file-locked" : "write-failed");
        }
        finally
        {
            try
            {
                uploadDir.Delete(true);
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

    private FancyToolResult DeleteIntroAudioCore(int id)
    {
        if (id <= 0) return new FancyToolResult(false, "invalid-id");
        var gamePathError = EnsureGamePath();
        if (gamePathError is not null) return new FancyToolResult(false, gamePathError);

        var ok = DeleteFilesWithRetry(
            Path.Combine(IntroMovieDir, $"Enter_{id}.acb"),
            Path.Combine(IntroMovieDir, $"Enter_{id}.awb"));
        if (ok) DropCachedIntroAudio(id);
        return new FancyToolResult(ok, ok ? null : "delete-failed");
    }

    /// <summary>
    /// 删掉 acb 解码出来的预览缓存：不然 temp 里会一直留着几十 MB 没人用的 wav。
    /// </summary>
    private void DropCachedIntroAudio(int id)
    {
        var cacheDir = Path.Combine(StaticSettings.tempPath, "IntroAudioPreview");
        if (!Directory.Exists(cacheDir)) return;

        try
        {
            foreach (var old in Directory.EnumerateFiles(cacheDir, $"Enter_{id}_*.wav"))
            {
                try
                {
                    System.IO.File.Delete(old);
                }
                catch
                {
                    // 还在被别人读着就下次再说
                }
            }
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "清理开场音频预览缓存失败: {id}", id);
        }
    }

    private sealed record IntroAudioFiles(string AcbPath, string AwbPath);

    /// <summary>
    /// 从上传的文件里提取音轨，生成到指定目录（acb/awb 成对）。
    /// 没有音轨或转换失败返回 null，由调用方决定是否清掉旧的 acb/awb。
    /// </summary>
    private async Task<IntroAudioFiles?> BuildIntroAudio(string inputPath, int id, string outputDir)
    {
        var wavPath = Path.Combine(outputDir, "audio.wav");
        var tmpAcbPath = Path.Combine(outputDir, $"Enter_{id}.acb");
        var tmpAwbPath = Path.Combine(outputDir, $"Enter_{id}.awb");

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
        finally
        {
            try
            {
                if (System.IO.File.Exists(wavPath)) System.IO.File.Delete(wavPath);
            }
            catch
            {
                // 忽略清理失败
            }
        }
    }

    private static string[] VideoFilePaths(int id) =>
    [
        Path.Combine(IntroMovieDir, VideoFileName(id, null)),
        Path.Combine(IntroMovieDir, VideoFileName(id, "L")),
        Path.Combine(IntroMovieDir, VideoFileName(id, "R")),
    ];

    private static string[] AudioFilePaths(int id) =>
    [
        Path.Combine(IntroMovieDir, $"Enter_{id}.acb"),
        Path.Combine(IntroMovieDir, $"Enter_{id}.awb"),
    ];

    private static string[] IntroFilePaths(int id) => [.. VideoFilePaths(id), .. AudioFilePaths(id)];

    [HttpPost]
    public async Task<FancyToolResult> DeleteIntroMovie(int id)
    {
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

    private FancyToolResult DeleteIntroMovieCore(int id)
    {
        if (id <= 0) return new FancyToolResult(false, "invalid-id");
        var gamePathError = EnsureGamePath();
        if (gamePathError is not null) return new FancyToolResult(false, gamePathError);

        var ok = DeleteFilesWithRetry(IntroFilePaths(id));
        if (ok) DropCachedIntroAudio(id);
        return new FancyToolResult(ok, ok ? null : "delete-failed");
    }

    private static string VideoFileName(int id, string? side) => side is null ? $"Enter_{id}.mp4" : $"Enter_{id}_{side}.mp4";

    private static string? ExistingPath(string fileName)
    {
        var path = Path.Combine(IntroMovieDir, fileName);
        return System.IO.File.Exists(path) ? path : null;
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

        var jacketPath = id > 0 ? FindJacket(id) : null;
        return new CamouflageInfoDto(model.Name, model.Artist, model.NoteDesigner, model.HideNoteDesigner,
            model.UnlockScore, jacketPath is not null, jacketPath);
    }

    /// <summary>
    /// 取伪装封面供前端预览。
    /// </summary>
    [HttpGet]
    public ActionResult GetCamouflageJacket(int id)
    {
        var path = id > 0 ? FindJacket(id) : null;
        if (path is null) return NotFound();

        var contentType = Path.GetExtension(path).ToLowerInvariant() == ".png" ? "image/png" : "image/jpeg";
        return PhysicalFile(path, contentType, enableRangeProcessing: true);
    }

    [HttpPost]
    public async Task<FancyToolResult> SetCamouflage([FromForm] int id, [FromForm] string? name, [FromForm] string? artist,
        [FromForm] string? noteDesigner, [FromForm] bool hideNoteDesigner, [FromForm] double unlockScore)
    {
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
            WriteAllTextAtomic(Path.Combine(CamouflageDir, $"{id}.toml"), BuildCamouflageToml(model));
            return new FancyToolResult(true, null);
        }
        catch (Exception e)
        {
            logger.LogError(e, "写入曲目伪装配置失败: {id}", id);
            return new FancyToolResult(false, "write-failed");
        }
    }

    /// <summary>
    /// 只写真正有意义的字段。AQMai 把缺失或空白的字段当作「不伪装」，
    /// 但这是对方实现的细节；toml 是跨项目的文件契约，空值就不要写出去。
    /// </summary>
    private static string BuildCamouflageToml(CamouflageModel model)
    {
        var table = new TomlTable
        {
            ["Name"] = model.Name,
            ["Artist"] = model.Artist,
        };
        if (!string.IsNullOrWhiteSpace(model.NoteDesigner)) table["NoteDesigner"] = model.NoteDesigner;
        if (model.HideNoteDesigner) table["HideNoteDesigner"] = true;
        if (model.UnlockScore != 0) table["UnlockScore"] = model.UnlockScore;

        return Toml.FromModel(table, TomlOptions);
    }

    [HttpPost]
    [DisableRequestSizeLimit]
    public async Task<FancyToolResult> SetCamouflageJacket([FromForm] int id, IFormFile file)
    {
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

        Directory.CreateDirectory(CamouflageDir);
        using var tx = new FancyFileTransaction(CamouflageDir, logger);
        try
        {
            var stagedPath = tx.StagePath($"jacket{ext}");
            await using (var stream = System.IO.File.Create(stagedPath))
            {
                await file.CopyToAsync(stream);
            }

            // 同一首曲子只保留一张封面：其它后缀的旧封面也一起备份掉（提交时进回收站）
            foreach (var otherExt in JacketExts)
            {
                if (!tx.Backup(Path.Combine(CamouflageDir, $"{id}_jacket{otherExt}")))
                    return new FancyToolResult(false, "file-locked");
            }

            tx.Place(stagedPath, Path.Combine(CamouflageDir, $"{id}_jacket{ext}"));

            tx.Commit();
            return new FancyToolResult(true, null);
        }
        catch (Exception e)
        {
            logger.LogError(e, "写入曲目伪装封面失败: {id}", id);
            return new FancyToolResult(false, e is FancyFileLockedException ? "file-locked" : "write-failed");
        }
    }

    /// <summary>
    /// 只删封面，保留 toml。游戏侧在没有封面时显示默认的空封面，是合法状态。
    /// </summary>
    [HttpPost]
    public async Task<FancyToolResult> DeleteCamouflageJacket(int id)
    {
        var gate = GetFileGate(id);
        await gate.WaitAsync();
        try
        {
            if (id <= 0) return new FancyToolResult(false, "invalid-id");
            var gamePathError = EnsureGamePath();
            if (gamePathError is not null) return new FancyToolResult(false, gamePathError);

            var ok = DeleteFilesWithRetry(JacketExts.Select(ext => Path.Combine(CamouflageDir, $"{id}_jacket{ext}")).ToArray());
            return new FancyToolResult(ok, ok ? null : "delete-failed");
        }
        finally
        {
            gate.Release();
        }
    }

    [HttpPost]
    public async Task<FancyToolResult> DeleteCamouflage(int id)
    {
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
        if (id <= 0) return new FancyToolResult(false, "invalid-id");
        var gamePathError = EnsureGamePath();
        if (gamePathError is not null) return new FancyToolResult(false, gamePathError);

        var ok = DeleteFilesWithRetry(
            [
                Path.Combine(CamouflageDir, $"{id}.toml"),
                .. JacketExts.Select(ext => Path.Combine(CamouflageDir, $"{id}_jacket{ext}")),
            ]);
        return new FancyToolResult(ok, ok ? null : "delete-failed");
    }

    private static readonly string[] JacketExts = [".jpg", ".jpeg", ".png"];

    private static string? FindJacket(int id)
    {
        foreach (var ext in JacketExts)
        {
            var path = Path.Combine(CamouflageDir, $"{id}_jacket{ext}");
            if (System.IO.File.Exists(path)) return path;
        }

        return null;
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

    #region 文件事务

    /// <summary>文件被别的进程占着（前端预览的句柄没释放），重试之后仍然搬不动。</summary>
    private sealed class FancyFileLockedException(string message) : IOException(message)
    {
    }

    /// <summary>
    /// 文件事务：stage 目录就建在目标目录里面，所以所有搬迁都是一次同卷 rename
    /// —— 既不会因为临时目录在别的盘而退化成整文件复制，也不会像「复制备份」那样
    /// 让峰值占用变成两三倍。用法：Stage 出新文件 → Backup 掉会被覆盖的旧文件 →
    /// Place 生效 → Commit 成功（旧文件进回收站）或 Dispose 时回滚。
    /// </summary>
    private sealed class FancyFileTransaction : IDisposable
    {
        private const string StageDirName = ".mcm-tmp";
        private const string BackupPrefix = "bak_";
        private const string JournalFileName = "journal";
        private const string BackupEntry = "bak:";
        private const string PlacedEntry = "placed:";
        private const string CommitMarker = "commit";

        /// <summary>正在跑的事务的 stage 目录，恢复遗留备份时要跳过它们</summary>
        private static readonly ConcurrentDictionary<string, byte> ActiveStageDirs = new(StringComparer.OrdinalIgnoreCase);

        private readonly string _targetDir;
        private readonly ILogger _logger;
        private readonly List<(string Target, string Backup)> _backups = [];
        private readonly List<string> _placedTargets = [];
        private bool _finished;
        /// <summary>回滚没能把备份全部还原时保留 stage 目录，别把最后一份原件删了</summary>
        private bool _keepStageDir;

        public FancyFileTransaction(string targetDir, ILogger logger)
        {
            _targetDir = targetDir;
            _logger = logger;
            StageDirPath = Path.Combine(targetDir, StageDirName, Guid.NewGuid().ToString("N"));
            ActiveStageDirs[StageDirPath] = 0;
            RecoverStaleBackups();
            Directory.CreateDirectory(StageDirPath);
        }

        public string StageDirPath { get; }

        /// <summary>新文件的暂存路径（和最终目标同卷）。</summary>
        public string StagePath(string fileName) => Path.Combine(StageDirPath, fileName);

        private string JournalPath => Path.Combine(StageDirPath, JournalFileName);

        /// <summary>
        /// 进程被强杀 / 断电后，上一次事务可能停在「备份搬走了但还没提交」或者「新文件已生效但还没提交」的半路上。
        /// 这里扫一遍遗留的 stage 目录，没提交的整体撤回、已提交的清掉残留。
        /// </summary>
        private void RecoverStaleBackups()
        {
            var parent = Path.Combine(_targetDir, StageDirName);
            if (!Directory.Exists(parent)) return;

            // 先把目录列出来再动：边删除边枚举同一个父目录在有些文件系统上会抛异常
            foreach (var stageDir in Directory.EnumerateDirectories(parent).ToList())
            {
                if (ActiveStageDirs.ContainsKey(stageDir)) continue;

                try
                {
                    RecoverStageDir(stageDir);
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "恢复上次中断的事务失败: {dir}", stageDir);
                }
            }
        }

        private void RecoverStageDir(string stageDir)
        {
            var journalPath = Path.Combine(stageDir, JournalFileName);
            var journal = System.IO.File.Exists(journalPath) ? System.IO.File.ReadAllLines(journalPath) : [];

            if (journal.Contains(CommitMarker))
            {
                // 已经提交过了：正式路径上的新文件就是用户要的结果，剩下的只是没来得及清掉的备份
                Directory.Delete(stageDir, true);
                return;
            }

            var entriesOf = (string prefix) => journal
                .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
                .Select(line => line[prefix.Length..])
                .ToList();

            // 先撤掉已经生效的新文件，再把备份搬回去——顺序反过来的话，
            // 中途再挂一次就会留下「备份已还原、新文件还在」的混合状态
            foreach (var fileName in entriesOf(PlacedEntry))
            {
                var target = Path.Combine(_targetDir, fileName);
                try
                {
                    if (System.IO.File.Exists(target)) System.IO.File.Delete(target);
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "清理未完成的事务文件失败: {path}", target);
                }
            }

            var hasJournal = journal.Length > 0;
            var backups = entriesOf(BackupEntry);
            if (!hasJournal)
            {
                // 没有事务日志（旧版本留下的目录）：只能按「正式路径上缺什么就补什么」尽力恢复
                backups = Directory.EnumerateFiles(stageDir, $"{BackupPrefix}*")
                    .Select(Path.GetFileName)
                    .Select(name => name![BackupPrefix.Length..])
                    .ToList();
            }

            foreach (var fileName in backups)
            {
                var backupPath = Path.Combine(stageDir, BackupPrefix + fileName);
                var target = Path.Combine(_targetDir, fileName);
                if (!System.IO.File.Exists(backupPath)) continue;
                if (!hasJournal && System.IO.File.Exists(target)) continue;

                if (MoveWithRetry(backupPath, target) is null)
                    _logger.LogWarning("从上次中断的事务里恢复了 {target}", target);
            }

            // 备份搬走之后，没来得及启用的新文件和事务日志都成了垃圾，一起清掉
            Directory.Delete(stageDir, true);
        }

        /// <summary>
        /// 把旧文件 rename 进 stage 当备份。文件不存在时什么都不做。
        /// 被占用（前端还在预览）时重试，最终失败返回 false，调用方应整体放弃。
        /// </summary>
        public bool Backup(string path)
        {
            if (_backups.Any(entry => string.Equals(entry.Target, path, StringComparison.OrdinalIgnoreCase))) return true;
            if (!System.IO.File.Exists(path)) return true;

            // 备份名带上原文件名，进程中途挂掉后下次能照着搬回原位
            var backupPath = StagePath(BackupPrefix + Path.GetFileName(path));
            if (MoveWithRetry(path, backupPath) is not null) return false;

            _backups.Add((path, backupPath));
            AppendJournal(BackupEntry + Path.GetFileName(path));
            return true;
        }

        /// <summary>
        /// 把事务进度记到 stage 里的日志上。进程被强杀后只有靠它才知道哪些文件已经生效、
        /// 哪些还躺在备份里——光看正式路径上有没有文件是判断不出来的（已生效的新文件也在那儿）。
        /// </summary>
        private void AppendJournal(string line)
        {
            try
            {
                System.IO.File.AppendAllText(JournalPath, line + Environment.NewLine);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "写入事务日志失败: {path}", JournalPath);
            }
        }

        /// <summary>重写事务日志，只留还没处理完的条目。</summary>
        private void RewriteJournal(IReadOnlyCollection<string> lines)
        {
            try
            {
                if (lines.Count == 0)
                {
                    if (System.IO.File.Exists(JournalPath)) System.IO.File.Delete(JournalPath);
                    return;
                }

                System.IO.File.WriteAllLines(JournalPath, lines);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "重写事务日志失败: {path}", JournalPath);
            }
        }

        /// <summary>把 stage 里的新文件放到目标位置（同卷 rename）。</summary>
        public void Place(string stagedPath, string targetPath)
        {
            var error = MoveWithRetry(stagedPath, targetPath);
            if (error is null)
            {
                _placedTargets.Add(targetPath);
                AppendJournal(PlacedEntry + Path.GetFileName(targetPath));
                return;
            }

            // 只有目标确实被别人占着（预览句柄没释放）才提示「文件被占用」，
            // 磁盘满、权限不足之类报成占用会让用户白关一遍预览
            if (System.IO.File.Exists(targetPath) && IsFileLocked(targetPath))
                throw new FancyFileLockedException($"无法写入 {targetPath}");
            throw new IOException($"无法写入 {targetPath}", error);
        }

        /// <summary>
        /// 提交：备份的旧文件送回收站（和项目里其它删除操作一致，用户还能捞回来），清掉 stage。
        /// 备份删不掉只是留垃圾，不能因此报失败——新文件此时已经就位了。
        /// </summary>
        public void Commit()
        {
            // 先落提交标记再删备份：反过来的话，标记没写成而备份已经没了，
            // 恢复逻辑会把这次成功写入的新文件当成半成品删掉
            AppendJournal(CommitMarker);
            _finished = true;

            foreach (var (_, backupPath) in _backups)
            {
                try
                {
                    if (System.IO.File.Exists(backupPath) && !TryDeleteFile(backupPath))
                        _logger.LogWarning("旧文件清理失败: {path}", backupPath);
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "旧文件清理失败: {path}", backupPath);
                }
            }

            DropStageDir();
        }

        /// <summary>
        /// 回滚：先把已经生效的新文件删掉，再把备份原样搬回去。
        /// </summary>
        public void Rollback()
        {
            if (_finished) return;
            _finished = true;

            var failedDeletes = new List<string>();
            foreach (var target in _placedTargets)
            {
                try
                {
                    if (System.IO.File.Exists(target)) System.IO.File.Delete(target);
                }
                catch (Exception)
                {
                    // 尽力回滚，单个文件失败不影响其它文件
                    failedDeletes.Add(target);
                }
            }

            var failedRestores = new List<(string Target, string BackupPath)>();
            foreach (var (target, backupPath) in _backups)
            {
                try
                {
                    if (!System.IO.File.Exists(backupPath)) continue;
                    if (MoveWithRetry(backupPath, target) is not null) PlatformFile.CopyFile(backupPath, target);
                    if (!System.IO.File.Exists(target)) failedRestores.Add((target, backupPath));
                }
                catch (Exception)
                {
                    failedRestores.Add((target, backupPath));
                }
            }

            // 没撤干净：还原失败的备份可能就是原文件唯一的副本，绝不能把 stage 目录清掉。
            // 把还没处理完的部分留在事务日志里，下次开事务时接着撤——
            // 已经处理掉的行必须去掉，否则下次会去删刚刚还原好的文件
            if (failedDeletes.Count > 0 || failedRestores.Count > 0)
            {
                _keepStageDir = true;
                var restoredTargets = _backups
                    .Where(entry => failedRestores.All(failed => failed.Target != entry.Target))
                    .Select(entry => entry.Target)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                RewriteJournal(
                [
                    .. failedDeletes.Where(target => !restoredTargets.Contains(target))
                        .Select(target => PlacedEntry + Path.GetFileName(target)),
                    .. failedRestores.Select(entry => BackupEntry + Path.GetFileName(entry.Target)),
                ]);

                foreach (var (target, backupPath) in failedRestores)
                    _logger.LogError("回滚失败，原文件仍在 {backup}，需要还原到 {target}", backupPath, target);
                return;
            }

            DropStageDir();
        }

        public void Dispose()
        {
            if (!_finished) Rollback();
            if (!_keepStageDir) DropStageDir();
            ActiveStageDirs.TryRemove(StageDirPath, out _);
        }

        private void DropStageDir()
        {
            try
            {
                if (Directory.Exists(StageDirPath)) Directory.Delete(StageDirPath, true);
                // 顺手把空的 .mcm-tmp 父目录也收掉
                var parent = Path.Combine(_targetDir, StageDirName);
                if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
                    Directory.Delete(parent);
            }
            catch
            {
                // 忽略清理失败
            }
        }
    }

    #endregion

    #region 文件工具

    /// <summary>
    /// 覆盖式移动文件。目标被占用（例如前端还在预览旧文件）时等一会儿再试。
    /// 成功返回 null，失败返回最后一次的异常——调用方要能区分「被占用」和真的写不进去（磁盘满、权限）。
    /// </summary>
    private static Exception? MoveWithRetry(string source, string dest)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        Exception? lastError = null;
        while (true)
        {
            try
            {
                PlatformFile.MoveFile(source, dest);
                return null;
            }
            catch (Exception e) when (DateTime.UtcNow < deadline)
            {
                lastError = e;
                Thread.Sleep(100);
            }
            catch (Exception e)
            {
                return e;
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

    #endregion

    private static string? EnsureGamePath()
        => string.IsNullOrWhiteSpace(StaticSettings.GamePath) || !Directory.Exists(StaticSettings.GamePath)
            ? "game-path-missing"
            : null;
}
