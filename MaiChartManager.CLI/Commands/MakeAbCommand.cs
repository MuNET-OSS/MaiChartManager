using System.ComponentModel;
using System.Text.RegularExpressions;
using MaiChartManager;
using MaiChartManager.CLI.Utils;
using MaiChartManager.Utils;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MaiChartManager.CLI.Commands;

public partial class MakeAbCommand : AsyncCommand<MakeAbCommand.Settings>
{
    [GeneratedRegex(@"^(?<id>\d+)\.(png|jpg|jpeg)$", RegexOptions.IgnoreCase)]
    private static partial Regex NumericFileRegex();

    [GeneratedRegex(@"^ui_jacket_(?<id>\d+)\.(png|jpg|jpeg)$", RegexOptions.IgnoreCase)]
    private static partial Regex UiJacketFileRegex();

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<folder>")]
        [Description("包含封面图片的文件夹路径")]
        public string Folder { get; set; } = "";

        public override ValidationResult Validate()
        {
            if (!Directory.Exists(Folder))
                return ValidationResult.Error($"文件夹不存在: {Folder}");

            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(StaticSettings.GamePath) || !Directory.Exists(StaticSettings.StreamingAssets))
        {
            AnsiConsole.MarkupLine("[red]✗ 未找到游戏目录，请先通过桌面版配置游戏路径[/]");
            return 1;
        }
        var candidates = Directory.EnumerateFiles(settings.Folder)
            .Select(path => (Path: path, Name: Path.GetFileName(path)))
            .Select(x =>
            {
                var m = NumericFileRegex().Match(x.Name);
                if (m.Success) return (x.Path, Id: m.Groups["id"].Value);

                m = UiJacketFileRegex().Match(x.Name);
                if (m.Success) return (x.Path, Id: m.Groups["id"].Value);

                return (Path: (string?)null, Id: (string?)null);
            })
            .Where(x => x.Path is not null)
            .ToList();

        if (candidates.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]✗ 未找到有效的图片文件（需要：纯6位数字文件名或 ui_jacket_xxxxxx 格式，PNG/JPG）[/]");
            return 1;
        }

        var jacketDir = Path.Combine(settings.Folder, "jacket");
        var jacketSmallDir = Path.Combine(settings.Folder, "jacket_s");
        Directory.CreateDirectory(jacketDir);
        Directory.CreateDirectory(jacketSmallDir);

        AnsiConsole.MarkupLine($"[yellow]找到 {candidates.Count} 张图片，开始转换...[/]");

        var errors = new List<string>();

        await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("转换中", maxValue: candidates.Count);

                foreach (var (path, id) in candidates)
                {
                    task.Description = $"[green]ui_jacket_{id}[/]";

                    try
                    {
                        await Task.Run(() =>
                        {
                            AssetBundleCreator.CreateTextureAssetBundle(
                                path!,
                                Path.Combine(jacketDir, $"ui_jacket_{id}.ab"),
                                $"UI_Jacket_{id}",
                                $"assets/assetbundle/jacket/ui_jacket_{id}.png",
                                $"jacket/ui_jacket_{id}.ab",
                                maxDimension: AssetBundleCreator.MaxJacketDimension);

                            AssetBundleCreator.CreateTextureAssetBundle(
                                path!,
                                Path.Combine(jacketSmallDir, $"ui_jacket_{id}_s.ab"),
                                $"UI_Jacket_{id}_s",
                                $"assets/assetbundle/jacket_s/ui_jacket_{id}_s.png",
                                $"jacket_s/ui_jacket_{id}_s.ab",
                                resizeWidth: 200,
                                resizeHeight: 200);
                        }, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        SentrySdk.CaptureException(ex);
                        errors.Add($"{Path.GetFileName(path!)}: {ex.Message}");
                    }

                    task.Increment(1);
                    TerminalProgress.Set((int)(task.Value * 100 / task.MaxValue));
                }

                TerminalProgress.Clear();
            });

        if (errors.Count > 0)
        {
            AnsiConsole.MarkupLine($"[red]✗ {errors.Count}/{candidates.Count} 个文件转换失败:[/]");
            foreach (var e in errors)
                AnsiConsole.MarkupLine($"[red]  {e}[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓ {candidates.Count} 张图片全部转换成功！[/]");
        AnsiConsole.MarkupLine($"[green]  jacket → {jacketDir}[/]");
        AnsiConsole.MarkupLine($"[green]  jacket_s → {jacketSmallDir}[/]");
        return 0;
    }
}
