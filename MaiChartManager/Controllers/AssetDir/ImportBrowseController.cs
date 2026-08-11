using System.Net;
using MaiChartManager.Platform;
using Microsoft.AspNetCore.Mvc;

namespace MaiChartManager.Controllers.AssetDir;

// maidata 导入用的后端目录浏览接口。
// 背景：WebKitGTK（Linux/Photino）既没有 showDirectoryPicker，<input webkitdirectory> 也只能选单文件，
// 所以前端没法在浏览器侧拿到目录内容。改为：后端弹原生选文件夹对话框，再通过下面 3 个接口
// 把所选目录的内容提供给前端的 ImportDirectory 适配器（见 Front/src/utils/httpImportDirectory.ts）。
//
// 安全说明：这些接口可以读取任意本地路径，仅限 loopback 连接使用。
// 授权必须依据实际连接来源，不能依赖可变的 export 配置，否则模式切换期间会产生竞态窗口。
[ApiController]
[Route("MaiChartManagerServlet/[action]Api")]
public class ImportBrowseController(IDesktopDialogService dialogService, ILogger<ImportBrowseController> logger) : ControllerBase
{
    private bool IsLoopbackRequest()
        => HttpContext.Connection.RemoteIpAddress is { } remoteIp && IPAddress.IsLoopback(remoteIp);

    // 子项列表的返回结构：name 显示名，path 子项绝对路径，isDirectory 是否为目录
    public record ImportDirEntry(string Name, string Path, bool IsDirectory);

    // 弹原生选文件夹对话框，返回选中的绝对路径；取消返回 null
    [HttpGet]
    public ActionResult<string?> PickImportFolder()
    {
        if (!IsLoopbackRequest()) return StatusCode(StatusCodes.Status403Forbidden);
        var path = dialogService.PickFolder();
        logger.LogInformation("PickImportFolder: {path}", path);
        // 取消时 PickFolder 返回 null，这里原样返回（前端按取消处理）
        return Ok(path);
    }

    // 列出目录下的直接子项（不递归）。path 不存在或不是目录时返回空数组。
    [HttpGet]
    public ActionResult<IEnumerable<ImportDirEntry>> ListImportDir([FromQuery] string path)
    {
        if (!IsLoopbackRequest()) return StatusCode(StatusCodes.Status403Forbidden);
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            return Ok(Array.Empty<ImportDirEntry>());
        }

        var result = new List<ImportDirEntry>();
        // 子目录
        foreach (var dir in Directory.EnumerateDirectories(path))
        {
            result.Add(new ImportDirEntry(Path.GetFileName(dir), dir, true));
        }
        // 子文件
        foreach (var file in Directory.EnumerateFiles(path))
        {
            result.Add(new ImportDirEntry(Path.GetFileName(file), file, false));
        }
        return Ok(result);
    }

    // 读取文件内容。
    // 支持两种调用方式（解决跨平台路径分隔符问题）：
    //   ReadImportFileApi?path=<文件完整路径>          → 直接读 path
    //   ReadImportFileApi?path=<目录>&name=<文件名>     → 后端 Path.Combine(path, name) 后再读
    // 这样前端 getFileHandle 不用自己拼路径。文件不存在返回 404。
    [HttpGet]
    public IActionResult ReadImportFile([FromQuery] string path, [FromQuery] string? name = null)
    {
        if (!IsLoopbackRequest()) return StatusCode(StatusCodes.Status403Forbidden);
        var fullPath = string.IsNullOrEmpty(name) ? path : Path.Combine(path, name);
        if (string.IsNullOrEmpty(fullPath) || !System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }
        return PhysicalFile(Path.GetFullPath(fullPath), "application/octet-stream");
    }
}
