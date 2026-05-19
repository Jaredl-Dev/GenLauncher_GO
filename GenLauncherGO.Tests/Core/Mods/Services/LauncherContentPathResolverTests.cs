using System;
using System.IO;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Mods.Services;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Tests.Testing;

namespace GenLauncherGO.Tests.Core.Mods.Services;

public sealed class LauncherContentPathResolverTests
{
    [Fact]
    public void ResolveVersionPath_WhenIdentityIsMod_ReturnsModVersionDirectory()
    {
        LauncherPaths paths = CreatePaths();
        var version = new LauncherContentVersion
        {
            ModificationType = ModificationType.Mod,
            Name = "Rise Of Reds",
            Version = "1.9"
        };

        OwnedContentPath? result = LauncherContentPathResolver.ResolveVersionPath(paths, version.ContentKey);

        result!.FullPath.Should().Be(Path.Combine(paths.ModsDirectory, "Rise Of Reds", "1.9"));
    }

    [Fact]
    public void ResolveVersionPath_WhenIdentityIsAddon_ReturnsAddonVersionDirectory()
    {
        LauncherPaths paths = CreatePaths();
        var version = new LauncherContentVersion
        {
            ModificationType = ModificationType.Addon,
            ParentContentName = "Rise Of Reds",
            Name = "Music Pack",
            Version = "2.0"
        };

        OwnedContentPath? result = LauncherContentPathResolver.ResolveVersionPath(paths, version.ContentKey);

        result!.FullPath.Should().Be(Path.Combine(
            paths.ModsDirectory,
            "Rise Of Reds",
            LauncherFileSystemLayout.AddonsFolderName,
            "Music Pack",
            "2.0"));
    }

    [Fact]
    public void ResolveVersionPath_WhenIdentityIsPatch_ReturnsPatchVersionDirectory()
    {
        LauncherPaths paths = CreatePaths();
        var version = new LauncherContentVersion
        {
            ModificationType = ModificationType.Patch,
            ParentContentName = "Rise Of Reds",
            Name = "Hotfix",
            Version = "2.1"
        };

        OwnedContentPath? result = LauncherContentPathResolver.ResolveVersionPath(paths, version.ContentKey);

        result!.FullPath.Should().Be(Path.Combine(
            paths.ModsDirectory,
            "Rise Of Reds",
            LauncherFileSystemLayout.PatchesFolderName,
            "Hotfix",
            "2.1"));
    }

    [Fact]
    public void ResolveVersionPath_WhenIdentityTypeIsUnsupported_ReturnsNull()
    {
        LauncherPaths paths = CreatePaths();
        var version = new LauncherContentVersion
        {
            ModificationType = ModificationType.Advertising,
            Name = "News",
            Version = "1"
        };

        OwnedContentPath? result = LauncherContentPathResolver.ResolveVersionPath(paths, version.ContentKey);

        result.Should().BeNull();
    }

    [Fact]
    public void ResolveVersionPath_WhenModNameContainsPathTraversal_Throws()
    {
        LauncherPaths paths = CreatePaths();
        var version = new LauncherContentVersion
        {
            ModificationType = ModificationType.Mod,
            Name = $"..{Path.DirectorySeparatorChar}Escape",
            Version = "1.0"
        };

        Action act = () => LauncherContentPathResolver.ResolveVersionPath(paths, version.ContentKey);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ResolveVersionPath_WhenAddonDependenceContainsPathTraversal_Throws()
    {
        LauncherPaths paths = CreatePaths();
        var version = new LauncherContentVersion
        {
            ModificationType = ModificationType.Addon,
            ParentContentName = $"..{Path.DirectorySeparatorChar}Escape",
            Name = "Music Pack",
            Version = "1.0"
        };

        Action act = () => LauncherContentPathResolver.ResolveVersionPath(paths, version.ContentKey);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ResolveVersionPath_WhenPatchVersionContainsPathTraversal_Throws()
    {
        LauncherPaths paths = CreatePaths();
        var version = new LauncherContentVersion
        {
            ModificationType = ModificationType.Patch,
            ParentContentName = "Rise Of Reds",
            Name = "Hotfix",
            Version = $"..{Path.DirectorySeparatorChar}Escape"
        };

        Action act = () => LauncherContentPathResolver.ResolveVersionPath(paths, version.ContentKey);

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
        var version = new LauncherContentVersion
        {
            ModificationType = modificationType,
            Name = name,
            Version = versionName,
            ParentContentName = parentContentName
        };

        OwnedContentPath? result = LauncherContentPathResolver.ResolveVersionPath(paths, version.ContentKey);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(ModificationType.Mod, null, "ShockWave", "1.2")]
    [InlineData(ModificationType.Addon, "ShockWave", "Music", "2.0")]
    [InlineData(ModificationType.Patch, "ShockWave", "Hotfix", "3.0")]
    public void ResolveVersionPathReturnsOwnedPathForDomainIdentity(
        ModificationType modificationType,
        string? parentContentName,
        string name,
        string versionName)
    {
        LauncherPaths paths = CreatePaths();
        var catalogVersion = new LauncherContentVersion
        {
            ModificationType = modificationType,
            ParentContentName = parentContentName ?? string.Empty,
            Name = name,
            Version = versionName,
        };
        OwnedContentPath? catalogPath = LauncherContentPathResolver.ResolveVersionPath(
            paths,
            catalogVersion.ContentKey);

        catalogPath.Should().NotBeNull();
        catalogPath!.OwnerRoot.Should().Be(Path.GetFullPath(paths.ModsDirectory));
    }

    private static LauncherPaths CreatePaths()
    {
        string root = Path.GetFullPath("GenLauncherGO.Tests");
        return TestLauncherPaths.Create(Path.Combine(root, "Game"));
    }

}
