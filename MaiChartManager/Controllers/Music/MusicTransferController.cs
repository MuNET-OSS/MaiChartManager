using System.IO.Compression;
using System.Text;
using MaiChartManager.Utils;
using MaiLib;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualBasic.FileIO;
using NAudio.Lame;
using SimaiSharp;
using Vanara.Windows.Forms;
using Xabe.FFmpeg;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;

namespace MaiChartManager.Controllers.Music;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api/{assetDir}/{id:int}")]
public class MusicTransferController(StaticSettings settings, ILogger<MusicTransferController> logger) : ControllerBase
{
    public record RequestCopyToRequest(MusicBatchController.MusicIdAndAssetDirPair[] music, bool removeEvents, bool legacyFormat);

    [HttpPost]
    [Route("/MaiChartManagerServlet/[action]Api")]
    public void RequestCopyTo(RequestCopyToRequest request)
    {
        if (AppMain.BrowserWin is null) return;
        var dialog = new FolderBrowserDialog
        {
            Description = "请选择目标位置"
        };
        if (AppMain.BrowserWin.Invoke(() => dialog.ShowDialog(AppMain.BrowserWin)) != DialogResult.OK) return;
        var dest = dialog.SelectedPath;
        logger.LogInformation("CopyTo: {dest}", dest);

        ShellProgressDialog? progress = null;
        if (request.music.Length > 1)
        {
            progress = new ShellProgressDialog()
            {
                AutoTimeEstimation = false,
                Title = "正在导出…",
                Description = $"正在导出 {request.music.Length} 首乐曲…",
                CancelMessage = "正在取消…",
                HideTimeRemaining = true,
            };
            progress.Start(AppMain.BrowserWin);
            progress.UpdateProgress(0, (ulong)request.music.Length);
        }

        for (var i = 0; i < request.music.Length; i++)
        {
            var musicId = request.music[i];
            var music = settings.GetMusic(musicId.Id, musicId.AssetDir);
            if (music is null) continue;
            if (progress?.IsCancelled ?? false)
            {
                break;
            }

            if (progress is not null)
            {
                progress.Detail = music.Name;
                progress.UpdateProgress((ulong)i, (ulong)request.music.Length);
            }

            // copy music
            Directory.CreateDirectory(Path.Combine(dest, "music"));
            FileSystem.CopyDirectory(Path.GetDirectoryName(music.FilePath), Path.Combine(dest, $@"music\music{music.Id:000000}"), UIOption.OnlyErrorDialogs);

            if (request.removeEvents)
            {
                var xmlDoc = music.GetXmlWithoutEventsAndRights();
                xmlDoc.Save(Path.Combine(dest, $@"music\music{music.Id:000000}\Music.xml"));
            }

            if (request.legacyFormat)
            {
                foreach (var file in Directory.EnumerateFiles(Path.Combine(dest, $@"music\music{music.Id:000000}"), "*.ma2", new EnumerationOptions() { MatchCasing = MatchCasing.CaseInsensitive }))
                {
                    var ma2 = System.IO.File.ReadAllLines(file);
                    var ma2_103 = new Ma2Parser().ChartOfToken(ma2).Compose(ChartEnum.ChartVersion.Ma2_103);
                    System.IO.File.WriteAllText(file, ma2_103);
                }
            }

            // copy jacket
            Directory.CreateDirectory(Path.Combine(dest, @"AssetBundleImages\jacket"));
            if (music.JacketPath is not null)
            {
                FileSystem.CopyFile(music.JacketPath, Path.Combine(dest, $@"AssetBundleImages\jacket\ui_jacket_{music.NonDxId:000000}{Path.GetExtension(music.JacketPath)}"),
                    UIOption.OnlyErrorDialogs);
            }
            else if (music.AssetBundleJacket is not null)
            {
                FileSystem.CopyFile(music.AssetBundleJacket, Path.Combine(dest, $@"AssetBundleImages\jacket\{Path.GetFileName(music.AssetBundleJacket)}"), UIOption.OnlyErrorDialogs);
                if (System.IO.File.Exists(music.AssetBundleJacket + ".manifest"))
                {
                    FileSystem.CopyFile(music.AssetBundleJacket + ".manifest", Path.Combine(dest, $@"AssetBundleImages\jacket\{Path.GetFileName(music.AssetBundleJacket)}.manifest"),
                        UIOption.OnlyErrorDialogs);
                }
            }
            else if (music.PseudoAssetBundleJacket is not null)
            {
                FileSystem.CopyFile(music.PseudoAssetBundleJacket, Path.Combine(dest, $@"AssetBundleImages\jacket\{Path.GetFileName(music.PseudoAssetBundleJacket)}"), UIOption.OnlyErrorDialogs);
            }

            // copy acbawb
            Directory.CreateDirectory(Path.Combine(dest, "SoundData"));
            if (StaticSettings.AcbAwb.TryGetValue($"music{music.NonDxId:000000}.acb", out var acb))
            {
                FileSystem.CopyFile(acb, Path.Combine(dest, $@"SoundData\music{music.NonDxId:000000}.acb"), UIOption.OnlyErrorDialogs);
            }

            if (StaticSettings.AcbAwb.TryGetValue($"music{music.NonDxId:000000}.awb", out var awb))
            {
                FileSystem.CopyFile(awb, Path.Combine(dest, $@"SoundData\music{music.NonDxId:000000}.awb"), UIOption.OnlyErrorDialogs);
            }

            // copy movie data
            if (StaticSettings.MovieDataMap.TryGetValue(music.NonDxId, out var movie))
            {
                Directory.CreateDirectory(Path.Combine(dest, "MovieData"));
                FileSystem.CopyFile(movie, Path.Combine(dest, $@"MovieData\{music.NonDxId:000000}{Path.GetExtension(movie)}"), UIOption.OnlyErrorDialogs);
            }
        }

        progress?.Stop();
    }

    [HttpGet]
    public void ExportOpt(int id, string assetDir, bool removeEvents = false, bool legacyFormat = false)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music is null) return;

        var zipStream = HttpContext.Response.BodyWriter.AsStream();
        using var zipArchive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true);

        // copy music
        foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(music.FilePath)))
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

            if (legacyFormat)
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
        if (StaticSettings.AcbAwb.TryGetValue($"music{music.NonDxId:000000}.acb", out var acb))
        {
            zipArchive.CreateEntryFromFile(acb, $"SoundData/music{music.NonDxId:000000}.acb");
        }

        if (StaticSettings.AcbAwb.TryGetValue($"music{music.NonDxId:000000}.awb", out var awb))
        {
            zipArchive.CreateEntryFromFile(awb, $"SoundData/music{music.NonDxId:000000}.awb");
        }

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
    public void ModifyId(int id, [FromBody] int newId, string assetDir)
    {
        if (IapManager.License != IapManager.LicenseStatus.Active) return;
        var music = settings.GetMusic(id, assetDir);
        if (music is null) return;
        var newNonDxId = newId % 10000;

        var abJacketTarget = Path.Combine(StaticSettings.StreamingAssets, assetDir, "AssetBundleImages", "jacket", $"ui_jacket_{newNonDxId:000000}.ab");
        var acbawbTarget = Path.Combine(StaticSettings.StreamingAssets, assetDir, "SoundData", $"music{newNonDxId:000000}");
        var movieTarget = Path.Combine(StaticSettings.StreamingAssets, assetDir, "MovieData", $"{newNonDxId:000000}");
        var newMusicDir = Path.Combine(StaticSettings.StreamingAssets, assetDir, "music", $"music{newNonDxId:000000}");
        DeleteIfExists(abJacketTarget, abJacketTarget + ".manifest", acbawbTarget + ".acb", acbawbTarget + ".awb", movieTarget + ".dat", movieTarget + ".mp4", newMusicDir);
        var abiDir = Path.Combine(StaticSettings.GamePath, "StreamingAssets", assetDir, @"AssetBundleImages\jacket");
        Directory.CreateDirectory(abiDir);

        // jacket
        if (music.JacketPath is not null)
        {
            var localJacketTarget = Path.Combine(abiDir, $"ui_jacket_{newNonDxId:000000}{Path.GetExtension(music.JacketPath)}");
            DeleteIfExists(localJacketTarget);
            logger.LogInformation("Move jacket: {music.JacketPath} -> {localJacketTarget}", music.JacketPath, localJacketTarget);
            FileSystem.MoveFile(music.JacketPath, localJacketTarget, UIOption.OnlyErrorDialogs);
        }
        else if (music.PseudoAssetBundleJacket is not null)
        {
            var localJacketTarget = Path.Combine(abiDir, $"ui_jacket_{newNonDxId:000000}{Path.GetExtension(music.PseudoAssetBundleJacket)}");
            DeleteIfExists(localJacketTarget);
            logger.LogInformation("Move jacket: {music.PseudoAssetBundleJacket} -> {localJacketTarget}", music.PseudoAssetBundleJacket, localJacketTarget);
            FileSystem.MoveFile(music.PseudoAssetBundleJacket, localJacketTarget, UIOption.OnlyErrorDialogs);
        }
        else if (music.AssetBundleJacket is not null)
        {
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
        settings.RescanAll();
    }

    [HttpGet]
    public async Task ExportAsMaidata(int id, string assetDir, bool ignoreVideo = false)
    {
        var music = settings.GetMusic(id, assetDir);
        if (music is null) return;

        await using var zipStream = HttpContext.Response.BodyWriter.AsStream();
        using var zipArchive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true);

        Ma2Parser parser = new();
        var simaiFile = new StringBuilder();

        simaiFile.AppendLine($"&title={music.Name}");
        simaiFile.AppendLine($"&artist={music.Artist}");
        simaiFile.AppendLine($"&wholebpm={music.Bpm}");
        simaiFile.AppendLine("&first=0.0333");
        simaiFile.AppendLine($"&shortid={music.Id}");
        simaiFile.AppendLine($"&genreid={music.GenreId}");
        var genre = StaticSettings.GenreList.FirstOrDefault(it => it.Id == music.GenreId);
        if (genre is not null)
            simaiFile.AppendLine($"&genre={genre.GenreName}");
        simaiFile.AppendLine($"&versionid={music.AddVersionId}");
        var version = StaticSettings.VersionList.FirstOrDefault(it => it.Id == music.AddVersionId);
        if (version is not null)
            simaiFile.AppendLine($"&version={version.GenreName}");
        simaiFile.AppendLine($"&chartconverter=MaiChartManager v{Application.ProductVersion}");
        simaiFile.AppendLine("&ChartConvertTool=MaiChartManager");
        simaiFile.AppendLine($"&ChartConvertToolVersion={Application.ProductVersion}");

        for (var i = 0; i < 5; i++)
        {
            var chart = music.Charts[i];
            if (chart is null) continue;

            var path = Path.Combine(Path.GetDirectoryName(music.FilePath)!, chart.Path);
            if (!System.IO.File.Exists(path)) continue;
            var ma2Content = await System.IO.File.ReadAllLinesAsync(path);
            var ma2 = parser.ChartOfToken(ma2Content);
            var simai = ma2.Compose(ChartEnum.ChartVersion.SimaiFes);
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
            var imgExt = (Path.GetExtension(music.JacketPath ?? music.PseudoAssetBundleJacket ?? music.AssetBundleJacket) ?? ".png").ToLowerInvariant();
            if (imgExt == ".ab") imgExt = ".png";
            var imageEntry = zipArchive.CreateEntry($"bg{imgExt}");
            await using var imageStream = imageEntry.Open();
            await imageStream.WriteAsync(img);
            imageStream.Close();
        }

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
        AudioConvert.ConvertWavPathToMp3Stream(await AudioConvert.GetCachedWavPath(id), soundStream, tag);
        soundStream.Close();

        if (!ignoreVideo && StaticSettings.MovieDataMap.TryGetValue(music.NonDxId, out var movieUsmPath))
        {
            string? pvMp4Path = null;
            if (Path.GetExtension(movieUsmPath).Equals(".dat", StringComparison.InvariantCultureIgnoreCase))
            {
                var tmpDir = Directory.CreateTempSubdirectory();
                logger.LogInformation("Temp dir: {tmpDir}", tmpDir.FullName);
                var movieUsm = Path.Combine(tmpDir.FullName, "movie.usm");
                FileSystem.CopyFile(movieUsmPath, movieUsm, UIOption.OnlyErrorDialogs);
                WannaCRI.WannaCRI.UnpackUsm(movieUsm, Path.Combine(tmpDir.FullName, "output"));
                var outputIvfFile = Directory.EnumerateFiles(Path.Combine(tmpDir.FullName, @"output\movie.usm\videos")).FirstOrDefault();
                if (outputIvfFile is not null)
                {
                    pvMp4Path = Path.Combine(tmpDir.FullName, "pv.mp4");
                    await FFmpeg.Conversions.New()
                        .AddParameter("-i " + outputIvfFile.Escape())
                        .AddParameter("-c:v copy")
                        .SetOutput(pvMp4Path)
                        .Start();
                }
            }
            else if (Path.GetExtension(movieUsmPath).Equals(".mp4", StringComparison.InvariantCultureIgnoreCase))
            {
                pvMp4Path = movieUsmPath;
            }

            if (pvMp4Path is not null)
                zipArchive.CreateEntryFromFile(pvMp4Path, "pv.mp4");
        }
    }
}