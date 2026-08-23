using MaiChartManager.Services;

namespace MaiChartManager.Tests.Services;

[CollectionDefinition("Static settings", DisableParallelization = true)]
public sealed class StaticSettingsCollection;

[Collection("Static settings")]
public sealed class ResourceJunctionServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"mcm resource links & {Guid.NewGuid():N}");
    private readonly string sourceRoot;
    private readonly string targetRoot;

    public ResourceJunctionServiceTests()
    {
        sourceRoot = Path.Combine(root, "source");
        targetRoot = Path.Combine(root, "target");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(targetRoot);
        foreach (var name in ResourceJunctionService.ResourceNames)
            Directory.CreateDirectory(Path.Combine(sourceRoot, name));
    }

    [Fact]
    public void FixedScopeContainsOnlyThreeResourceDirectories()
    {
        Assert.Equal(["AssetBundleImages", "MovieData", "SoundData"], ResourceJunctionService.ResourceNames);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)ResourceJunctionService.ResourceNames)[0] = "OtherDirectory");
    }

    [Fact]
    public void AutoSelectionAcceptsGameRootAndPackageAndChoosesMostFiles()
    {
        var targetGame = CreateGame("target-game", [0, 0, 0]);
        var smallerGame = CreateGame("smaller-game", [1, 1, 1]);
        var largerGame = CreateGame("larger-game", [2, 3, 4]);
        var service = new ResourceJunctionService(
            () => Path.Combine(targetGame, "Package"),
            () => [targetGame, Path.Combine(smallerGame, "Package"), largerGame]);

        var overview = service.AutoSelectSource();

        Assert.Equal(ResourceSourceSelectionMode.Automatic, overview.SelectionMode);
        Assert.Equal(Path.Combine(largerGame, "Package", "Sinmai_Data", "StreamingAssets", "A000"), overview.SourceRoot);
        Assert.Equal(9, overview.TotalFileCount);
        Assert.Equal([2L, 3L, 4L], overview.FileCounts.Select(item => item.FileCount));
    }

    [Fact]
    public void AutoSelectionRejectsIncompleteCandidatesAndCurrentGame()
    {
        var targetGame = CreateGame("target-game-invalid", [5, 5, 5]);
        var incompleteGame = CreateGame("incomplete-game", [1, 1, 1]);
        Directory.Delete(Path.Combine(incompleteGame, "Package", "Sinmai_Data", "StreamingAssets", "A000", "MovieData"), true);
        var service = new ResourceJunctionService(
            () => Path.Combine(targetGame, "Package"),
            () => [targetGame, incompleteGame, Path.Combine(root, "missing")]);

        var overview = service.AutoSelectSource();

        Assert.Equal(ResourceSourceSelectionMode.None, overview.SelectionMode);
        Assert.Null(overview.SourceRoot);
        Assert.All(overview.Items, item => Assert.Equal(ResourceJunctionStatus.SourceMissing, item.Status));
    }

    [Fact]
    public void AutoSelectionRequiresManualChoiceWhenHighestCountsTie()
    {
        var targetGame = CreateGame("target-game-tie", [0, 0, 0]);
        var firstGame = CreateGame("first-game-tie", [1, 2, 3]);
        var secondGame = CreateGame("second-game-tie", [3, 2, 1]);
        var service = new ResourceJunctionService(
            () => targetGame,
            () => [firstGame, secondGame]);

        var overview = service.AutoSelectSource();

        Assert.Equal(ResourceSourceSelectionMode.Tie, overview.SelectionMode);
        Assert.Null(overview.SourceRoot);
        Assert.NotNull(overview.Detail);
    }

    [Fact]
    public void AutoSelectionFindsSiblingForDirectSinmaiDataLayout()
    {
        var targetGame = CreateDirectGame("target-direct", [0, 0, 0]);
        var sourceGame = CreateDirectGame("source-direct", [2, 2, 2]);
        var previousGamePath = StaticSettings.GamePath;
        var previousConfig = StaticSettings.Config;
        try
        {
            StaticSettings.GamePath = targetGame;
            StaticSettings.Config = new Config();

            var overview = new ResourceJunctionService().AutoSelectSource();

            Assert.Equal(ResourceSourceSelectionMode.Automatic, overview.SelectionMode);
            Assert.Equal(Path.Combine(sourceGame, "Sinmai_Data", "StreamingAssets", "A000"), overview.SourceRoot);
            Assert.Equal(6, overview.TotalFileCount);
        }
        finally
        {
            StaticSettings.GamePath = previousGamePath;
            StaticSettings.Config = previousConfig;
        }
    }

    [Fact]
    public void AutoSelectionRejectsResourceDirectoryJunctions()
    {
        if (!OperatingSystem.IsWindows()) return;
        var targetGame = CreateGame("target-game-reparse", [0, 0, 0]);
        var sourceGame = CreateGame("source-game-reparse", [1, 1, 1]);
        var resourcePath = Path.Combine(sourceGame, "Package", "Sinmai_Data", "StreamingAssets", "A000", "MovieData");
        var linkedDirectory = Path.Combine(root, "linked-resource");
        Directory.Delete(resourcePath, true);
        Directory.CreateDirectory(linkedDirectory);
        CreateJunction(linkedDirectory, resourcePath);
        try
        {
            var service = new ResourceJunctionService(() => targetGame, () => [sourceGame]);

            var overview = service.AutoSelectSource();

            Assert.Equal(ResourceSourceSelectionMode.None, overview.SelectionMode);
            Assert.Null(overview.SourceRoot);
        }
        finally
        {
            Directory.Delete(resourcePath, false);
        }
    }

    [Fact]
    public void AutoSelectionDoesNotTraverseNestedReparsePoints()
    {
        if (!OperatingSystem.IsWindows()) return;
        var targetGame = CreateGame("target-game-nested-reparse", [0, 0, 0]);
        var sourceGame = CreateGame("source-game-nested-reparse", [1, 1, 1]);
        var externalDirectory = Path.Combine(root, "external-resource-files");
        Directory.CreateDirectory(externalDirectory);
        File.WriteAllText(Path.Combine(externalDirectory, "outside.dat"), "outside");
        var nestedJunction = Path.Combine(
            sourceGame,
            "Package",
            "Sinmai_Data",
            "StreamingAssets",
            "A000",
            "AssetBundleImages",
            "external-link");
        CreateJunction(externalDirectory, nestedJunction);
        try
        {
            var service = new ResourceJunctionService(() => targetGame, () => [sourceGame]);

            var overview = service.AutoSelectSource();

            Assert.Equal(3, overview.TotalFileCount);
        }
        finally
        {
            Directory.Delete(nestedJunction, false);
        }
    }

    [Fact]
    public void ManualSelectionOverridesAutomaticSelectionForSession()
    {
        var targetGame = CreateGame("target-game-manual", [0, 0, 0]);
        var automaticGame = CreateGame("automatic-game", [4, 4, 4]);
        var manualGame = CreateGame("manual-game", [1, 1, 1]);
        var service = new ResourceJunctionService(
            () => targetGame,
            () => [automaticGame, manualGame]);

        service.AutoSelectSource();
        var overview = service.SelectManualSource(Path.Combine(manualGame, "Package"));

        Assert.Equal(ResourceSourceSelectionMode.Manual, overview.SelectionMode);
        Assert.Equal(Path.Combine(manualGame, "Package", "Sinmai_Data", "StreamingAssets", "A000"), overview.SourceRoot);
        Assert.Equal(3, overview.TotalFileCount);
    }

    [Fact]
    public void SelectionStateIsIsolatedBetweenSessions()
    {
        var targetGame = CreateGame("target-game-session-isolation", [0, 0, 0]);
        var firstSource = CreateGame("first-source-session-isolation", [1, 1, 1]);
        var secondSource = CreateGame("second-source-session-isolation", [2, 2, 2]);
        var service = new ResourceJunctionService(() => targetGame, () => [firstSource, secondSource]);

        service.SelectManualSource(firstSource, "first");
        service.SelectManualSource(secondSource, "second");

        Assert.Equal(
            Path.Combine(firstSource, "Package", "Sinmai_Data", "StreamingAssets", "A000"),
            service.GetOverview("first").SourceRoot);
        Assert.Equal(
            Path.Combine(secondSource, "Package", "Sinmai_Data", "StreamingAssets", "A000"),
            service.GetOverview("second").SourceRoot);
    }

    [Fact]
    public void UnsupportedOverviewUsesUnsupportedStatusForEveryResource()
    {
        var overview = ResourceJunctionService.CreateUnsupportedOverview();

        Assert.All(overview.Items, item => Assert.Equal(ResourceJunctionStatus.Unsupported, item.Status));
        Assert.Null(overview.SourceRoot);
        Assert.Null(overview.TargetRoot);
    }

    [Fact]
    public void AutoSelectionPreservesManualSourceForTheSameSession()
    {
        var sourceRoot = CreateGame("manual-source", [1, 2, 3]);
        var targetRoot = CreateGame("manual-target", [0, 0, 0]);
        var service = new ResourceJunctionService(
            () => targetRoot,
            () => [sourceRoot]);

        var manual = service.SelectManualSource(sourceRoot, "session");
        var afterAuto = service.AutoSelectSource("session");

        Assert.Equal(manual.SourceRoot, afterAuto.SourceRoot);
        Assert.Equal(ResourceSourceSelectionMode.Manual, afterAuto.SelectionMode);
    }

    [Fact]
    public void ManualSelectionRejectsCurrentGame()
    {
        var targetGame = CreateGame("target-game-self", [0, 0, 0]);
        var service = new ResourceJunctionService(() => targetGame, () => []);

        Assert.Throws<ArgumentException>(() => service.SelectManualSource(targetGame));
    }

    [Fact]
    public void ManualTargetSelectionIsSessionOnlyAndKeepsDistinctSource()
    {
        var configuredTarget = CreateGame("configured-target", [0, 0, 0]);
        var manualTarget = CreateGame("manual-target", [0, 0, 0]);
        var sourceGame = CreateGame("source-for-manual-target", [1, 1, 1]);
        var configuredTargetReads = 0;
        var service = new ResourceJunctionService(
            () =>
            {
                configuredTargetReads++;
                return configuredTarget;
            },
            () => [sourceGame]);
        service.AutoSelectSource();
        var readsBeforeManualSelection = configuredTargetReads;

        var overview = service.SelectManualTarget(manualTarget);

        Assert.Equal(Path.Combine(manualTarget, "Package", "Sinmai_Data", "StreamingAssets", "A000"), overview.TargetRoot);
        Assert.Equal(Path.Combine(sourceGame, "Package", "Sinmai_Data", "StreamingAssets", "A000"), overview.SourceRoot);
        Assert.Equal(readsBeforeManualSelection, configuredTargetReads);
    }

    [Fact]
    public void SelectingCurrentSourceAsTargetClearsSource()
    {
        var configuredTarget = CreateGame("configured-target-clear", [0, 0, 0]);
        var sourceGame = CreateGame("source-becomes-target", [1, 1, 1]);
        var service = new ResourceJunctionService(() => configuredTarget, () => [sourceGame]);
        service.AutoSelectSource();

        var overview = service.SelectManualTarget(sourceGame);

        Assert.Equal(ResourceSourceSelectionMode.None, overview.SelectionMode);
        Assert.Null(overview.SourceRoot);
        Assert.NotNull(overview.Detail);
    }

    [Fact]
    public void ExistingRealDirectoriesAreConflicts()
    {
        if (!OperatingSystem.IsWindows()) return;
        foreach (var name in ResourceJunctionService.ResourceNames)
            Directory.CreateDirectory(Path.Combine(targetRoot, name));

        var result = new ResourceJunctionService(sourceRoot, targetRoot).Inspect();

        Assert.All(result, item => Assert.Equal(ResourceJunctionStatus.Conflict, item.Status));
    }

    [Fact]
    public void CreateAndRemoveOnlyVerifiedJunctions()
    {
        if (!OperatingSystem.IsWindows()) return;
        var sourceFile = Path.Combine(sourceRoot, ResourceJunctionService.ResourceNames[0], "source.txt");
        File.WriteAllText(sourceFile, "source remains unchanged");
        var service = new ResourceJunctionService(sourceRoot, targetRoot);

        var created = service.CreateLinks();
        var inspected = service.Inspect();
        var removed = service.RemoveLinks();

        Assert.All(created, item => Assert.Equal(ResourceJunctionStatus.Created, item.Status));
        Assert.All(inspected, item => Assert.Equal(ResourceJunctionStatus.AlreadyLinked, item.Status));
        Assert.All(removed, item => Assert.Equal(ResourceJunctionStatus.Removed, item.Status));
        Assert.True(File.Exists(sourceFile));
        Assert.Equal("source remains unchanged", File.ReadAllText(sourceFile));
    }

    [Fact]
    public void WrongJunctionTargetIsNotRemoved()
    {
        if (!OperatingSystem.IsWindows()) return;
        var wrongSource = Path.Combine(root, "wrong-source");
        Directory.CreateDirectory(wrongSource);
        var target = Path.Combine(targetRoot, ResourceJunctionService.ResourceNames[0]);
        CreateJunction(wrongSource, target);
        var service = new ResourceJunctionService(sourceRoot, targetRoot);

        var result = service.RemoveLinks();

        Assert.Equal(ResourceJunctionStatus.WrongTarget, result[0].Status);
        Assert.True(Directory.Exists(target));
    }

    [Fact]
    public void VerifiedJunctionCannotBeReplacedBeforeDeletion()
    {
        if (!OperatingSystem.IsWindows()) return;
        var source = Path.Combine(sourceRoot, ResourceJunctionService.ResourceNames[0]);
        var target = Path.Combine(targetRoot, ResourceJunctionService.ResourceNames[0]);
        CreateJunction(source, target);
        var replacementSucceeded = false;

        var removed = ResourceJunctionService.RemoveVerifiedJunction(
            source,
            target,
            () =>
            {
                try
                {
                    Directory.Delete(target, false);
                    Directory.CreateDirectory(target);
                    replacementSucceeded = true;
                }
                catch (IOException)
                {
                }
            });

        Assert.True(removed);
        Assert.False(replacementSucceeded);
        Assert.False(Directory.Exists(target));
    }

    public void Dispose()
    {
        if (!Directory.Exists(root)) return;
        var service = new ResourceJunctionService(sourceRoot, targetRoot);
        service.RemoveLinks();
        var wrongTarget = Path.Combine(targetRoot, ResourceJunctionService.ResourceNames[0]);
        if (Directory.Exists(wrongTarget) && (File.GetAttributes(wrongTarget) & FileAttributes.ReparsePoint) != 0)
            Directory.Delete(wrongTarget, false);
        Directory.Delete(root, true);
    }

    private static void CreateJunction(string source, string target)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(target);
        startInfo.ArgumentList.Add(source);
        using var process = System.Diagnostics.Process.Start(startInfo)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private string CreateGame(string name, int[] fileCounts)
    {
        var gameRoot = Path.Combine(root, name);
        var a000 = Path.Combine(gameRoot, "Package", "Sinmai_Data", "StreamingAssets", "A000");
        for (var resourceIndex = 0; resourceIndex < ResourceJunctionService.ResourceNames.Count; resourceIndex++)
        {
            var resourceRoot = Path.Combine(a000, ResourceJunctionService.ResourceNames[resourceIndex]);
            var nestedRoot = Path.Combine(resourceRoot, "nested");
            Directory.CreateDirectory(nestedRoot);
            for (var fileIndex = 0; fileIndex < fileCounts[resourceIndex]; fileIndex++)
                File.WriteAllText(Path.Combine(nestedRoot, $"{fileIndex}.dat"), "test");
        }
        return gameRoot;
    }

    private string CreateDirectGame(string name, int[] fileCounts)
    {
        var gameRoot = Path.Combine(root, name);
        var a000 = Path.Combine(gameRoot, "Sinmai_Data", "StreamingAssets", "A000");
        for (var resourceIndex = 0; resourceIndex < ResourceJunctionService.ResourceNames.Count; resourceIndex++)
        {
            var resourceRoot = Path.Combine(a000, ResourceJunctionService.ResourceNames[resourceIndex]);
            Directory.CreateDirectory(resourceRoot);
            for (var fileIndex = 0; fileIndex < fileCounts[resourceIndex]; fileIndex++)
                File.WriteAllText(Path.Combine(resourceRoot, $"{fileIndex}.dat"), "test");
        }
        return gameRoot;
    }
}
