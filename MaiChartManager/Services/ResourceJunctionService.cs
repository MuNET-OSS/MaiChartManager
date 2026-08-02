using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MaiChartManager.Services;

public enum ResourceJunctionStatus
{
    Ready,
    Created,
    AlreadyLinked,
    Removed,
    SourceMissing,
    TargetRootMissing,
    Conflict,
    WrongTarget,
    Failed,
    Unsupported,
}

public enum ResourceSourceSelectionMode
{
    None,
    Automatic,
    Manual,
    Tie,
}

public record ResourceJunctionItem(
    string Name,
    string Source,
    string Target,
    ResourceJunctionStatus Status,
    string? Detail = null);

public record ResourceDirectoryFileCount(string Name, long FileCount);

public record ResourceJunctionOverview(
    string? SourceRoot,
    string? TargetRoot,
    ResourceSourceSelectionMode SelectionMode,
    IReadOnlyList<ResourceDirectoryFileCount> FileCounts,
    long TotalFileCount,
    string? Detail,
    IReadOnlyList<ResourceJunctionItem> Items);

public class ResourceJunctionService
{
    public static readonly string[] ResourceNames = ["AssetBundleImages", "MovieData", "SoundData"];

    private const uint IoReparseTagMountPoint = 0xA0000003;
    private readonly Func<string> targetPathProvider;
    private readonly Func<IEnumerable<string>> candidatePathProvider;
    private readonly bool pathsAreA000Roots;
    private string? selectedSourceRoot;
    private string? selectedTargetRoot;
    private ResourceSourceSelectionMode selectionMode;
    private IReadOnlyList<ResourceDirectoryFileCount> selectedFileCounts = [];
    private string? selectionDetail;

    public ResourceJunctionService()
        : this(() => StaticSettings.GamePath, GetDefaultCandidatePaths, false)
    {
    }

    public ResourceJunctionService(string sourceRoot, string targetRoot)
        : this(() => targetRoot, () => [], true)
    {
        selectedSourceRoot = NormalizePath(sourceRoot);
        selectionMode = ResourceSourceSelectionMode.Manual;
        selectedFileCounts = CountResourceFiles(selectedSourceRoot);
    }

    public ResourceJunctionService(Func<string> targetPathProvider, Func<IEnumerable<string>> candidatePathProvider)
        : this(targetPathProvider, candidatePathProvider, false)
    {
    }

    private ResourceJunctionService(
        Func<string> targetPathProvider,
        Func<IEnumerable<string>> candidatePathProvider,
        bool pathsAreA000Roots)
    {
        this.targetPathProvider = targetPathProvider;
        this.candidatePathProvider = candidatePathProvider;
        this.pathsAreA000Roots = pathsAreA000Roots;
    }

    public ResourceJunctionOverview AutoSelectSource()
    {
        var targetRoot = GetTargetRoot();
        if (targetRoot is null)
            return ClearSelection(ResourceSourceSelectionMode.None, "The current game directory is invalid.");

        var candidates = candidatePathProvider()
            .Select(TryResolveA000Root)
            .Where(path => path is not null && !SamePath(path, targetRoot))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => TryCreateCandidate(path!))
            .Where(candidate => candidate is not null)
            .Cast<ResourceSourceCandidate>()
            .OrderByDescending(candidate => candidate.TotalFileCount)
            .ToArray();

        if (candidates.Length == 0)
            return ClearSelection(ResourceSourceSelectionMode.None, "No valid source game directory was found in game path history or adjacent directories.");

        var best = candidates[0];
        if (candidates.Skip(1).Any(candidate => candidate.TotalFileCount == best.TotalFileCount))
            return ClearSelection(ResourceSourceSelectionMode.Tie, "Multiple source game directories have the same highest file count. Select one manually.");

        selectedSourceRoot = best.Root;
        selectedFileCounts = best.FileCounts;
        selectionMode = ResourceSourceSelectionMode.Automatic;
        selectionDetail = null;
        return GetOverview();
    }

    public ResourceJunctionOverview SelectManualSource(string path)
    {
        var targetRoot = GetTargetRoot() ?? throw new InvalidOperationException("The current game directory is invalid.");
        var sourceRoot = TryResolveA000Root(path)
            ?? throw new ArgumentException("The selected folder is not a valid game root or Package directory.", nameof(path));
        if (SamePath(sourceRoot, targetRoot))
            throw new ArgumentException("The source game directory must differ from the current game directory.", nameof(path));

        var candidate = TryCreateCandidate(sourceRoot)
            ?? throw new ArgumentException("The selected source must contain three readable, real resource directories.", nameof(path));
        selectedSourceRoot = candidate.Root;
        selectedFileCounts = candidate.FileCounts;
        selectionMode = ResourceSourceSelectionMode.Manual;
        selectionDetail = null;
        return GetOverview();
    }

    public ResourceJunctionOverview SelectManualTarget(string path)
    {
        var targetRoot = TryResolveA000Root(path)
            ?? throw new ArgumentException("The selected folder is not a valid game root or Package directory.", nameof(path));

        selectedTargetRoot = targetRoot;
        if (selectedSourceRoot is not null && SamePath(selectedSourceRoot, targetRoot))
            return ClearSelection(ResourceSourceSelectionMode.None, "The source must differ from the selected target. Select a source directory again.");

        return GetOverview();
    }

    public ResourceJunctionOverview GetOverview()
    {
        var targetRoot = GetTargetRoot();
        var items = selectedSourceRoot is null || targetRoot is null
            ? BuildUnavailableItems(targetRoot)
            : ResourceNames.Select(name => Inspect(name, selectedSourceRoot, targetRoot)).ToArray();
        return new(
            selectedSourceRoot,
            targetRoot,
            selectionMode,
            selectedFileCounts,
            selectedFileCounts.Sum(item => item.FileCount),
            selectionDetail,
            items);
    }

    public IReadOnlyList<ResourceJunctionItem> Inspect()
    {
        return GetOverview().Items;
    }

    public IReadOnlyList<ResourceJunctionItem> CreateLinks()
    {
        var sourceRoot = selectedSourceRoot;
        var targetRoot = GetTargetRoot();
        if (sourceRoot is null || targetRoot is null) return BuildUnavailableItems(targetRoot);

        return ResourceNames.Select(name =>
        {
            var item = Inspect(name, sourceRoot, targetRoot);
            if (item.Status != ResourceJunctionStatus.Ready) return item;

            try
            {
                CreateJunction(item.Source, item.Target);
                var verified = Inspect(name, sourceRoot, targetRoot);
                return verified.Status == ResourceJunctionStatus.AlreadyLinked
                    ? verified with { Status = ResourceJunctionStatus.Created }
                    : verified with { Status = ResourceJunctionStatus.Failed, Detail = "Junction was created but verification failed." };
            }
            catch (Exception e)
            {
                return item with { Status = ResourceJunctionStatus.Failed, Detail = e.Message };
            }
        }).ToArray();
    }

    public IReadOnlyList<ResourceJunctionItem> RemoveLinks()
    {
        var sourceRoot = selectedSourceRoot;
        var targetRoot = GetTargetRoot();
        if (sourceRoot is null || targetRoot is null) return BuildUnavailableItems(targetRoot);

        return ResourceNames.Select(name =>
        {
            var item = Inspect(name, sourceRoot, targetRoot);
            if (item.Status != ResourceJunctionStatus.AlreadyLinked) return item;

            try
            {
                Directory.Delete(item.Target, false);
                var verified = Inspect(name, sourceRoot, targetRoot);
                return verified.Status == ResourceJunctionStatus.Ready
                    ? verified with { Status = ResourceJunctionStatus.Removed }
                    : verified with { Status = ResourceJunctionStatus.Failed, Detail = "Junction removal could not be verified." };
            }
            catch (Exception e)
            {
                return item with { Status = ResourceJunctionStatus.Failed, Detail = e.Message };
            }
        }).ToArray();
    }

    private ResourceJunctionOverview ClearSelection(ResourceSourceSelectionMode mode, string detail)
    {
        selectedSourceRoot = null;
        selectedFileCounts = [];
        selectionMode = mode;
        selectionDetail = detail;
        return GetOverview();
    }

    private string? GetTargetRoot()
    {
        if (selectedTargetRoot is not null) return selectedTargetRoot;
        var path = targetPathProvider();
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (pathsAreA000Roots) return Directory.Exists(path) ? NormalizePath(path) : null;
        return TryResolveA000Root(path);
    }

    private static string? TryResolveA000Root(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var fullPath = NormalizePath(path);
            var direct = Path.Combine(fullPath, "Sinmai_Data", "StreamingAssets", "A000");
            if (Directory.Exists(direct)) return NormalizePath(direct);

            var package = Path.Combine(fullPath, "Package", "Sinmai_Data", "StreamingAssets", "A000");
            return Directory.Exists(package) ? NormalizePath(package) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IEnumerable<string> GetDefaultCandidatePaths()
    {
        foreach (var historyPath in StaticSettings.Config.HistoryPath)
            yield return historyPath;

        var currentPath = StaticSettings.GamePath;
        if (string.IsNullOrWhiteSpace(currentPath)) yield break;

        string? gameRoot;
        try
        {
            var fullPath = NormalizePath(currentPath);
            gameRoot = Directory.Exists(Path.Combine(fullPath, "Sinmai_Data", "StreamingAssets", "A000"))
                ? Directory.GetParent(fullPath)?.FullName
                : Directory.Exists(Path.Combine(fullPath, "Package", "Sinmai_Data", "StreamingAssets", "A000"))
                    ? fullPath
                    : null;
        }
        catch (Exception)
        {
            yield break;
        }

        var parent = gameRoot is null ? null : Directory.GetParent(gameRoot)?.FullName;
        if (parent is null || !Directory.Exists(parent)) yield break;

        IEnumerable<string> siblings;
        try
        {
            siblings = Directory.EnumerateDirectories(parent).ToArray();
        }
        catch (Exception)
        {
            yield break;
        }

        foreach (var sibling in siblings)
            yield return sibling;
    }

    private static ResourceSourceCandidate? TryCreateCandidate(string root)
    {
        try
        {
            var counts = CountResourceFiles(root);
            return new(root, counts, counts.Sum(item => item.FileCount));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IReadOnlyList<ResourceDirectoryFileCount> CountResourceFiles(string root)
    {
        return ResourceNames.Select(name =>
        {
            var directory = new DirectoryInfo(Path.Combine(root, name));
            if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"{name} is missing or is a reparse point.");
            return new ResourceDirectoryFileCount(name, directory.EnumerateFiles("*", SearchOption.AllDirectories).LongCount());
        }).ToArray();
    }

    private IReadOnlyList<ResourceJunctionItem> BuildUnavailableItems(string? targetRoot)
    {
        var status = targetRoot is null ? ResourceJunctionStatus.TargetRootMissing : ResourceJunctionStatus.SourceMissing;
        return ResourceNames.Select(name => new ResourceJunctionItem(
            name,
            selectedSourceRoot is null ? "" : Path.Combine(selectedSourceRoot, name),
            targetRoot is null ? "" : Path.Combine(targetRoot, name),
            status,
            selectionDetail)).ToArray();
    }

    private static ResourceJunctionItem Inspect(string name, string sourceRoot, string targetRoot)
    {
        var source = Path.Combine(sourceRoot, name);
        var target = Path.Combine(targetRoot, name);

        if (!OperatingSystem.IsWindows())
            return new(name, source, target, ResourceJunctionStatus.Unsupported, "Junctions are only supported on Windows.");
        if (!Directory.Exists(source))
            return new(name, source, target, ResourceJunctionStatus.SourceMissing);
        if (!Directory.Exists(targetRoot))
            return new(name, source, target, ResourceJunctionStatus.TargetRootMissing);

        var entry = FindTargetEntry(targetRoot, name);
        if (entry is null)
            return new(name, source, target, ResourceJunctionStatus.Ready);
        if (!TryGetReparseTag(target, out var tag) || tag != IoReparseTagMountPoint)
            return new(name, source, target, ResourceJunctionStatus.Conflict, "The target exists and is not a Junction.");

        try
        {
            var destination = entry.ResolveLinkTarget(false)?.FullName;
            if (destination is not null && SamePath(destination, source))
                return new(name, source, target, ResourceJunctionStatus.AlreadyLinked);
            return new(name, source, target, ResourceJunctionStatus.WrongTarget, destination);
        }
        catch (Exception e)
        {
            return new(name, source, target, ResourceJunctionStatus.WrongTarget, e.Message);
        }
    }

    private static FileSystemInfo? FindTargetEntry(string targetRoot, string name)
    {
        return new DirectoryInfo(targetRoot)
            .EnumerateFileSystemInfos(name, SearchOption.TopDirectoryOnly)
            .FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool SamePath(string left, string right)
    {
        return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            path = @"\\" + path[8..];
        else if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            path = path[4..];
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static void CreateJunction(string source, string target)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(target);
        startInfo.ArgumentList.Add(source);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start mklink.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new IOException((error.Length > 0 ? error : output).Trim());
    }

    private static bool TryGetReparseTag(string path, out uint tag)
    {
        tag = 0;
        using var handle = CreateFile(
            path,
            0,
            0x00000001 | 0x00000002 | 0x00000004,
            IntPtr.Zero,
            3,
            0x00200000 | 0x02000000,
            IntPtr.Zero);
        if (handle.IsInvalid) return false;

        if (!GetFileInformationByHandleEx(handle, 9, out var info, (uint)Marshal.SizeOf<FileAttributeTagInfo>()))
            return false;
        tag = info.ReparseTag;
        return true;
    }

    private record ResourceSourceCandidate(
        string Root,
        IReadOnlyList<ResourceDirectoryFileCount> FileCounts,
        long TotalFileCount);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int fileInformationClass,
        out FileAttributeTagInfo fileInformation,
        uint bufferSize);
}
