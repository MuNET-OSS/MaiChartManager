using System.IO.Compression;
using System.Collections.Concurrent;
using System.Text;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MaiChartManager.Models;
using MaiChartManager.Platform;
using MaiChartManager.Utils;
using Microsoft.AspNetCore.Mvc;
using MuConvert.mai;
using NAudio.Lame;

namespace MaiChartManager.Controllers.Music;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api/{assetDir}/{id:int}")]
public partial class MusicTransferController(
    StaticSettings settings,
    ILogger<MusicTransferController> logger,
    IDesktopDialogService dialogService,
    ITaskbarProgress taskbarProgress,
    IProgressController progressController) : ControllerBase
{
    public record RequestCopyToRequest(MusicBatchController.MusicIdAndAssetDirPair[] music, bool removeEvents, bool legacyFormat);
    public record GenreChangeResult(bool Success, string? Error, int NewId);
    public record GenreChangeRequest(int GenreId);

    public enum MaidataSubdirMode
    {
        None = 0,
        Genre = 1,
        Version = 2,
    }

    // 原生选目录导出 maidata 的请求体：
    // music 为要导出的歌曲列表；ignoreVideo 控制是否跳过 PV；
    // subdir 控制是否按流派/版本建一级子目录；byId 为 true 时叶子目录用歌曲 id 命名。
    public record RequestExportMaidataRequest(
        MusicBatchController.MusicIdAndAssetDirPair[] music,
        bool ignoreVideo = false,
        MaidataSubdirMode subdir = MaidataSubdirMode.None,
        bool byId = false);

    private static int[] GetAudioCandidateIds(MusicXmlWithABJacket music)
    {
        return [music.CueId, music.Id, music.NonDxId];
    }

    private static string BuildAudioResolveErrorMessage(MusicXmlWithABJacket music)
    {
        var candidates = string.Join(", ", GetAudioCandidateIds(music)
            .Select(it => (int)(Math.Abs((long)it) % 10000))
            .Distinct()
            .Select(it => it.ToString("000000")));
        return $"Failed to resolve audio ACB/AWB for music {music.Id} ({music.Name}), cueId={music.CueId:000000}, nonDxId={music.NonDxId:000000}, candidates=[{candidates}].";
    }

    private static int GetBatchExportMaxConcurrency()
    {
        return Math.Max(1, Environment.ProcessorCount / 2);
    }

    /// <summary>
    /// 根据 AssetBundle 封面路径（如 ".../AssetBundleImages/jacket/ui_jacket_000123.ab"），
    /// 计算同级 "jacket_s" 目录下的小封面路径
    /// （如 ".../AssetBundleImages/jacket_s/ui_jacket_000123_s.ab"）。
    /// 若路径格式不符则返回 null。
    /// </summary>
    private static string? GetAssetBundleJacketSmallPath(string assetBundleJacketPath)
    {
        var dir = Path.GetDirectoryName(assetBundleJacketPath);
        if (string.IsNullOrWhiteSpace(dir)) return null;
        var parentDir = Path.GetDirectoryName(dir);
        if (string.IsNullOrWhiteSpace(parentDir)) return null;

        var jacketSDir = Path.Combine(parentDir, "jacket_s");
        var nameWithoutExt = Path.GetFileNameWithoutExtension(assetBundleJacketPath);
        var ext = Path.GetExtension(assetBundleJacketPath);
        return Path.Combine(jacketSDir, nameWithoutExt + "_s" + ext);
    }

    private static readonly ConcurrentDictionary<string, string> FileHashCache = new(StringComparer.OrdinalIgnoreCase);

    private static string GetFileHash(FileInfo fileInfo)
    {
        var cacheKey = $"{Path.GetFullPath(fileInfo.FullName)}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
        return FileHashCache.GetOrAdd(cacheKey, _ =>
        {
            using var stream = System.IO.File.OpenRead(fileInfo.FullName);
            return Convert.ToHexString(SHA256.HashData(stream));
        });
    }

    private static bool IsFileUnchanged(string sourcePath, string destinationPath)
    {
        if (!System.IO.File.Exists(destinationPath))
        {
            return false;
        }

        var sourceInfo = new FileInfo(sourcePath);
        var destinationInfo = new FileInfo(destinationPath);
        if (sourceInfo.Length == destinationInfo.Length
            && sourceInfo.LastWriteTimeUtc.Ticks == destinationInfo.LastWriteTimeUtc.Ticks)
        {
            return true;
        }

        return string.Equals(GetFileHash(sourceInfo), GetFileHash(destinationInfo), StringComparison.Ordinal);
    }
    private static bool CopyFileIfChanged(string sourcePath, string destinationPath)
    {
        if (IsFileUnchanged(sourcePath, destinationPath))
        {
            return false;
        }

        var destinationDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        System.IO.File.Copy(sourcePath, destinationPath, overwrite: true);
        System.IO.File.SetLastWriteTimeUtc(destinationPath, System.IO.File.GetLastWriteTimeUtc(sourcePath));
        return true;
    }

    private static void CopyDirectoryIfChanged(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", System.IO.SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            var destinationFile = Path.Combine(destinationDirectory, relativePath);
            CopyFileIfChanged(sourceFile, destinationFile);
        }
    }

    private void CopySharedFileIfNeeded(
        string sourcePath,
        string destinationPath,
        ConcurrentDictionary<string, string> copiedSharedDestinations)
    {
        var normalizedSourcePath = Path.GetFullPath(sourcePath);
        var normalizedDestinationPath = Path.GetFullPath(destinationPath);

        if (!copiedSharedDestinations.TryAdd(normalizedDestinationPath, normalizedSourcePath))
        {
            if (copiedSharedDestinations.TryGetValue(normalizedDestinationPath, out var existingSourcePath)
                && !string.Equals(existingSourcePath, normalizedSourcePath, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "Skip shared copy to {destination}: already copied from {existingSource}, current source is {currentSource}.",
                    normalizedDestinationPath,
                    existingSourcePath,
                    normalizedSourcePath);
            }

            return;
        }

        try
        {
            var destinationDir = Path.GetDirectoryName(normalizedDestinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            CopyFileIfChanged(normalizedSourcePath, normalizedDestinationPath);
        }
        catch
        {
            copiedSharedDestinations.TryRemove(normalizedDestinationPath, out _);
            throw;
        }
    }

    private void CopyMusicToDirectory(
        MusicXmlWithABJacket music,
        string musicRootDir,
        string jacketRootDir,
        string soundRootDir,
        string movieRootDir,
        bool removeEvents,
        bool legacyFormat,
        ConcurrentDictionary<string, string> copiedSharedDestinations)
    {
        var musicDir = Path.GetDirectoryName(music.FilePath);
        if (string.IsNullOrWhiteSpace(musicDir) || !Directory.Exists(musicDir))
        {
            logger.LogWarning("Skip export for music {musicId}: invalid source directory from file path {filePath}", music.Id, music.FilePath);
            return;
        }

        // 复制音乐数据
        var musicDestDir = Path.Combine(musicRootDir, $"music{music.Id:000000}");
        CopyDirectoryIfChanged(musicDir, musicDestDir);

        if (removeEvents)
        {
            var xmlDoc = music.GetXmlWithoutEventsAndRights();
            xmlDoc.Save(Path.Combine(musicDestDir, "Music.xml"));
        }

        if (legacyFormat)
        {
            foreach (var file in Directory.EnumerateFiles(musicDestDir, "*.ma2", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }))
            {
                var originalContent = System.IO.File.ReadAllText(file);
                int.TryParse(MA2VersionRegex().Match(originalContent).Groups[1].Value, out var ma2Version);
                if (ma2Version == 3) continue; // 已经是103，不需要再转换
                
                var (chart, _) = new MA2Parser().Parse(originalContent);
                var (ma2_103, _) = new MA2_103Generator().Generate(chart);
                System.IO.File.WriteAllText(file, ma2_103);
            }
        }

        // 复制封面
        if (music.JacketPath is not null)
        {
            var jacketDest = Path.Combine(jacketRootDir, $"ui_jacket_{music.NonDxId:000000}{Path.GetExtension(music.JacketPath)}");
            CopySharedFileIfNeeded(music.JacketPath, jacketDest, copiedSharedDestinations);
        }
        else if (music.AssetBundleJacket is not null)
        {
            var jacketFileName = Path.GetFileName(music.AssetBundleJacket);
            CopySharedFileIfNeeded(music.AssetBundleJacket, Path.Combine(jacketRootDir, jacketFileName), copiedSharedDestinations);
            if (System.IO.File.Exists(music.AssetBundleJacket + ".manifest"))
            {
                CopySharedFileIfNeeded(music.AssetBundleJacket + ".manifest", Path.Combine(jacketRootDir, jacketFileName + ".manifest"), copiedSharedDestinations);
            }

            // Issue #42: jacket_s 位于同级目录，导出时必须写入 AssetBundleImages/jacket_s/
            var jacketSPath = GetAssetBundleJacketSmallPath(music.AssetBundleJacket);
            if (jacketSPath is not null && System.IO.File.Exists(jacketSPath))
            {
                var jacketSRootDir = Path.Combine(Path.GetDirectoryName(jacketRootDir)!, "jacket_s");
                var jacketSFileName = Path.GetFileName(jacketSPath);
                CopySharedFileIfNeeded(jacketSPath, Path.Combine(jacketSRootDir, jacketSFileName), copiedSharedDestinations);
                if (System.IO.File.Exists(jacketSPath + ".manifest"))
                {
                    CopySharedFileIfNeeded(jacketSPath + ".manifest", Path.Combine(jacketSRootDir, jacketSFileName + ".manifest"), copiedSharedDestinations);
                }
            }
        }
        else if (music.PseudoAssetBundleJacket is not null)
        {
            var jacketFileName = Path.GetFileName(music.PseudoAssetBundleJacket);
            CopySharedFileIfNeeded(music.PseudoAssetBundleJacket, Path.Combine(jacketRootDir, jacketFileName), copiedSharedDestinations);
        }

        // 复制 ACB/AWB 音频
        if (AudioConvert.TryResolveAcbAwb(GetAudioCandidateIds(music), out var resolvedAudioId, out var acb, out var awb)
            && acb is not null
            && awb is not null)
        {
            CopySharedFileIfNeeded(acb, Path.Combine(soundRootDir, $"music{resolvedAudioId:000000}.acb"), copiedSharedDestinations);
            CopySharedFileIfNeeded(awb, Path.Combine(soundRootDir, $"music{resolvedAudioId:000000}.awb"), copiedSharedDestinations);
        }
        else
        {
            logger.LogWarning("{message}", BuildAudioResolveErrorMessage(music));
        }

        // 复制视频数据
        if (StaticSettings.MovieDataMap.TryGetValue(music.NonDxId, out var movie))
        {
            CopySharedFileIfNeeded(movie, Path.Combine(movieRootDir, $"{music.NonDxId:000000}{Path.GetExtension(movie)}"), copiedSharedDestinations);
        }
    }

    [HttpPost]
    [Route("/MaiChartManagerServlet/[action]Api")]
    public void RequestCopyTo(RequestCopyToRequest request)
    {
        var dest = dialogService.PickFolder(Locale.SelectTargetLocation);
        if (dest is null) return;
        logger.LogInformation("CopyTo: {dest}", dest);

        var showProgress = request.music.Length > 1;
        using var progress = showProgress
            ? progressController.Begin(Locale.Exporting, string.Format(Locale.ExportingMultipleMusic, request.music.Length), Locale.Cancelling)
            : null;
        progress?.Report(0, (ulong)request.music.Length);

        if (request.music.Length == 0)
        {
            return;
        }

        var musicRootDir = Path.Combine(dest, "music");
        var jacketRootDir = Path.Combine(dest, "AssetBundleImages", "jacket");
        var soundRootDir = Path.Combine(dest, "SoundData");
        var movieRootDir = Path.Combine(dest, "MovieData");
        Directory.CreateDirectory(musicRootDir);
        Directory.CreateDirectory(jacketRootDir);
        Directory.CreateDirectory(soundRootDir);

        var musicIndex = new Dictionary<(int Id, string AssetDir), MusicXmlWithABJacket>();
        foreach (var music in settings.GetMusicList())
        {
            musicIndex.TryAdd((music.Id, music.AssetDir), music);
        }

        var progressLock = new object();
        var completed = 0;
        var maxConcurrency = GetBatchExportMaxConcurrency();
        var progressStep = Math.Max(1, request.music.Length / 100);
        var copiedSharedDestinations = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var cancellation = new CancellationTokenSource();

        try
        {
            Parallel.ForEach(request.music, new ParallelOptions
            {
                MaxDegreeOfParallelism = maxConcurrency,
                CancellationToken = cancellation.Token,
            }, (musicId, state) =>
            {
                if (progress is not null)
                {
                    lock (progressLock)
                    {
                        if (progress.IsCancelled)
                        {
                            cancellation.Cancel();
                            state.Stop();
                            return;
                        }
                    }
                }

                string? currentMusicName = null;
                if (!musicIndex.TryGetValue((musicId.Id, musicId.AssetDir), out var music))
                {
                    logger.LogWarning("Skip export: music {musicId} in {assetDir} not found.", musicId.Id, musicId.AssetDir);
                }
                else
                {
                    currentMusicName = music.Name;
                    CopyMusicToDirectory(music, musicRootDir, jacketRootDir, soundRootDir, movieRootDir, request.removeEvents, request.legacyFormat, copiedSharedDestinations);
                }

                var done = Interlocked.Increment(ref completed);
                if (progress is not null && (done % progressStep == 0 || done == request.music.Length))
                {
                    lock (progressLock)
                    {
                        progress.Report((ulong)done, (ulong)request.music.Length, currentMusicName);
                    }
                }
            });
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("批量导出被用户取消。");
        }
    }

    [HttpGet]
    public void ExportOpt(int id, string assetDir, bool removeEvents = false, bool legacyFormat = false)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music is null) return;
        var musicDir = Path.GetDirectoryName(music.FilePath);
        if (string.IsNullOrWhiteSpace(musicDir) || !Directory.Exists(musicDir))
        {
            var message = $"Invalid source directory for music {music.Id}: {music.FilePath}";
            logger.LogError("{message}", message);
            throw new DirectoryNotFoundException(message);
        }

        var zipStream = HttpContext.Response.BodyWriter.AsStream();
        using var zipArchive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true);

        // 复制音乐数据
        foreach (var file in Directory.EnumerateFiles(musicDir))
        {
            if (Path.GetFileName(file).Equals("Music.xml", StringComparison.InvariantCultureIgnoreCase) && removeEvents)
            {
                logger.LogInformation("从 Music.xml 中移除 Events 和版权信息");
                var xmlDoc = music.GetXmlWithoutEventsAndRights();
                var entry = zipArchive.CreateEntry($"music/music{music.Id:000000}/Music.xml");
                using var stream = entry.Open();
                xmlDoc.Save(stream);
                continue;
            }

            if (legacyFormat && Path.GetExtension(file).Equals(".ma2", StringComparison.InvariantCultureIgnoreCase))
            {
                var ma2 = System.IO.File.ReadAllText(file);
                int.TryParse(MA2VersionRegex().Match(ma2).Groups[1].Value, out var ma2Version);
                if (ma2Version != 3)
                { // 不是103才进行转换，如果已经是103的话，不需要再转换
                    var (chart, _) = new MA2Parser().Parse(ma2);
                    (ma2, _) = new MA2_103Generator().Generate(chart);
                }
                var entry = zipArchive.CreateEntry($"music/music{music.Id:000000}/{Path.GetFileName(file)}");
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream);
                writer.Write(ma2);
                writer.Close();
            }
            else
            {
                zipArchive.CreateEntryFromFile(file, $"music/music{music.Id:000000}/{Path.GetFileName(file)}");
            }
        }

        // 复制封面
        if (music.JacketPath is not null)
        {
            zipArchive.CreateEntryFromFile(music.JacketPath, $"AssetBundleImages/jacket/ui_jacket_{music.NonDxId:000000}{Path.GetExtension(music.JacketPath)}");
        }
        else if (music.AssetBundleJacket is not null)
        {
            zipArchive.CreateEntryFromFile(music.AssetBundleJacket, $"AssetBundleImages/jacket/{Path.GetFileName(music.AssetBundleJacket)}");
            if (System.IO.File.Exists(music.AssetBundleJacket + ".manifest"))
            {
                zipArchive.CreateEntryFromFile(music.AssetBundleJacket + ".manifest", $"AssetBundleImages/jacket/{Path.GetFileName(music.AssetBundleJacket)}.manifest");
            }

            // Issue #42: jacket_s 位于同级目录，导出时必须写入 AssetBundleImages/jacket_s/
            var jacketSPath = GetAssetBundleJacketSmallPath(music.AssetBundleJacket);
            if (jacketSPath is not null && System.IO.File.Exists(jacketSPath))
            {
                zipArchive.CreateEntryFromFile(jacketSPath, $"AssetBundleImages/jacket_s/{Path.GetFileName(jacketSPath)}");
                if (System.IO.File.Exists(jacketSPath + ".manifest"))
                {
                    zipArchive.CreateEntryFromFile(jacketSPath + ".manifest", $"AssetBundleImages/jacket_s/{Path.GetFileName(jacketSPath)}.manifest");
                }
            }
        }
        else if (music.PseudoAssetBundleJacket is not null)
        {
            zipArchive.CreateEntryFromFile(music.PseudoAssetBundleJacket, $"AssetBundleImages/jacket/{Path.GetFileName(music.PseudoAssetBundleJacket)}");
        }

        // 复制 ACB/AWB 音频
        if (!AudioConvert.TryResolveAcbAwb(GetAudioCandidateIds(music), out var resolvedAudioId, out var acb, out var awb) || acb is null || awb is null)
        {
            var message = BuildAudioResolveErrorMessage(music);
            logger.LogError("{message}", message);
            throw new FileNotFoundException(message);
        }
        zipArchive.CreateEntryFromFile(acb, $"SoundData/music{resolvedAudioId:000000}.acb");
        zipArchive.CreateEntryFromFile(awb, $"SoundData/music{resolvedAudioId:000000}.awb");

        // 复制视频数据
        if (StaticSettings.MovieDataMap.TryGetValue(music.NonDxId, out var movie))
        {
            zipArchive.CreateEntryFromFile(movie, $"MovieData/{music.NonDxId:000000}{Path.GetExtension(movie)}");
        }
    }

    /// <summary>
    /// 双人谱的文件名：xxx.ma2 -> xxx_L.ma2
    /// </summary>
    private static string WithSide(string path, string side) => path.Replace(".ma2", side + ".ma2");

    private void DeleteIfExists(params string[] path)
    {
        foreach (var p in path)
        {
            if (Directory.Exists(p))
            {
                logger.LogInformation("Delete directory: {p}", p);
                PlatformFile.DeleteDirectory(p);
            }

            if (System.IO.File.Exists(p))
            {
                logger.LogInformation("Delete file: {p}", p);
                PlatformFile.DeleteFile(p);
            }
        }
    }

    private void DeleteAb(string abPath)
    {
        PlatformFile.DeleteFile(abPath);
        if (System.IO.File.Exists(abPath + ".manifest"))
            PlatformFile.DeleteFile(abPath + ".manifest");
    }

    private void MoveJacketSoundVideo(MusicXmlWithABJacket music, int newId, string assetDir)
    {
        var newNonDxId = newId % 10000;
        // 当新ID和旧ID的 NonDx部分相同时（例如 5003 -> 15003），封面、音频、视频文件的目标路径与源路径完全一致，因此既不需要也不应该删除/移动它们。
        if (newNonDxId == music.NonDxId) return;
        
        var abiDir = Path.Combine(StaticSettings.StreamingAssets, assetDir, "AssetBundleImages", "jacket");
        var abiSDir = Path.Combine(StaticSettings.StreamingAssets, assetDir, "AssetBundleImages", "jacket_s");
        Directory.CreateDirectory(abiDir);
        Directory.CreateDirectory(abiSDir);
        var abJacketTarget = Path.Combine(abiDir, $"ui_jacket_{newNonDxId:000000}.ab");
        var abJacketSTarget = Path.Combine(abiSDir, $"ui_jacket_{newNonDxId:000000}_s.ab");
        var acbawbTarget = Path.Combine(StaticSettings.StreamingAssets, assetDir, "SoundData", $"music{newNonDxId:000000}");
        var movieTarget = Path.Combine(StaticSettings.StreamingAssets, assetDir, "MovieData", $"{newNonDxId:000000}");
        DeleteIfExists(abJacketTarget, abJacketTarget + ".manifest", abJacketSTarget, abJacketSTarget + ".manifest", acbawbTarget + ".acb", acbawbTarget + ".awb", movieTarget + ".dat", movieTarget + ".mp4");

        #region 移动或重打包封面图
        var jacketSourcePath = music.JacketPath is not null ? music.JacketPath : music.PseudoAssetBundleJacket;
        if (jacketSourcePath is not null)
        {
            // 如果music.JacketPath或music.PseudoAssetBundleJacket不为空，则直接执行移动逻辑即可
            var localJacketTarget = Path.Combine(abiDir, $"ui_jacket_{newNonDxId:000000}{Path.GetExtension(jacketSourcePath)}");
            DeleteIfExists(localJacketTarget);
            logger.LogInformation("Move jacket: {jacketSourcePng} -> {localJacketTarget}", jacketSourcePath, localJacketTarget);
            PlatformFile.MoveFile(jacketSourcePath, localJacketTarget);
        }
        else if (music.AssetBundleJacket is not null)
        {
            var oldAb = music.AssetBundleJacket!;
            var oldSmallAb = GetAssetBundleJacketSmallPath(oldAb);
            var idPad = $"{newNonDxId:000000}";
            logger.LogInformation("Repack jacket AB: {oldMainAb} -> {abJacketTarget}", oldAb, abJacketTarget);
            
            // 重打包大jacket
            AssetBundleCreator.RepackTextureAssetBundle(
                oldAb,
                abJacketTarget,
                $"UI_Jacket_{idPad}",
                $"assets/assetbundle/jacket/ui_jacket_{idPad}.png",
                $"jacket/ui_jacket_{idPad}.ab");
            
            // 对小jacket：如果存在，重打包；如果不存在，则从png重新缩放，重新CreateTextureAssetBundle
            if (oldSmallAb is not null && System.IO.File.Exists(oldSmallAb))
            {
                AssetBundleCreator.RepackTextureAssetBundle(
                    oldSmallAb,
                    abJacketSTarget,
                    $"UI_Jacket_{idPad}_s",
                    $"assets/assetbundle/jacket_s/ui_jacket_{idPad}_s.png",
                    $"jacket_s/ui_jacket_{idPad}_s.ab");
            }
            else
            {
                var pngBytes = music.GetMusicJacketPngData();
                AssetBundleCreator.CreateTextureAssetBundle(
                    pngBytes,
                    abJacketSTarget,
                    $"UI_Jacket_{idPad}_s",
                    $"assets/assetbundle/jacket_s/ui_jacket_{idPad}_s.png",
                    $"jacket_s/ui_jacket_{idPad}_s.ab",
                    resizeWidth: 200,
                    resizeHeight: 200);
            }
            
            DeleteAb(oldAb);
            if (oldSmallAb is not null) DeleteAb(oldSmallAb);

            StaticSettings.AssetBundleJacketMap.Remove(music.NonDxId);
            StaticSettings.AssetBundleJacketMap[newNonDxId] = abJacketTarget;
        }
        #endregion

        #region 移动音频和视频
        // 我也不知道它需不需要重新保存，先直接移动试试
        // 是可以的
        if (StaticSettings.AcbAwb.TryGetValue($"music{music.NonDxId:000000}.acb", out var acb))
        {
            logger.LogInformation("Move acb: {acb} -> {acbawbTarget}.acb", acb, acbawbTarget);
            PlatformFile.MoveFile(acb, acbawbTarget + ".acb");
        }

        if (StaticSettings.AcbAwb.TryGetValue($"music{music.NonDxId:000000}.awb", out var awb))
        {
            logger.LogInformation("Move awb: {awb} -> {acbawbTarget}.awb", awb, acbawbTarget);
            PlatformFile.MoveFile(awb, acbawbTarget + ".awb");
        }

        // 视频数据
        if (StaticSettings.MovieDataMap.TryGetValue(music.NonDxId, out var movie))
        {
            logger.LogInformation("Move movie: {movie} -> {movieTarget}", movie, movieTarget);
            PlatformFile.MoveFile(movie, movieTarget + Path.GetExtension(movie));
        }
        #endregion
    }
    
    [HttpPost]
    public async Task ModifyId(int id, [FromBody] int newId, string assetDir)
    {
#if WINDOWS
        if (IapManager.License != IapManager.LicenseStatus.Active) return;
#endif
        var music = settings.GetMusic(id, assetDir);
        if (music is null) return;
        var error = await ModifyIdCore(music, newId, assetDir);
        if (error is not null) throw new InvalidOperationException(error);
    }

    /// <summary>
    /// 改乐曲 ID 的实现。返回 null 表示成功，否则是错误信息。
    /// </summary>
    private async Task<string?> ModifyIdCore(MusicXmlWithABJacket music, int newId, string assetDir)
    {
        var musicDir = Path.GetDirectoryName(music.FilePath);
        if (string.IsNullOrWhiteSpace(musicDir) || !Directory.Exists(musicDir))
        {
            var message = $"Invalid source directory for music {music.Id}: {music.FilePath}";
            logger.LogError("{message}", message);
            return message;
        }
        if (music.Id == newId) return null;

        var newMusicDir = Path.Combine(StaticSettings.StreamingAssets, assetDir, "music", $"music{newId:000000}");
        DeleteIfExists(newMusicDir);

        MoveJacketSoundVideo(music, newId, assetDir); // 移动封面图、音频、视频

        // 谱面
        var oldMusicDir = Path.GetDirectoryName(music.FilePath)!;
        for (var i = 0; i < music.Charts.Length; i++)
        {
            var chart = music.Charts[i];
            if (!chart.Enable) continue;

            var oldFilePath = Path.Combine(oldMusicDir, chart.Path);
            var newFileName = $"{newId:000000}_0{i}.ma2";
            var newFilePath = Path.Combine(oldMusicDir, newFileName);

            // 双人谱只有 _L / _R 两个文件，没有单谱文件，任意一种存在就算有谱面
            if (!System.IO.File.Exists(oldFilePath)
                && !System.IO.File.Exists(WithSide(oldFilePath, "_L"))
                && !System.IO.File.Exists(WithSide(oldFilePath, "_R")))
                continue;

            logger.LogInformation("Move chart: {chart.Path} -> {newFileName}", chart.Path, newFileName);
            if (System.IO.File.Exists(oldFilePath)) PlatformFile.MoveFile(oldFilePath, newFilePath);
            // 双人谱的左右谱面文件一起改名
            foreach (var side in new[] { "_L", "_R" })
            {
                var oldSideFile = WithSide(oldFilePath, side);
                if (System.IO.File.Exists(oldSideFile))
                    PlatformFile.MoveFile(oldSideFile, WithSide(newFilePath, side));
            }

            chart.Path = newFileName;
        }

        // 保存 XML
        music.Id = newId;
        music.Save();
        Directory.CreateDirectory(Path.Combine(StaticSettings.StreamingAssets, assetDir, "music"));
        logger.LogInformation("Move music dir: {oldMusicDir} -> {newMusicDir}", oldMusicDir, newMusicDir);
        PlatformFile.MoveDirectory(oldMusicDir, newMusicDir);

        // 重新扫描全部
        await settings.RescanAll();
        return null;
    }

    /// <summary>
    /// 切换乐曲流派。切到/切回宴会场时，ID 变更（宴会场 ID 固定后四位、顺延序号）和谱面转换
    /// 都在这个接口里一次做完，避免前端分步调用留下半改状态。
    /// 返回 Error 为 null 表示成功，NewId 是处理后的乐曲 ID。
    /// </summary>
    [HttpPost]
    public async Task<GenreChangeResult> ChangeMusicGenre(int id, [FromBody] GenreChangeRequest request, string assetDir)
    {
        var genreId = request.GenreId;
        var music = settings.GetMusic(id, assetDir);
        if (music is null) return new GenreChangeResult(false, GenreChangeError.MusicNotFound, id);

        var wasUtage = music.GenreId == UtageConverter.UtageGenreId;
        var toUtage = genreId == UtageConverter.UtageGenreId;

        // 和宴会场无关的流派切换保持原来的行为：只改流派
        if (wasUtage == toUtage)
        {
            music.GenreId = genreId;
            music.Save();
            return new GenreChangeResult(true, null, id);
        }

        var newId = id;
        var chartIndex = -1;
        if (toUtage)
        {
            // 宴会场只认 Basic 谱面，先确认只有一个启用的难度
            var error = UtageConverter.FindSingleEnabledChart(music, out chartIndex);
            if (error is not null) return new GenreChangeResult(false, error, id);
            if (UtageConverter.IsUtageId(id))
            {
                // 已经是宴会场 ID 了，只改流派
                newId = id;
            }
            else
            {
                // 目标 ID 被占用时顺延一个序号（后四位不变），而不是覆盖掉已有的宴会场谱面
                var freeId = UtageConverter.FindFreeUtageId(id,
                    candidate => settings.GetMusic(candidate, assetDir) is not null);
                if (freeId is null) return new GenreChangeResult(false, GenreChangeError.NoAvailableId, id);
                newId = freeId.Value;
            }
        }
        else
        {
            // 含宴会场特有 L/R 双人谱的乐曲无法转成普通谱面
            if (UtageConverter.HasSideCharts(music))
                return new GenreChangeResult(false, GenreChangeError.DoublePlayerChart, id);
            newId = id > UtageConverter.UtageIdStart ? id - UtageConverter.UtageIdStart : id;
        }

        if (newId != id)
        {
#if WINDOWS
            if (IapManager.License != IapManager.LicenseStatus.Active)
                return new GenreChangeResult(false, GenreChangeError.LicenseRequired, id);
#endif
            // 切到宴会场时挑的本来就是空闲 ID，只有切回普通流派时目标 ID 可能已被占用
            if (settings.GetMusic(newId, assetDir) is not null)
                return new GenreChangeResult(false, GenreChangeError.TargetIdExists, id);
        }

        var converted = false;
        try
        {
            if (toUtage && chartIndex > 0)
            {
                var error = UtageConverter.MoveToBasic(music, chartIndex, logger);
                if (error is not null) return new GenreChangeResult(false, error, id);
                converted = true;
            }

            var targetId = id;
            if (newId != id)
            {
                var error = await ModifyIdCore(music, newId, assetDir);
                if (error is not null)
                {
                    TryRollback();
                    logger.LogError("切换流派时改 ID 失败: {error}", error);
                    return new GenreChangeResult(false, GenreChangeError.InvalidMusicDirectory, id);
                }
                targetId = newId;
            }

            var target = settings.GetMusic(targetId, assetDir);
            if (target is null)
            {
                TryRollback();
                return new GenreChangeResult(false, GenreChangeError.MusicNotFound, id);
            }

            target.GenreId = genreId;
            target.Save();
            return new GenreChangeResult(true, null, targetId);
        }
        catch (Exception e)
        {
            logger.LogError(e, "切换流派失败: {id} -> {genreId}", id, genreId);
            TryRollback();
            return new GenreChangeResult(false, GenreChangeError.Unexpected, id);
        }

        // 谱面已经转换成功、但后续步骤失败时，把谱面换回去
        void TryRollback()
        {
            if (!converted) return;
            try
            {
                var error = UtageConverter.RestoreFromBasic(music, chartIndex, logger);
                if (error is not null) logger.LogError("回滚谱面转换失败: {error}", error);
            }
            catch (Exception rollbackError)
            {
                logger.LogError(rollbackError, "回滚谱面转换失败");
            }
        }
    }

    [HttpGet]
    public async Task ExportAsMaidata(int id, string assetDir, bool ignoreVideo = false)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music is null) return;
        var (simaiFile, track, img, imgExt, video) = await _exportAsMaidata(music, ignoreVideo);
        
        await using var zipStream = HttpContext.Response.BodyWriter.AsStream();
        using var zipArchive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true);
        
        var maidataEntry = zipArchive.CreateEntry("maidata.txt");
        await using var maidataStream = maidataEntry.Open();
        await maidataStream.WriteAsync(Encoding.UTF8.GetBytes(simaiFile.ToString()));
        maidataStream.Close();
        
        if (img is not null)
        {
            var imageEntry = zipArchive.CreateEntry($"bg{imgExt}");
            await using var imageStream = imageEntry.Open();
            await imageStream.WriteAsync(img);
            imageStream.Close();
        }
        
        var soundEntry = zipArchive.CreateEntry("track.mp3");
        await using var soundStream = soundEntry.Open();
        await soundStream.WriteAsync(track);
        soundStream.Close();

        if (video is not null)
        {
            var pvEntry = zipArchive.CreateEntry("pv.mp4");
            await using var pvStream = pvEntry.Open();
            await pvStream.WriteAsync(video);
            pvStream.Close();
        }
    }
    
    private async Task<(Maidata simaiFile, byte[] track, byte[]? img, string imgExt, byte[]? video)> _exportAsMaidata(MusicXmlWithABJacket music, bool ignoreVideo = false)
    {
        var musicDir = Path.GetDirectoryName(music.FilePath);
        if (string.IsNullOrWhiteSpace(musicDir) || !Directory.Exists(musicDir))
        {
            var message = $"Invalid source directory for music {music.Id}: {music.FilePath}";
            logger.LogError("{message}", message);
            throw new DirectoryNotFoundException(message);
        }
        var isUtage = music.Id >= 100000 || music.GenreId == 107;

        var simaiFile = new Maidata();
        simaiFile.Title = music.Name;
        simaiFile.Artist = music.Artist;
        simaiFile.WholeBpm = music.Bpm;
        simaiFile.First = 0;
        simaiFile["shortid"] = music.Id.ToString();
        simaiFile["genreid"] = music.GenreId.ToString();
        var genre = StaticSettings.GenreList.FirstOrDefault(it => it.Id == music.GenreId);
        if (genre is not null) simaiFile["genre"] = genre.GenreName;
        simaiFile["versionid"] = music.AddVersionId.ToString();
        var version = StaticSettings.VersionList.FirstOrDefault(it => it.Id == music.AddVersionId);
        if (version is not null) simaiFile["version"] = version.GenreName;

        // demo_seek（预览起止时间）
        try
        {
            if (AudioConvert.TryResolveAcbAwb(GetAudioCandidateIds(music), out _, out var previewAcb, out _) && previewAcb is not null)
            {
                var previewTime = CriUtils.GetAudioPreviewTime(previewAcb);
                if (previewTime.StartTime >= 0 && previewTime.EndTime > previewTime.StartTime)
                {
                    simaiFile.Demo = ((float)previewTime.StartTime, (float)(previewTime.EndTime - previewTime.StartTime));
                }
            }
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "ExportAsMaidata: 获取音频预览时间失败，已忽略。");
        }
        
        for (var i = 0; i < music.Charts.Length; i++)
        {
            var chart = music.Charts[i];
            if (chart is null || !chart.Enable || string.IsNullOrWhiteSpace(chart.Path)) continue;

            var chartPath = Path.Combine(musicDir, chart.Path);
            if (!System.IO.File.Exists(chartPath))
            {
                var fallbackPath = Path.Combine(musicDir, chart.Path.Replace(".ma2", "_L.ma2", StringComparison.OrdinalIgnoreCase));
                if (!System.IO.File.Exists(fallbackPath)) continue;
                chartPath = fallbackPath;
            }

            var simaiLevelId = isUtage ? 7 : i + 2;
            await ConvertToSimai(music, chart, chartPath, simaiFile, simaiLevelId);
            
            if (isUtage && music.UtagePlayStyle == 1)
            { // 双人宴谱，尝试把右侧导出成难度8
                var rightPath = Path.Combine(musicDir, chart.Path.Replace(".ma2", "_R.ma2", StringComparison.OrdinalIgnoreCase));
                if (!System.IO.File.Exists(rightPath)) continue;
                await ConvertToSimai(music, chart, rightPath, simaiFile, 8);
            }
        }
        
        var appVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "";
        simaiFile["chartconverter"] = $"MaiChartManager v{appVersion}";

        // 导出封面
        var img = music.GetMusicJacketPngData();
        var imgExt = (Path.GetExtension(music.RealJacketPath) ?? ".png").ToLowerInvariant();
        if (imgExt == ".ab") imgExt = ".png";

        // 导出音频
        var tag = new ID3TagData
        {
            Title = music.Name,
            Artist = music.Artist,
            Album = genre?.GenreName,
            Track = music.Id.ToString(),
            Comment = version?.GenreName,
            AlbumArt = img,
        };

        if (!AudioConvert.TryResolveAcbAwb(GetAudioCandidateIds(music), out _, out var acbPath, out var awbPath) || acbPath is null || awbPath is null)
        {
            var message = BuildAudioResolveErrorMessage(music);
            logger.LogError("{message}", message);
            throw new FileNotFoundException(message);
        }
        var wav = Audio.AcbToWav(acbPath);
        var mp3 = AudioConvert.ConvertWavToMp3(wav, tag);
        
        // 导出视频
        byte[]? video = null;
        if (!ignoreVideo && StaticSettings.MovieDataMap.TryGetValue(music.NonDxId, out var movieUsmPath))
        {
            DirectoryInfo? tmpDir = null;
            try
            {
                string? pvMp4Path = null;
                var ext = Path.GetExtension(movieUsmPath).ToLowerInvariant();

                if (ext == ".dat" || ext == ".usm")
                {
                    tmpDir = Directory.CreateTempSubdirectory();
                    logger.LogInformation("Temp dir: {tmpDir}", tmpDir.FullName);
                    pvMp4Path = Path.Combine(tmpDir.FullName, "pv.mp4");

                    await VideoConvert.ConvertUsmToMp4(movieUsmPath, pvMp4Path);
                }
                else if (ext == ".mp4")
                {
                    pvMp4Path = movieUsmPath;
                }

                if (pvMp4Path is not null && System.IO.File.Exists(pvMp4Path))
                {
                    video = await System.IO.File.ReadAllBytesAsync(pvMp4Path);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "导出音乐 {musicId}（{name}）的 pv.mp4 失败，跳过视频。", music.Id, music.Name);
            }
            finally
            {
                if (tmpDir is not null)
                {
                    try
                    {
                        tmpDir.Delete(true);
                    }
                    catch
                    {
                        // 忽略清理错误
                    }
                }
            }
        }

        return (simaiFile, mp3, img, imgExt, video);
    }

    private async Task ConvertToSimai(MusicXmlWithABJacket music, MusicXml.Chart chart, string chartPath, Maidata simaiFile, int simaiLevelId)
    {
        try
        {
            var ma2Content = await System.IO.File.ReadAllTextAsync(chartPath);
            var (cvtChart, _) = new MA2Parser().Parse(ma2Content);
            var (simai, _) = new SimaiGenerator().Generate(cvtChart);

            var lvStr = $"{chart.Level}.{chart.LevelDecimal}";
            simaiFile.AddLevel(simaiLevelId, new MaidataLevel(simai, lvStr, chart.Designer));
            simaiFile.ClockCount = cvtChart.ClockCount; // 通过多次写入，自然实现取最后一个有效难度的clockCount，作为写入maidata中的
        }
        catch (Exception e)
        {
            logger.LogError("ExportAsMaidata FAILED! {title}, {filename}: {e}", music.Name, chartPath, e);
            throw;
        }
    }

    // 把单首歌导出为 maidata 文件（maidata.txt + 封面 + 音频）写入 targetDir。
    // 该方法供原生选目录导出复用；与 zip 版 ExportAsMaidata 产物保持一致。
    private async Task WriteMaidataToDirectory(int id, string assetDir, string targetDir, bool ignoreVideo)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music is null) return;
        Directory.CreateDirectory(targetDir);
        var (simaiFile, track, img, imgExt, video) = await _exportAsMaidata(music, ignoreVideo);

        await System.IO.File.WriteAllTextAsync(Path.Combine(targetDir, "maidata.txt"), simaiFile.ToString(), Encoding.UTF8);

        if (img is not null)
        {
            await System.IO.File.WriteAllBytesAsync(Path.Combine(targetDir, $"bg{imgExt}"), img);
        }

        await System.IO.File.WriteAllBytesAsync(Path.Combine(targetDir, "track.mp3"), track);

        if (video is not null)
        {
            await System.IO.File.WriteAllBytesAsync(Path.Combine(targetDir, "pv.mp4"), video);
        }
    }

    // 把文件名中的非法字符替换为下划线，空结果回退到 fallback。
    // treatDotAsInvalid 为 true 时，`.` 也视为非法字符。流派名和版本名本身不含句点，
    // 用来避免 "." / ".." 被 Path.Combine 解析到目标目录之外；歌名不传此参数，以免改掉正常句点。
    private static string SanitizeFileNameSegment(string name, string fallback, bool treatDotAsInvalid = false)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            var invalid = Array.IndexOf(invalidChars, ch) >= 0 || (treatDotAsInvalid && ch == '.');
            builder.Append(invalid ? '_' : ch);
        }

        var result = builder.ToString().Trim();
        return string.IsNullOrEmpty(result) ? fallback : result;
    }

    // 原生选目录导出 maidata：弹出系统目录选择对话框，对每首歌在子目录中写入 maidata。
    // 用于没有 File System Access API 的环境（Linux/Photino/WebKitGTK）。
    [HttpPost]
    [Route("/MaiChartManagerServlet/[action]Api")]
    public async Task RequestExportMaidata([FromBody] RequestExportMaidataRequest request)
    {
        var dest = dialogService.PickFolder(Locale.SelectTargetLocation);
        if (dest is null) return;
        logger.LogInformation("ExportMaidata: {dest}", dest);

        if (request.music.Length == 0) return;

        var showProgress = request.music.Length > 1;
        using var progress = showProgress
            ? progressController.Begin(Locale.Exporting, string.Format(Locale.ExportingMultipleMusic, request.music.Length), Locale.Cancelling)
            : null;
        progress?.Report(0, (ulong)request.music.Length);

        // 记录已使用的子目录名，避免不同歌曲互相覆盖
        var usedDirNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var done = 0;
        var total = request.music.Length;

        foreach (var musicId in request.music)
        {
            if (progress?.IsCancelled == true)
            {
                logger.LogInformation("批量导出 maidata 被用户取消。");
                break;
            }

            var music = settings.GetMusic(musicId.Id, musicId.AssetDir);
            if (music is null)
            {
                logger.LogWarning("Skip export: music {id} in {assetDir} not found.", musicId.Id, musicId.AssetDir);
                done++;
                progress?.Report((ulong)done, (ulong)total);
                continue;
            }

            try
            {
                // 一级分组目录（与前端 remoteExport 的 selectedMaidataSubdir 对齐）
                string? parentDir = request.subdir switch
                {
                    MaidataSubdirMode.Genre => SanitizeFileNameSegment(
                        StaticSettings.GenreList.FirstOrDefault(it => it.Id == music.GenreId)?.GenreName ?? "", "Unknown", treatDotAsInvalid: true),
                    MaidataSubdirMode.Version => SanitizeFileNameSegment(
                        StaticSettings.VersionList.FirstOrDefault(it => it.Id == music.AddVersionId)?.GenreName ?? "", "Unknown", treatDotAsInvalid: true),
                    _ => null,
                };

                // 叶子目录：byId 用 id；否则安全化歌名 + DX 后缀（与前端 remoteExport 保持一致）
                string baseName;
                if (request.byId) baseName = music.Id.ToString();
                else
                {
                    var suffix = music.Id is > 10000 and < 20000 ? " [DX]" : "";
                    baseName = SanitizeFileNameSegment(music.Name ?? "", music.Id.ToString()) + suffix;
                }

                // 处理重名：追加 id，再不行追加序号
                var dirName = parentDir is null ? baseName : Path.Combine(parentDir, baseName);
                if (!usedDirNames.Add(dirName))
                {
                    dirName += $"_{music.Id}";
                    var n = 1;
                    while (!usedDirNames.Add(dirName))
                    {
                        dirName += $"_{n++}";
                    }
                }

                var targetDir = Path.Combine(dest, dirName);
                await WriteMaidataToDirectory(musicId.Id, musicId.AssetDir, targetDir, request.ignoreVideo);
            }
            catch (Exception e)
            {
                logger.LogError(e, "导出音乐 {id}（{name}）的 maidata 失败，已跳过。", music.Id, music.Name);
            }

            done++;
            progress?.Report((ulong)done, (ulong)total, music.Name);
        }
    }

    [GeneratedRegex(@"VERSION\t0.00.00\t1.(\d\d).00")]
    private static partial Regex MA2VersionRegex();
}
