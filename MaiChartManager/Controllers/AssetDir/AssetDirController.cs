using MaiChartManager.Attributes;
using MaiChartManager.Platform;
using MaiChartManager.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace MaiChartManager.Controllers.AssetDir;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api")]
public class AssetDirController(StaticSettings settings, ILogger<AssetDirController> logger, IDesktopDialogService dialogService) : ControllerBase
{
    [HttpPost]
    public void CreateAssetDir([FromBody] string dir)
    {
        Directory.CreateDirectory(Path.Combine(StaticSettings.StreamingAssets, dir));
    }

    [HttpDelete]
    public void DeleteAssetDir([FromBody] string dir)
    {
        PlatformFile.DeleteDirectory(Path.Combine(StaticSettings.StreamingAssets, dir), showDialog: true);
    }

    public record GetAssetsDirsResult(string DirName, IEnumerable<string> SubFiles, string Version);

    [NonAction]
    public string VersionDisplay(int major, int minor, int release)
    {
        var baseVersion = $"{major}.{minor}";
        if (release != 0)
        {
            baseVersion += "-" + ReleaseToLetters(release);
        }
        return baseVersion;
    }

    [NonAction]
    private string ReleaseToLetters(int release)
    {
        var result = "";
        while (release > 0)
        {
            release--;
            result = (char)('A' + release % 26) + result;
            release /= 26;
        }
        return result;
    }

    [HttpGet]
    public IEnumerable<GetAssetsDirsResult> GetAssetsDirs()
    {
        foreach (var path in StaticSettings.AssetsDirs)
        {
            var version = "";
            var dataConfig = Path.Combine(StaticSettings.StreamingAssets, path, "DataConfig.xml");
            if (System.IO.File.Exists(dataConfig))
            {
                try
                {
                    var doc = new System.Xml.XmlDocument();
                    doc.Load(dataConfig);
                    var versionNode = doc.SelectSingleNode("/DataConfig/version");
                    if (versionNode is not null)
                    {
                        var major = int.Parse(versionNode.SelectSingleNode("major")!.InnerText);
                        var minor = int.Parse(versionNode.SelectSingleNode("minor")!.InnerText);
                        var release = int.Parse(versionNode.SelectSingleNode("release")!.InnerText);
                        version = VersionDisplay(major, minor, release);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to read version from {dataConfig}", dataConfig);
                }
            }
            yield return new GetAssetsDirsResult(path, Directory.EnumerateFiles(Path.Combine(StaticSettings.StreamingAssets, path)).Select(Path.GetFileName)!, version);
        }
    }

    public record GetAssetDirTxtValueRequest(string DirName, string FileName);

    [HttpPost]
    public string GetAssetDirTxtValue([FromBody] GetAssetDirTxtValueRequest req)
    {
        return System.IO.File.ReadAllText(Path.Combine(StaticSettings.StreamingAssets, req.DirName, req.FileName));
    }

    [HttpDelete]
    public void DeleteAssetDirTxt([FromBody] GetAssetDirTxtValueRequest req)
    {
        PlatformFile.DeleteFile(Path.Combine(StaticSettings.StreamingAssets, req.DirName, req.FileName));
    }

    public record PutAssetDirTxtValueRequest(string DirName, string FileName, string Content);

    [HttpPut]
    public void PutAssetDirTxtValue([FromBody] PutAssetDirTxtValueRequest req)
    {
        System.IO.File.WriteAllText(Path.Combine(StaticSettings.StreamingAssets, req.DirName, req.FileName), req.Content);
    }

    [HttpPost]
    public async Task RequestLocalImportDir()
    {
        var src = dialogService.PickFolder(Locale.SelectAssetDirectory);
        if (src is null) return;
        logger.LogInformation("LocalImportDir: {src}", src);
        var destName = Path.GetFileName(src);
        if (!StaticSettings.ADirRegex().IsMatch(destName))
        {
            var maybeRealDir = Directory.EnumerateDirectories(src).FirstOrDefault(it => StaticSettings.ADirRegex().IsMatch(Path.GetFileName(it)));
            if (maybeRealDir is not null)
            {
                src = maybeRealDir;
                destName = Path.GetFileName(src);
            }
        }

        if (!StaticSettings.ADirRegex().IsMatch(destName) || StaticSettings.AssetsDirs.Contains(destName))
        {
            destName = settings.GetFreeAssetDir();
        }

        var dest = Path.Combine(StaticSettings.StreamingAssets, destName);
        logger.LogInformation("Src: {src} Dest: {dest}", src, dest);
        PlatformFile.CopyDirectory(src, dest);
        await settings.RescanAll();
    }

    public record UploadAssetDirResult(string DirName);

    [HttpPost]
    // https://code-maze.com/aspnetcore-upload-large-files/
    [DisableRequestSizeLimit]
    [DisableFormValueModelBinding]
    [Route("{destName}")]
    // 看起来就算用 IAsyncEnumerable 获取文件，还是会等所以文件都上传完了再调用这个方法
    // 而且不知道为什么上传到一半会 Network Error
    public async Task<UploadAssetDirResult> UploadAssetDir(string? destName)
    {
        logger.LogInformation("UploadAssetDir");

        if (destName is null || !StaticSettings.ADirRegex().IsMatch(destName) || StaticSettings.AssetsDirs.Contains(destName))
        {
            destName = settings.GetFreeAssetDir();
        }

        var dest = Path.Combine(StaticSettings.StreamingAssets, destName);

        // https://stackoverflow.com/questions/36437282/dealing-with-large-file-uploads-on-asp-net-core-1-0
        var boundary = HeaderUtilities.RemoveQuotes(MediaTypeHeaderValue.Parse(Request.ContentType).Boundary).Value;
        var reader = new MultipartReader(boundary!, Request.Body);
        var section = await reader.ReadNextSectionAsync();

        while (section != null)
        {
            if (ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var contentDisposition))
            {
                if (contentDisposition.DispositionType.Equals("form-data") && !string.IsNullOrEmpty(contentDisposition.FileName.Value))
                {
                    var fileName = contentDisposition.FileName.Value;
                    await using var stream = section.Body;
                    // 处理文件流
                    logger.LogInformation("UploadAssetDir: {destName} {file}", destName, fileName);
                    var filePath = Path.Combine(dest, fileName.TrimStart('/', '\\'));
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    await using var fileStream = new FileStream(filePath, FileMode.Create);
                    await stream.CopyToAsync(fileStream);
                }
            }

            section = await reader.ReadNextSectionAsync();
        }

        await settings.RescanAll();

        return new UploadAssetDirResult(destName);
    }
}