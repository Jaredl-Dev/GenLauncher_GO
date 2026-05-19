using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Launching.Services;
using GenLauncherGO.Tests.Testing;

namespace GenLauncherGO.Tests.Infrastructure.Launching.Services;

public sealed class FileSystemLaunchContentIntegrityTargetBuilderTests
{
    [Fact]
    public void BuildTargetsUsesLauncherOwnedTempPathsAndIgnoresInactiveCacheFiles()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        Directory.CreateDirectory(paths.ModsDirectory);
        Directory.CreateDirectory(paths.ImagesDirectory);
        LauncherContentVersion activeVersion = CreateVersion("Rise", "1.0");
        LauncherContentVersion inactiveVersion = CreateVersion("Rise", "0.9");
        string cacheDirectory = paths.GetModificationImagesDirectory("Rise");
        Directory.CreateDirectory(cacheDirectory);
        File.WriteAllText(Path.Combine(cacheDirectory, "1.0.png"), "active");
        File.WriteAllText(Path.Combine(cacheDirectory, "0.9.png"), "inactive");
        File.WriteAllText(Path.Combine(cacheDirectory, "0.9-background.jpg"), "inactive background");
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
        cacheTarget.Target.IgnoredRelativePaths.Should().BeEquivalentTo("0.9.png", "0.9-background.jpg");
        cacheTarget.Target.RootDirectory.Should().StartWith(paths.ImagesDirectory);
    }

    private static LauncherContentVersion CreateVersion(string name, string version)
    {
        return new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation { ContentSourceKind = ContentSourceKind.Manual },
            ModificationType = ModificationType.Mod,
            Name = name,
            Version = version,
        };
    }

    private static LauncherPaths CreatePaths(string root)
    {
        return TestLauncherPaths.Create(Path.Combine(root, "Game"));
    }
}
