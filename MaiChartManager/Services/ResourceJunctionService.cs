using System.ComponentModel;
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
    public static IReadOnlyList<string> ResourceNames { get; } =
        Array.AsReadOnly(["AssetBundleImages", "MovieData", "SoundData"]);

    private const uint IoReparseTagMountPoint = 0xA0000003;
    private readonly Func<string> targetPathProvider;
    private readonly Func<IEnumerable<string>> candidatePathProvider;
    private readonly bool pathsAreA000Roots;
    private readonly object stateGate = new();
    private readonly Dictionary<string, SelectionState> sessionStates = new(StringComparer.Ordinal);

    public ResourceJunctionService()
        : this(() => StaticSettings.GamePath, GetDefaultCandidatePaths, false)
    {
    }

    public ResourceJunctionService(string sourceRoot, string targetRoot)
        : this(() => targetRoot, () => [], true)
    {
        var state = GetState("default");
        state.SelectedSourceRoot = NormalizePath(sourceRoot);
        state.SelectionMode = ResourceSourceSelectionMode.Manual;
        state.SelectedFileCounts = CountResourceFiles(state.SelectedSourceRoot);
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

    public ResourceJunctionOverview AutoSelectSource(string sessionId = "default")
    {
        lock (stateGate)
        {
        var state = GetState(sessionId);
        var targetRoot = GetTargetRoot(state);
        if (targetRoot is null)
            return ClearSelection(state, ResourceSourceSelectionMode.None, "The current game directory is invalid.");

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
            return ClearSelection(state, ResourceSourceSelectionMode.None, "No valid source game directory was found in game path history or adjacent directories.");

        var best = candidates[0];
        if (candidates.Skip(1).Any(candidate => candidate.TotalFileCount == best.TotalFileCount))
            return ClearSelection(state, ResourceSourceSelectionMode.Tie, "Multiple source game directories have the same highest file count. Select one manually.");

        state.SelectedSourceRoot = best.Root;
        state.SelectedFileCounts = best.FileCounts;
        state.SelectionMode = ResourceSourceSelectionMode.Automatic;
        state.SelectionDetail = null;
        return GetOverviewCore(state);
        }
    }

    public ResourceJunctionOverview SelectManualSource(string path, string sessionId = "default")
    {
        lock (stateGate)
        {
        var state = GetState(sessionId);
        var targetRoot = GetTargetRoot(state) ?? throw new InvalidOperationException("The current game directory is invalid.");
        var sourceRoot = TryResolveA000Root(path)
            ?? throw new ArgumentException("The selected folder is not a valid game root or Package directory.", nameof(path));
        if (SamePath(sourceRoot, targetRoot))
            throw new ArgumentException("The source game directory must differ from the current game directory.", nameof(path));

        var candidate = TryCreateCandidate(sourceRoot)
            ?? throw new ArgumentException("The selected source must contain three readable, real resource directories.", nameof(path));
        state.SelectedSourceRoot = candidate.Root;
        state.SelectedFileCounts = candidate.FileCounts;
        state.SelectionMode = ResourceSourceSelectionMode.Manual;
        state.SelectionDetail = null;
        return GetOverviewCore(state);
        }
    }

    public ResourceJunctionOverview SelectManualTarget(string path, string sessionId = "default")
    {
        lock (stateGate)
        {
        var state = GetState(sessionId);
        var targetRoot = TryResolveA000Root(path)
            ?? throw new ArgumentException("The selected folder is not a valid game root or Package directory.", nameof(path));

        state.SelectedTargetRoot = targetRoot;
        if (state.SelectedSourceRoot is not null && SamePath(state.SelectedSourceRoot, targetRoot))
            return ClearSelection(state, ResourceSourceSelectionMode.None, "The source must differ from the selected target. Select a source directory again.");

        return GetOverviewCore(state);
        }
    }

    public ResourceJunctionOverview GetOverview(string sessionId = "default")
    {
        lock (stateGate)
        {
        return GetOverviewCore(GetState(sessionId));
        }
    }

    private ResourceJunctionOverview GetOverviewCore(SelectionState state)
    {
        var targetRoot = GetTargetRoot(state);
        var items = state.SelectedSourceRoot is null || targetRoot is null
            ? BuildUnavailableItems(state, targetRoot)
            : ResourceNames.Select(name => Inspect(name, state.SelectedSourceRoot, targetRoot)).ToArray();
        return new(
            state.SelectedSourceRoot,
            targetRoot,
            state.SelectionMode,
            state.SelectedFileCounts,
            state.SelectedFileCounts.Sum(item => item.FileCount),
            state.SelectionDetail,
            items);
    }

    public IReadOnlyList<ResourceJunctionItem> Inspect(string sessionId = "default")
    {
        return GetOverview(sessionId).Items;
    }

    public IReadOnlyList<ResourceJunctionItem> CreateLinks(string sessionId = "default")
    {
        lock (stateGate)
        {
        var state = GetState(sessionId);
        var sourceRoot = state.SelectedSourceRoot;
        var targetRoot = GetTargetRoot(state);
        if (sourceRoot is null || targetRoot is null) return BuildUnavailableItems(state, targetRoot);

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
    }

    public IReadOnlyList<ResourceJunctionItem> RemoveLinks(string sessionId = "default")
    {
        lock (stateGate)
        {
        var state = GetState(sessionId);
        var sourceRoot = state.SelectedSourceRoot;
        var targetRoot = GetTargetRoot(state);
        if (sourceRoot is null || targetRoot is null) return BuildUnavailableItems(state, targetRoot);

        return ResourceNames.Select(name =>
        {
            var item = Inspect(name, sourceRoot, targetRoot);
            if (item.Status != ResourceJunctionStatus.AlreadyLinked) return item;

            try
            {
                if (!RemoveVerifiedJunction(item.Source, item.Target))
                    return Inspect(name, sourceRoot, targetRoot);
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
    }

    private ResourceJunctionOverview ClearSelection(SelectionState state, ResourceSourceSelectionMode mode, string detail)
    {
        state.SelectedSourceRoot = null;
        state.SelectedFileCounts = [];
        state.SelectionMode = mode;
        state.SelectionDetail = detail;
        return GetOverviewCore(state);
    }

    private string? GetTargetRoot(SelectionState state)
    {
        if (state.SelectedTargetRoot is not null) return state.SelectedTargetRoot;
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
                ? string.Equals(Path.GetFileName(fullPath), "Package", StringComparison.OrdinalIgnoreCase)
                    ? Directory.GetParent(fullPath)?.FullName
                    : fullPath
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
            return new ResourceDirectoryFileCount(name, CountFilesWithoutReparsePoints(directory));
        }).ToArray();
    }

    private static long CountFilesWithoutReparsePoints(DirectoryInfo root)
    {
        var count = 0L;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (entry is DirectoryInfo child)
                    pending.Push(child);
                else
                    count++;
            }
        }
        return count;
    }

    private IReadOnlyList<ResourceJunctionItem> BuildUnavailableItems(SelectionState state, string? targetRoot)
    {
        var status = targetRoot is null ? ResourceJunctionStatus.TargetRootMissing : ResourceJunctionStatus.SourceMissing;
        return ResourceNames.Select(name => new ResourceJunctionItem(
            name,
            state.SelectedSourceRoot is null ? "" : Path.Combine(state.SelectedSourceRoot, name),
            targetRoot is null ? "" : Path.Combine(targetRoot, name),
            status,
            state.SelectionDetail)).ToArray();
    }

    private SelectionState GetState(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("Session id is required.", nameof(sessionId));
        if (!sessionStates.TryGetValue(sessionId, out var state))
            sessionStates[sessionId] = state = new SelectionState();
        return state;
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

    internal static bool RemoveVerifiedJunction(string source, string target, Action? beforeDelete = null)
    {
        using var handle = CreateFile(
            target,
            0x00010000,
            0x00000001 | 0x00000002,
            IntPtr.Zero,
            3,
            0x00200000 | 0x02000000,
            IntPtr.Zero);
        if (handle.IsInvalid) return false;

        var buffer = new byte[16 * 1024];
        if (!DeviceIoControl(handle, 0x000900A8, IntPtr.Zero, 0, buffer, buffer.Length, out _, IntPtr.Zero))
            return false;
        if (BitConverter.ToUInt32(buffer, 0) != IoReparseTagMountPoint)
            return false;

        var destination = ReadMountPointDestination(buffer);
        if (destination is null || !SamePath(destination, source))
            return false;

        beforeDelete?.Invoke();
        var disposition = new FileDispositionInfo { DeleteFile = 1 };
        if (!SetFileInformationByHandle(
                handle,
                4,
                ref disposition,
                (uint)Marshal.SizeOf<FileDispositionInfo>()))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return true;
    }

    private static string? ReadMountPointDestination(byte[] buffer)
    {
        const int pathBufferOffset = 16;
        var printNameOffset = BitConverter.ToUInt16(buffer, 12);
        var printNameLength = BitConverter.ToUInt16(buffer, 14);
        if (printNameLength > 0)
            return System.Text.Encoding.Unicode.GetString(buffer, pathBufferOffset + printNameOffset, printNameLength);

        var substituteNameOffset = BitConverter.ToUInt16(buffer, 8);
        var substituteNameLength = BitConverter.ToUInt16(buffer, 10);
        if (substituteNameLength == 0) return null;
        var substituteName = System.Text.Encoding.Unicode.GetString(
            buffer,
            pathBufferOffset + substituteNameOffset,
            substituteNameLength);
        return substituteName.StartsWith(@"\??\", StringComparison.Ordinal) ? substituteName[4..] : substituteName;
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

    private sealed class SelectionState
    {
        public string? SelectedSourceRoot { get; set; }
        public string? SelectedTargetRoot { get; set; }
        public ResourceSourceSelectionMode SelectionMode { get; set; }
        public IReadOnlyList<ResourceDirectoryFileCount> SelectedFileCounts { get; set; } = [];
        public string? SelectionDetail { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfo
    {
        public byte DeleteFile;
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        IntPtr inputBuffer,
        int inputBufferSize,
        [Out] byte[] outputBuffer,
        int outputBufferSize,
        out int bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle file,
        int fileInformationClass,
        ref FileDispositionInfo fileInformation,
        uint bufferSize);
}
