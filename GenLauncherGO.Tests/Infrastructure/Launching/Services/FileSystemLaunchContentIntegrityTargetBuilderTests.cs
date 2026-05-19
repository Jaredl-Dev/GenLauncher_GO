using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Launching.Services;

namespace GenLauncherGO.Tests.Infrastructure.Launching.Services;

public sealed class FileSystemLaunchContentIntegrityTargetBuilderTests
{
    [Fact]
    public void Build_TargetsUsesLauncherOwnedTempPathsAndIgnoresInactiveCacheFiles()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        LauncherContentVersion activeVersion = CreateVersion("Rise", "1.0");
        LauncherContentVersion inactiveVersion = CreateVersion("Rise", "0.9");
        string cacheDirectory = paths.GetModificationImagesDirectory("Rise");
        Directory.CreateDirectory(cacheDirectory);
        File.WriteAllText(Path.Combine(cacheDirectory, "1.0.png"), "active");
        File.WriteAllText(Path.Combine(cacheDirectory, "0.9.png"), "inactive");
        File.WriteAllText(Path.Combine(cacheDirectory, LauncherContentTheme.ResolveBackgroundImageBaseName("0.9") + ".jpg"), "inactive background");
        var builder = new FileSystemLaunchContentIntegrityTargetBuilder();

        IReadOnlyList<LaunchContentIntegrityTargetContext> targets = builder.BuildTargets(
            new LaunchContentIntegrityTargetRequest(
                paths,
                new[] { activeVersion },
                new[] { activeVersion, inactiveVersion },
                "cache"));

        targets.Should().HaveCount(2);
        LaunchContentIntegrityTargetContext packageTarget = targets.Single(target => !target.IsCache);
        packageTarget.Target.RootDirectory.Should().Be(Path.Combine(paths.ModsDirectory, "Rise", "1.0"));
        packageTarget.Target.SourceKind.Should().Be(ContentSourceKind.Manual);
        LaunchContentIntegrityTargetContext cacheTarget = targets.Single(target => target.IsCache);
        cacheTarget.Target.RootDirectory.Should().Be(cacheDirectory);
        cacheTarget.Target.IgnoredRelativePaths.Should().BeEquivalentTo("0.9.png", LauncherContentTheme.ResolveBackgroundImageBaseName("0.9") + ".jpg");
        cacheTarget.Target.RootDirectory.Should().StartWith(paths.ImagesDirectory);
    }

    [Fact]
    public void BuildTargets_WhenTheImageCacheDirectoryIsMissing_IgnoresNothing()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        LauncherContentVersion activeVersion = CreateVersion("Rise", "1.0");
        LauncherContentVersion inactiveVersion = CreateVersion("Rise", "0.9");
        var builder = new FileSystemLaunchContentIntegrityTargetBuilder();

        IReadOnlyList<LaunchContentIntegrityTargetContext> targets = builder.BuildTargets(
            new LaunchContentIntegrityTargetRequest(
                paths,
                new[] { activeVersion },
                new[] { activeVersion, inactiveVersion },
                "cache"));

        targets.Single(target => target.IsCache).Target.IgnoredRelativePaths.Should().BeEmpty();
    }

    private static LauncherContentVersion CreateVersion(string name, string version)
    {
        return TestLauncherContent.Version(name, version, sourceKind: ContentSourceKind.Manual);
    }
}
