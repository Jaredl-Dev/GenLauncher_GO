using System;
using System.IO;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Mods.Services;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Tests.Core.Mods.Services;

public sealed class LauncherContentPathResolverTests
{
    [Theory]
    [InlineData(ModificationType.Mod, null, "Rise Of Reds", "1.9", "Rise Of Reds/1.9")]
    [InlineData(ModificationType.Addon, "Rise Of Reds", "Music Pack", "2.0", "Rise Of Reds/Addons/Music Pack/2.0")]
    [InlineData(ModificationType.Patch, "Rise Of Reds", "Hotfix", "2.1", "Rise Of Reds/Patches/Hotfix/2.1")]
    public void ResolveVersionPath_WhenIdentityIsSupported_ReturnsOwnedVersionDirectory(
        ModificationType modificationType,
        string? parentContentName,
        string name,
        string version,
        string expectedRelativePath)
    {
        LauncherPaths paths = CreatePaths();
        LauncherContentKey contentKey = new(modificationType, parentContentName, name, version);

        OwnedContentPath? result = LauncherContentPathResolver.ResolveVersionPath(paths, contentKey);

        AssertOwnedPath(result, paths, expectedRelativePath);
    }

    [Fact]
    public void ResolveVersionPath_WhenIdentityTypeIsUnsupported_ReturnsNull()
    {
        LauncherPaths paths = CreatePaths();
        LauncherContentKey contentKey = new(ModificationType.Advertising, null, "News", "1");

        OwnedContentPath? result = LauncherContentPathResolver.ResolveVersionPath(paths, contentKey);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(ModificationType.Mod, null, "../Escape", "1.0")]
    [InlineData(ModificationType.Addon, "../Escape", "Music Pack", "1.0")]
    [InlineData(ModificationType.Patch, "Rise Of Reds", "Hotfix", "../Escape")]
    [InlineData(ModificationType.Mod, null, "CON", "1.0")]
    [InlineData(ModificationType.Mod, null, "Rise Of Reds", "1.0.")]
    [InlineData(ModificationType.Addon, @"C:\Escape", "Music Pack", "1.0")]
    public void ResolveVersionPath_WhenIdentityIsUnsafeForAPathSegment_Throws(
        ModificationType modificationType,
        string? parentContentName,
        string name,
        string version)
    {
        LauncherPaths paths = CreatePaths();
        LauncherContentKey contentKey = new(modificationType, parentContentName, name, version);

        Action act = () => LauncherContentPathResolver.ResolveVersionPath(paths, contentKey);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(ModificationType.Mod, "", "1.0", "")]
    [InlineData(ModificationType.Mod, "ShockWave", "", "")]
    [InlineData(ModificationType.Addon, "HD", "1.0", "")]
    [InlineData(ModificationType.Patch, "Balance", "1.0", "")]
    public void ResolveVersionPath_WhenIdentityIsIncomplete_ReturnsNull(
        ModificationType modificationType,
        string name,
        string versionName,
        string parentContentName)
    {
        LauncherPaths paths = CreatePaths();
        LauncherContentKey contentKey = new(modificationType, parentContentName, name, versionName);

        OwnedContentPath? result = LauncherContentPathResolver.ResolveVersionPath(paths, contentKey);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(ModificationType.Mod, null, "ShockWave", "ShockWave", "ShockWave")]
    [InlineData(ModificationType.Addon, "ShockWave", "Music", "ShockWave/Addons/Music", "ShockWave")]
    [InlineData(ModificationType.Patch, "ShockWave", "Hotfix", "ShockWave/Patches/Hotfix", "ShockWave")]
    public void ResolveContentPaths_WhenIdentityIsSupported_ReturnsOwnedMappedDirectories(
        ModificationType modificationType,
        string? parentContentName,
        string name,
        string expectedContentRelativePath,
        string expectedCleanupRelativePath)
    {
        LauncherPaths paths = CreatePaths();
        LauncherContentKey contentKey = new(modificationType, parentContentName, name, "1.0");

        OwnedContentPath? contentPath = LauncherContentPathResolver.ResolveContentPath(paths, contentKey);
        OwnedContentPath? cleanupPath = LauncherContentPathResolver.ResolveCleanupRootPath(paths, contentKey);

        AssertOwnedPath(contentPath, paths, expectedContentRelativePath);
        AssertOwnedPath(cleanupPath, paths, expectedCleanupRelativePath);
    }

    [Theory]
    [InlineData(ModificationType.Mod, null, "")]
    [InlineData(ModificationType.Addon, null, "Music")]
    [InlineData(ModificationType.Patch, "", "Hotfix")]
    [InlineData(ModificationType.Advertising, null, "News")]
    public void ResolveContentPaths_WhenIdentityIsIncompleteOrUnsupported_ReturnNull(
        ModificationType modificationType,
        string? parentContentName,
        string name)
    {
        LauncherPaths paths = CreatePaths();
        LauncherContentKey contentKey = new(modificationType, parentContentName, name, "1.0");

        OwnedContentPath? contentPath = LauncherContentPathResolver.ResolveContentPath(paths, contentKey);
        OwnedContentPath? cleanupPath = LauncherContentPathResolver.ResolveCleanupRootPath(paths, contentKey);

        contentPath.Should().BeNull();
        cleanupPath.Should().BeNull();
    }

    private static void AssertOwnedPath(
        OwnedContentPath? path,
        LauncherPaths paths,
        string expectedRelativePath)
    {
        string ownerRoot = Path.GetFullPath(paths.ModsDirectory);
        string normalizedRelativePath = expectedRelativePath.Replace('/', Path.DirectorySeparatorChar);

        path.Should().NotBeNull();
        path!.OwnerRoot.Should().Be(ownerRoot);
        path.FullPath.Should().Be(Path.Combine(ownerRoot, normalizedRelativePath));
    }

    private static LauncherPaths CreatePaths()
    {
        return TestLauncherPaths.CreateVirtualRoot("LauncherContentPathResolver");
    }
}
