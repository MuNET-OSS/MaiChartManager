using System.Text.RegularExpressions;
using MaiChartManager.Utils;
using Microsoft.AspNetCore.Mvc;

namespace MaiChartManager.Controllers.Tools;

[ApiController]
[Route("MaiChartManagerServlet/[action]Api")]
public partial class ImageToAbToolController(StaticSettings settings, ILogger<ImageToAbToolController> logger) : ControllerBase
{
    [GeneratedRegex(@"^(?<id>\d+)\.(png|jpg|jpeg)$", RegexOptions.IgnoreCase)]
    private static partial Regex NumericFileRegex();

    [GeneratedRegex(@"^ui_jacket_(?<id>\d+)\.(png|jpg|jpeg)$", RegexOptions.IgnoreCase)]
    private static partial Regex UiJacketFileRegex();

    public enum ImageToAbEventType
    {
        Progress,
        Success,
        Error
    }

    [HttpPost]
    public async Task ImageToAbTool()
    {
        Response.Headers.Append("Content-Type", "text/event-stream");

        var dialog = new FolderBrowserDialog
        {
            Description = Locale.SelectImageFolder,
            ShowNewFolderButton = false,
        };

        if (WinUtils.ShowDialog(dialog) != DialogResult.OK)
        {
            await WriteEvent(ImageToAbEventType.Error, Locale.FileNotSelected);
            return;
        }

        var selectedPath = dialog.SelectedPath;
        if (string.IsNullOrWhiteSpace(selectedPath) || !Directory.Exists(selectedPath))
        {
            await WriteEvent(ImageToAbEventType.Error, Locale.FileNotSelected);
            return;
        }
        
        // 所选择的路径是否是正规的OPT内jacket路径。方法是判断路径结尾是否是AssetBundleImages\jacket
        var isIngameJacketPath = selectedPath.TrimEnd('\\').EndsWith(@"AssetBundleImages\jacket", StringComparison.OrdinalIgnoreCase);

        var deleteOriginalPngAfterSuccess = false;
        if (isIngameJacketPath)
        {
            deleteOriginalPngAfterSuccess = MessageBox.Show(
                Locale.ImageToAbDeleteOriginalPngQuestion, Locale.ImageToAb, MessageBoxButtons.YesNo, 
                MessageBoxIcon.Question) == DialogResult.Yes;
        }

        var candidates = Directory.EnumerateFiles(selectedPath)
            .Select(path => new
            {
                Path = path,
                Name = Path.GetFileName(path),
            })
            .Select(x =>
            {
                var numericMatch = NumericFileRegex().Match(x.Name);
                if (numericMatch.Success)
                {
                    return new ImageTaskItem(x.Path, numericMatch.Groups["id"].Value.PadLeft(6, '0'));
                }

                var uiJacketMatch = UiJacketFileRegex().Match(x.Name);
                if (uiJacketMatch.Success)
                {
                    return new ImageTaskItem(x.Path, uiJacketMatch.Groups["id"].Value.PadLeft(6, '0'));
                }

                return null;
            })
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();

        if (candidates.Count == 0)
        {
            await WriteEvent(
                ImageToAbEventType.Error,
                Locale.NoValidImagesFound);
            return;
        }

        var outputRootDir = isIngameJacketPath ? Path.GetDirectoryName(selectedPath)! : selectedPath;
        var jacketDir = Path.Combine(outputRootDir, "jacket");
        var jacketSmallDir = Path.Combine(outputRootDir, "jacket_s");
        Directory.CreateDirectory(jacketDir);
        Directory.CreateDirectory(jacketSmallDir);

        var failures = new List<string>();
        var total = candidates.Count;

        for (var i = 0; i < total; i++)
        {
            var item = candidates[i];
            var id = item.Id;

            try
            {
                var fullAbPath = Path.Combine(jacketDir, $"ui_jacket_{id}.ab");
                AssetBundleCreator.CreateTextureAssetBundle(
                    item.FilePath,
                    fullAbPath,
                    $"UI_Jacket_{id}",
                    $"assets/assetbundle/jacket/ui_jacket_{id}.png",
                    $"jacket/ui_jacket_{id}.ab");

                var smallAbPath = Path.Combine(jacketSmallDir, $"ui_jacket_{id}_s.ab");
                AssetBundleCreator.CreateTextureAssetBundle(
                    item.FilePath,
                    smallAbPath,
                    $"UI_Jacket_{id}_s",
                    $"assets/assetbundle/jacket_s/ui_jacket_{id}_s.png",
                    $"jacket_s/ui_jacket_{id}_s.ab",
                    resizeWidth: 200,
                    resizeHeight: 200);

                var percent = (int)((i + 1) * 100.0 / total);
                await WriteEvent(ImageToAbEventType.Progress, percent.ToString());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create AB for image {ImagePath}", item.FilePath);
                failures.Add($"{Path.GetFileName(item.FilePath)}: {ex.Message}");
                continue;
            }
            if (deleteOriginalPngAfterSuccess)
            {
                try
                { // 删除原始文件。如果删除失败，也不要抛异常，只是打个警告
                    System.IO.File.Delete(item.FilePath);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete source PNG after ImageToAb: {Path}", item.FilePath);
                }
            }
        }
        
        await settings.RescanAll(); // rescan all

        if (failures.Count > 0)
        {
            await WriteEvent(
                ImageToAbEventType.Error,
                $"{string.Format(Locale.ConvertFailed, $"{failures.Count}/{total}")}\n{string.Join("\n", failures)}");
            return;
        }

        await WriteEvent(ImageToAbEventType.Success, selectedPath);
    }

    private async Task WriteEvent(ImageToAbEventType eventType, string data)
    {
        await Response.WriteAsync($"event: {eventType}\ndata: {data}\n\n");
        await Response.Body.FlushAsync();
    }

    private sealed record ImageTaskItem(string FilePath, string Id);
}
