using System.ComponentModel;
using MaiChartManager.Models;
using MaiChartManager.Platform;
using Microsoft.AspNetCore.Mvc;

namespace MaiChartManager.Controllers.AssetDir;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api")]
public class CheckConflictController(StaticSettings settings, ILogger<CheckConflictController> logger) : ControllerBase
{
    public enum AssetType
    {
        Music,
        Movie
    }

    public record CheckConflictEntry(AssetType Type, string UpperDir, string LowerDir, string FileName, int MusicId, string MusicName);

    [HttpPost]
    public IEnumerable<CheckConflictEntry> CheckConflict([FromBody] string dir)
    {
        var assetDir = Path.Combine(StaticSettings.StreamingAssets, dir);
        var musicDir = Path.Combine(assetDir, "music");

        if (!Directory.Exists(musicDir))
        {
            return [];
        }

        var musicList = new List<MusicXmlWithABJacket>();
        foreach (var subDir in Directory.EnumerateDirectories(musicDir))
        {
            if (!System.IO.File.Exists(Path.Combine(subDir, "Music.xml"))) continue;
            var musicXml = new MusicXmlWithABJacket(Path.Combine(subDir, "Music.xml"), StaticSettings.GamePath, dir);
            musicList.Add(musicXml);
        }

        var allSoundAssets = new Dictionary<string, string[]>();
        var allMovieAssets = new Dictionary<string, string[]>();
        foreach (var aDir in StaticSettings.AssetsDirs)
        {
            if (aDir.Equals(dir, StringComparison.InvariantCultureIgnoreCase)) continue;
            var path = Path.Combine(StaticSettings.StreamingAssets, aDir);
            var soundDataDir = Path.Combine(path, "SoundData");
            var movieDataDir = Path.Combine(path, "MovieData");

            if (Directory.Exists(soundDataDir))
            {
                allSoundAssets[aDir] = Directory.EnumerateFiles(soundDataDir).Select(it => Path.GetFileName(it).ToLowerInvariant()).ToArray();
            }

            if (Directory.Exists(movieDataDir))
            {
                allMovieAssets[aDir] = Directory.EnumerateFiles(movieDataDir).Select(it => Path.GetFileName(it).ToLowerInvariant()).ToArray();
            }
        }

        var conflictList = new List<CheckConflictEntry>();
        foreach (var music in musicList)
        {
            string? alreadyExistSoundADir = null;
            string? alreadyExistMovieADir = null;
            foreach (var (aDir, filenames) in allSoundAssets)
            {
                if (alreadyExistSoundADir is null && (filenames.Contains($"music{music.NonDxId:000000}.acb") || filenames.Contains($"music{music.NonDxId:000000}.awb")))
                {
                    alreadyExistSoundADir = aDir;
                    continue;
                }

                if (alreadyExistSoundADir is null) continue;
                if (filenames.Contains($"music{music.NonDxId:000000}.acb"))
                {
                    conflictList.Add(new CheckConflictEntry(AssetType.Music, aDir, alreadyExistSoundADir, $"music{music.NonDxId:000000}.acb", music.Id, music.Name));
                }

                if (filenames.Contains($"music{music.NonDxId:000000}.awb"))
                {
                    conflictList.Add(new CheckConflictEntry(AssetType.Music, aDir, alreadyExistSoundADir, $"music{music.NonDxId:000000}.awb", music.Id, music.Name));
                }
            }

            foreach (var (aDir, filenames) in allMovieAssets)
            {
                if (alreadyExistMovieADir is null && filenames.Contains($"{music.NonDxId:000000}.dat"))
                {
                    alreadyExistMovieADir = aDir;
                    continue;
                }

                if (alreadyExistMovieADir is null) continue;
                if (filenames.Contains($"{music.NonDxId:000000}.dat"))
                {
                    conflictList.Add(new CheckConflictEntry(AssetType.Movie, aDir, alreadyExistMovieADir, $"{music.NonDxId:000000}.dat", music.Id, music.Name));
                }
            }
        }

        return conflictList;
    }

    public record DeleteAssetRequest(string AssetDir, AssetType Type, string FileName);

    [HttpDelete]
    public void DeleteAssets([FromBody] DeleteAssetRequest[] requests)
    {
        foreach (var req in requests)
        {
            var path = Path.Combine(StaticSettings.StreamingAssets, req.AssetDir);
            path = req.Type switch
            {
                AssetType.Music => Path.Combine(path, "SoundData"),
                AssetType.Movie => Path.Combine(path, "MovieData"),
                _ => throw new InvalidEnumArgumentException()
            };

            logger.LogInformation("Delete file {path}", Path.Combine(path, req.FileName));
            PlatformFile.DeleteFile(Path.Combine(path, req.FileName));
        }
    }
}
