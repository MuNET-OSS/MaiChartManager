using System.IO.Compression;
using System.Collections.Concurrent;
using System.Text;
using System.Security.Cryptography;
using MaiChartManager.Models;
using MaiChartManager.Utils;
using MaiLib;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualBasic.FileIO;
using NAudio.Lame;
using Vanara.Windows.Forms;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;

namespace MaiChartManager.Controllers.Music;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api/{assetDir}/{id:int}")]
public class MusicTransferController(StaticSettings settings, ILogger<MusicTransferController> logger) : ControllerBase
{
    public record RequestCopyToRequest(MusicBatchController.MusicIdAndAssetDirPair[] music, bool removeEvents, bool legacyFormat);

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

        // copy music
        var musicDestDir = Path.Combine(musicRootDir, $"music{music.Id:000000}");
        CopyDirectoryIfChanged(musicDir, musicDestDir);

        if (removeEvents)
        {
            var xmlDoc = music.GetXmlWithoutEventsAndRights();
            xmlDoc.Save(Path.Combine(musicDestDir, "Music.xml"));
        }

        if (legacyFormat)
        {
            var parser = new Ma2Parser();
            foreach (var file in Directory.EnumerateFiles(musicDestDir, "*.ma2", new EnumerationOptions()
            {
                MatchCasing = MatchCasing.CaseInsensitive
            }))
            {
                var originalContent = System.IO.File.ReadAllText(file);
                var ma2Lines = new List<string>();
                using (var reader = new StringReader(originalContent))
                {
                    string? line;
                    while ((line = reader.ReadLine()) is not null)
                    {
                        ma2Lines.Add(line);
                    }
                }

                var ma2_103 = parser.ChartOfToken(ma2Lines.ToArray()).Compose(ChartEnum.ChartVersion.Ma2_103);
                if (!string.Equals(originalContent, ma2_103, StringComparison.Ordinal))
                {
                    System.IO.File.WriteAllText(file, ma2_103);
                }
            }
        }

        // copy jacket
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
        }
        else if (music.PseudoAssetBundleJacket is not null)
        {
            var jacketFileName = Path.GetFileName(music.PseudoAssetBundleJacket);
            CopySharedFileIfNeeded(music.PseudoAssetBundleJacket, Path.Combine(jacketRootDir, jacketFileName), copiedSharedDestinations);
        }

        // copy acbawb
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

        // copy movie data
        if (StaticSettings.MovieDataMap.TryGetValue(music.NonDxId, out var movie))
        {
            CopySharedFileIfNeeded(movie, Path.Combine(movieRootDir, $"{music.NonDxId:000000}{Path.GetExtension(movie)}"), copiedSharedDestinations);
        }
    }

    [HttpPost]
    [Route("/MaiChartManagerServlet/[action]Api")]
    public void RequestCopyTo(RequestCopyToRequest request)
    {
        var dialog = new FolderBrowserDialog
        {
            Description = Locale.SelectTargetLocation
        };
        if (WinUtils.ShowDialog(dialog) != DialogResult.OK) return;
        var dest = dialog.SelectedPath;
        logger.LogInformation("CopyTo: {dest}", dest);

        ShellProgressDialog? progress = null;
        if (request.music.Length > 1)
        {
            progress = new ShellProgressDialog()
            {
                AutoTimeEstimation = false,
                Title = Locale.Exporting,
                Description = string.Format(Locale.ExportingMultipleMusic, request.music.Length),
                CancelMessage = Locale.Cancelling,
                HideTimeRemaining = true,
            };
            progress.Start(AppMain.BrowserWin!);
            progress.UpdateProgress(0, (ulong)request.music.Length);
        }

        if (request.music.Length == 0)
        {
            progress?.Stop();
            return;
        }

        var musicRootDir = Path.Combine(dest, "music");
        var jacketRootDir = Path.Combine(dest, @"AssetBundleImages\jacket");
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

        var cancellation = new CancellationTokenSource();
        var progressLock = new object();
        var completed = 0;
        var maxConcurrency = GetBatchExportMaxConcurrency();
        var progressStep = Math.Max(1, request.music.Length / 100);
        var copiedSharedDestinations = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            Parallel.ForEach(request.music, new ParallelOptions
            {
                MaxDegreeOfParallelism = maxConcurrency,
                CancellationToken = cancellation.Token
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
                        if (currentMusicName is not null)
                        {
                            progress.Detail = currentMusicName;
                        }

                        progress.UpdateProgress((ulong)done, (ulong)request.music.Length);
                    }
                }
            });
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Batch export cancelled by user.");
        }
        finally
        {
            progress?.Stop();
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

        // copy music
        foreach (var file in Directory.EnumerateFiles(musicDir))
        {
            if (Path.GetFileName(file).Equals("Music.xml", StringComparison.InvariantCultureIgnoreCase) && removeEvents)
            {
                logger.LogInformation("Remove events and rights from Music.xml");
                var xmlDoc = music.GetXmlWithoutEventsAndRights();
                var entry = zipArchive.CreateEntry($"music/music{music.Id:000000}/Music.xml");
                using var stream = entry.Open();
                xmlDoc.Save(stream);
                continue;
            }

            if (legacyFormat && Path.GetExtension(file).Equals(".ma2", StringComparison.InvariantCultureIgnoreCase))
            {
                var ma2 = System.IO.File.ReadAllLines(file);
                var ma2_103 = new Ma2Parser().ChartOfToken(ma2).Compose(ChartEnum.ChartVersion.Ma2_103);
                var entry = zipArchive.CreateEntry($"music/music{music.Id:000000}/{Path.GetFileName(file)}");
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream);
                writer.Write(ma2_103);
                writer.Close();
            }
            else
            {
                zipArchive.CreateEntryFromFile(file, $"music/music{music.Id:000000}/{Path.GetFileName(file)}");
            }
        }

        // copy jacket
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
        }
        else if (music.PseudoAssetBundleJacket is not null)
        {
            zipArchive.CreateEntryFromFile(music.PseudoAssetBundleJacket, $"AssetBundleImages/jacket/{Path.GetFileName(music.PseudoAssetBundleJacket)}");
        }

        // copy acbawb
        if (!AudioConvert.TryResolveAcbAwb(GetAudioCandidateIds(music), out var resolvedAudioId, out var acb, out var awb) || acb is null || awb is null)
        {
            var message = BuildAudioResolveErrorMessage(music);
            logger.LogError("{message}", message);
            throw new FileNotFoundException(message);
        }
        zipArchive.CreateEntryFromFile(acb, $"SoundData/music{resolvedAudioId:000000}.acb");
        zipArchive.CreateEntryFromFile(awb, $"SoundData/music{resolvedAudioId:000000}.awb");

        // copy movie data
        if (StaticSettings.MovieDataMap.TryGetValue(music.NonDxId, out var movie))
        {
            zipArchive.CreateEntryFromFile(movie, $"MovieData/{music.NonDxId:000000}{Path.GetExtension(movie)}");
        }
    }

    private void DeleteIfExists(params string[] path)
    {
        foreach (var p in path)
        {
            if (Directory.Exists(p))
            {
                logger.LogInformation("Delete directory: {p}", p);
                FileSystem.DeleteDirectory(p, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }

            if (System.IO.File.Exists(p))
            {
                logger.LogInformation("Delete file: {p}", p);
                FileSystem.DeleteFile(p, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
        }
    }

    [HttpPost]
    public async Task ModifyId(int id, [FromBody] int newId, string assetDir)
    {
        if (IapManager.License != IapManager.LicenseStatus.Active) return;
        var music = settings.GetMusic(id, assetDir);
        if (music is null) return;
        var musicDir = Path.GetDirectoryName(music.FilePath);
        if (string.IsNullOrWhiteSpace(musicDir) || !Directory.Exists(musicDir))
        {
            var message = $"Invalid source directory for music {music.Id}: {music.FilePath}";
            logger.LogError("{message}", message);
            throw new DirectoryNotFoundException(message);
        }
        var newNonDxId = newId % 10000;

        var abJacketTarget = Path.Combine(StaticSettings.StreamingAssets, assetDir, "AssetBundleImages", "jacket", $"ui_jacket_{newNonDxId:000000}.ab");
        var acbawbTarget = Path.Combine(StaticSettings.StreamingAssets, assetDir, "SoundData", $"music{newNonDxId:000000}");
        var movieTarget = Path.Combine(StaticSettings.StreamingAssets, assetDir, "MovieData", $"{newNonDxId:000000}");
        var newMusicDir = Path.Combine(StaticSettings.StreamingAssets, assetDir, "music", $"music{newId:000000}");
        DeleteIfExists(abJacketTarget, abJacketTarget + ".manifest", acbawbTarget + ".acb", acbawbTarget + ".awb", movieTarget + ".dat", movieTarget + ".mp4", newMusicDir);
        var abiDir = Path.Combine(StaticSettings.StreamingAssets, assetDir, @"AssetBundleImages\jacket");
        Directory.CreateDirectory(abiDir);

        // jacket
        var jacketSourcePath = music.JacketPath is not null ? music.JacketPath : music.PseudoAssetBundleJacket;
        if (jacketSourcePath is not null)
        {
            // 如果music.JacketPath或music.PseudoAssetBundleJacket不为空，则直接执行移动逻辑即可
            var localJacketTarget = Path.Combine(abiDir, $"ui_jacket_{newNonDxId:000000}{Path.GetExtension(jacketSourcePath)}");
            DeleteIfExists(localJacketTarget);
            logger.LogInformation("Move jacket: {jacketSourcePng} -> {localJacketTarget}", jacketSourcePath, localJacketTarget);
            FileSystem.MoveFile(jacketSourcePath, localJacketTarget, UIOption.OnlyErrorDialogs);
        }
        else if (music.AssetBundleJacket is not null)
        {
            // 否则需要执行转换逻辑
            var localJacketTarget = Path.Combine(abiDir, $"ui_jacket_{newNonDxId:000000}.png");
            logger.LogInformation("Convert jacket: {music.AssetBundleJacket} -> {abJacketTarget}", music.AssetBundleJacket, abJacketTarget);
            System.IO.File.WriteAllBytes(localJacketTarget, music.GetMusicJacketPngData()!);
            FileSystem.DeleteFile(music.AssetBundleJacket, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            if (System.IO.File.Exists(music.AssetBundleJacket + ".manifest"))
            {
                FileSystem.MoveFile(music.AssetBundleJacket + ".manifest", abJacketTarget + ".manifest", UIOption.OnlyErrorDialogs);
            }
        }

        // 我也不知道它需不需要重新保存，先直接移动试试
        // 是可以的
        if (StaticSettings.AcbAwb.TryGetValue($"music{music.NonDxId:000000}.acb", out var acb))
        {
            logger.LogInformation("Move acb: {acb} -> {acbawbTarget}.acb", acb, acbawbTarget);
            FileSystem.MoveFile(acb, acbawbTarget + ".acb", UIOption.OnlyErrorDialogs);
        }

        if (StaticSettings.AcbAwb.TryGetValue($"music{music.NonDxId:000000}.awb", out var awb))
        {
            logger.LogInformation("Move awb: {awb} -> {acbawbTarget}.awb", awb, acbawbTarget);
            FileSystem.MoveFile(awb, acbawbTarget + ".awb", UIOption.OnlyErrorDialogs);
        }

        // movie data
        if (StaticSettings.MovieDataMap.TryGetValue(music.NonDxId, out var movie))
        {
            logger.LogInformation("Move movie: {movie} -> {movieTarget}", movie, movieTarget);
            FileSystem.MoveFile(movie, movieTarget + Path.GetExtension(movie), UIOption.OnlyErrorDialogs);
        }

        // 谱面
        var oldMusicDir = Path.GetDirectoryName(music.FilePath)!;
        for (var i = 0; i < 6; i++)
        {
            var chart = music.Charts[i];
            if (!chart.Enable) continue;
            if (!System.IO.File.Exists(Path.Combine(oldMusicDir, chart.Path))) continue;
            var newFileName = $"{newId:000000}_0{i}.ma2";
            logger.LogInformation("Move chart: {chart.Path} -> {newFileName}", chart.Path, newFileName);
            FileSystem.MoveFile(Path.Combine(oldMusicDir, chart.Path), Path.Combine(oldMusicDir, newFileName));
            chart.Path = newFileName;
        }

        // xml
        music.Id = newId;
        music.Save();
        Directory.CreateDirectory(Path.Combine(StaticSettings.StreamingAssets, assetDir, "music"));
        logger.LogInformation("Move music dir: {oldMusicDir} -> {newMusicDir}", oldMusicDir, newMusicDir);
        FileSystem.MoveDirectory(oldMusicDir, newMusicDir, UIOption.OnlyErrorDialogs);

        // rescan all
        await settings.RescanAll();
    }

    [HttpGet]
    public async Task ExportAsMaidata(int id, string assetDir, bool ignoreVideo = false)
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

        await using var zipStream = HttpContext.Response.BodyWriter.AsStream();
        using var zipArchive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true);

        Ma2Parser parser = new();
        var simaiFile = new StringBuilder();

        simaiFile.AppendLine($"&title={music.Name}");
        simaiFile.AppendLine($"&artist={music.Artist}");
        simaiFile.AppendLine($"&wholebpm={music.Bpm}");
        simaiFile.AppendLine("&first=0");
        simaiFile.AppendLine($"&shortid={music.Id}");
        simaiFile.AppendLine($"&genreid={music.GenreId}");
        var genre = StaticSettings.GenreList.FirstOrDefault(it => it.Id == music.GenreId);
        if (genre is not null)
            simaiFile.AppendLine($"&genre={genre.GenreName}");
        simaiFile.AppendLine($"&versionid={music.AddVersionId}");
        var version = StaticSettings.VersionList.FirstOrDefault(it => it.Id == music.AddVersionId);
        if (version is not null)
            simaiFile.AppendLine($"&version={version.GenreName}");

        // demo_seek
        try
        {
            if (AudioConvert.TryResolveAcbAwb(GetAudioCandidateIds(music), out _, out var previewAcb, out _) && previewAcb is not null)
            {
                var previewTime = CriUtils.GetAudioPreviewTime(previewAcb);
                if (previewTime.StartTime >= 0 && previewTime.EndTime > previewTime.StartTime)
                {
                    simaiFile.AppendLine($"&demo_seek={previewTime.StartTime}");
                    simaiFile.AppendLine($"&demo_len={previewTime.EndTime - previewTime.StartTime}");
                }
            }
        }
        catch
        {
            // ignore preview time errors
        }

        var ma2Contents = new List<(int, string[])>();
        
        // 关于clock_count功能，我决定不走MaiLib了，而是我们自己解析。因为ma2.Compose返回的是裸谱面inote中的内容，没有办法合理的把clock信息插进去。因此，我们自己解析吧。
        // 选用最难的一张有效谱面的MET值作为全曲的&clock_count
        int clockCount = 0;

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

            var ma2Content = await System.IO.File.ReadAllLinesAsync(chartPath);
            ma2Contents.Add((i, ma2Content));

            // 从谱面内容中寻找MET行
            var metLine = ma2Content.FirstOrDefault(it => it.StartsWith("MET\t"));
            if (metLine is not null && int.TryParse(metLine.Split('\t')[4], out var v)) clockCount = v;
        }

        if (clockCount > 0) simaiFile.AppendLine($"&clock_count={clockCount}");

        simaiFile.AppendLine($"&chartconverter=MaiChartManager v{Application.ProductVersion}");
        simaiFile.AppendLine("&ChartConvertTool=MaiChartManager");
        simaiFile.AppendLine($"&ChartConvertToolVersion={Application.ProductVersion}");
        
        // 根据前面读取的结果，向simaiFile中最终写入谱面信息相关字段
        foreach (var (i, ma2Content) in ma2Contents)
        {
            var chart = music.Charts[i];
            var ma2 = parser.ChartOfToken(ma2Content);
            var simai = ma2.Compose(ChartEnum.ChartVersion.SimaiFes);
            simaiFile.AppendLine(""); // 为了格式美观，在每个难度之前添加一个空行
            simaiFile.AppendLine($"&lv_{i + 2}={chart.Level}.{chart.LevelDecimal}");
            simaiFile.AppendLine($"&des_{i + 2}={chart.Designer}");
            simaiFile.AppendLine($"&inote_{i + 2}={simai}");
        }

        var maidataEntry = zipArchive.CreateEntry("maidata.txt");
        await using var maidataStream = maidataEntry.Open();
        await maidataStream.WriteAsync(Encoding.UTF8.GetBytes(simaiFile.ToString()));
        maidataStream.Close();

        // copy jacket
        var img = music.GetMusicJacketPngData();
        if (img is not null)
        {
            var imgExt = (Path.GetExtension(music.RealJacketPath) ?? ".png").ToLowerInvariant();
            if (imgExt == ".ab") imgExt = ".png";
            var imageEntry = zipArchive.CreateEntry($"bg{imgExt}");
            await using var imageStream = imageEntry.Open();
            await imageStream.WriteAsync(img);
            imageStream.Close();
        }

        // 导出音频
        var soundEntry = zipArchive.CreateEntry("track.mp3");
        await using var soundStream = soundEntry.Open();
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
        AudioConvert.ConvertWavToMp3Stream(wav, soundStream, tag);
        soundStream.Close();

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
                    zipArchive.CreateEntryFromFile(pvMp4Path, "pv.mp4");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to export pv.mp4 for music {musicId} ({name}), skipping video.", music.Id, music.Name);
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
                        // ignore cleanup errors
                    }
                }
            }
        }
    }
}