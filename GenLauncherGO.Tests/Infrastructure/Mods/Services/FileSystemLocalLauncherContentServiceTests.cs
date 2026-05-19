using System;
using System.Collections.Generic;
using System.IO;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Mods.Services;
using GenLauncherGO.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Mods.Services;

public sealed class FileSystemLocalLauncherContentServiceTests
{
    [Fact]
    public void FindInstalledVersionsReturnsInstalledModsPatchesAndAddons()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        FileSystemLocalLauncherContentService service = CreateService();
        Directory.CreateDirectory(Path.Combine(paths.ModsDirectory, "ShockWave", "1.2", "Data", "Empty"));
        CreateFile(Path.Combine(paths.ModsDirectory, "ShockWave", "1.2", "Data", "Real", "INI.big"));
        CreateFile(Path.Combine(paths.ModsDirectory, "ShockWave", "Addons", "HD", "1.0", "HD.big"));
        CreateFile(Path.Combine(paths.ModsDirectory, "ShockWave", "Patches", "Balance", "2.0", "Patch.big"));
        Directory.CreateDirectory(Path.Combine(paths.ModsDirectory, "EmptyMod", "1.0"));

        IReadOnlyList<LauncherContentVersion> versions = service.FindInstalledVersions(paths);

        versions.Should().HaveCount(3);
        versions.Should().ContainSingle(version =>
            version.ModificationType == ModificationType.Mod &&
            version.Name == "ShockWave" &&
            version.Version == "1.2" &&
            version.Installation.Installed);
        versions.Should().ContainSingle(version =>
            version.ModificationType == ModificationType.Addon &&
            version.Name == "HD" &&
            version.Version == "1.0" &&
            version.ParentContentName == "ShockWave" &&
            version.Installation.Installed);
        versions.Should().ContainSingle(version =>
            version.ModificationType == ModificationType.Patch &&
            version.Name == "Balance" &&
            version.Version == "2.0" &&
            version.ParentContentName == "ShockWave" &&
            version.Installation.Installed);
        versions.Should().NotContain(version => version.Name == "EmptyMod");
    }

    [Fact]
    public void DeleteVersionDeletesVersionAndPrunesEmptyParents()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        FileSystemLocalLauncherContentService service = CreateService();
        string versionDirectory = Path.Combine(paths.ModsDirectory, "ShockWave", "Addons", "HD", "1.0");
        CreateFile(Path.Combine(versionDirectory, "HD.big"));
        var version = new LauncherContentVersion
        {
            ModificationType = ModificationType.Addon,
            Name = "HD",
            Version = "1.0",
            ParentContentName = "ShockWave"
        };

        service.DeleteVersion(paths, version.ContentKey);

        Directory.Exists(versionDirectory).Should().BeFalse();
        Directory.Exists(Path.Combine(paths.ModsDirectory, "ShockWave")).Should().BeFalse();
        Directory.Exists(paths.ModsDirectory).Should().BeTrue();
    }

    [Fact]
    public void DeleteVersionDeletesPackageStagingFolderWhenInstalledFolderIsMissing()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        FileSystemLocalLauncherContentService service = CreateService();
        string versionDirectory = Path.Combine(paths.ModsDirectory, "ShockWave", "1.2");
        string packageStagingDirectory = paths.GetPackageTemporaryPath(
            new OwnedContentPath(paths.ModsDirectory, versionDirectory)).FullPath;
        CreateFile(Path.Combine(packageStagingDirectory, "Data", "INI.big"));
        var version = new LauncherContentVersion
        {
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = "1.2"
        };

        service.DeleteVersion(paths, version.ContentKey);

        Directory.Exists(packageStagingDirectory).Should().BeFalse();
        Directory.Exists(Path.Combine(paths.TempDirectory, "Packages", "ShockWave")).Should().BeFalse();
        Directory.Exists(versionDirectory).Should().BeFalse();
    }

    [Fact]
    public void DeleteVersionDeletesPackageStagingFolderForChildContentWhenInstalledFolderIsMissing()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        FileSystemLocalLauncherContentService service = CreateService();
        string versionDirectory = Path.Combine(paths.ModsDirectory, "ShockWave", "Addons", "HD", "1.0");
        string packageStagingDirectory = paths.GetPackageTemporaryPath(
            new OwnedContentPath(paths.ModsDirectory, versionDirectory)).FullPath;
        CreateFile(Path.Combine(packageStagingDirectory, "HD.big"));
        var version = new LauncherContentVersion
        {
            ModificationType = ModificationType.Addon,
            Name = "HD",
            Version = "1.0",
            ParentContentName = "ShockWave"
        };

        service.DeleteVersion(paths, version.ContentKey);

        Directory.Exists(packageStagingDirectory).Should().BeFalse();
        Directory.Exists(Path.Combine(paths.TempDirectory, "Packages", "ShockWave")).Should().BeFalse();
        Directory.Exists(versionDirectory).Should().BeFalse();
    }

    [Fact]
    public void DeleteContentDeletesModRootAndPackageStagingRoot()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        FileSystemLocalLauncherContentService service = CreateService();
        string contentDirectory = Path.Combine(paths.ModsDirectory, "ShockWave");
        string packageStagingDirectory = paths.GetPackageTemporaryPath(
            new OwnedContentPath(paths.ModsDirectory, contentDirectory)).FullPath;
        CreateFile(Path.Combine(contentDirectory, "1.2", "Data", "INI.big"));
        CreateFile(Path.Combine(contentDirectory, "Addons", "HD", "1.0", "HD.big"));
        CreateFile(Path.Combine(packageStagingDirectory, "1.2", "Data", "INI.big"));
        var version = new LauncherContentVersion
        {
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = "1.2"
        };

        service.DeleteContent(paths, version.ContentKey);

        Directory.Exists(contentDirectory).Should().BeFalse();
        Directory.Exists(packageStagingDirectory).Should().BeFalse();
        Directory.Exists(paths.ModsDirectory).Should().BeTrue();
    }

    [Fact]
    public void DeleteContentDeletesChildContentRoot()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        FileSystemLocalLauncherContentService service = CreateService();
        string contentDirectory = Path.Combine(paths.ModsDirectory, "ShockWave", "Addons", "HD");
        CreateFile(Path.Combine(contentDirectory, "1.0", "HD.big"));
        CreateFile(Path.Combine(contentDirectory, "2.0", "HD.big"));
        var version = new LauncherContentVersion
        {
            ModificationType = ModificationType.Addon,
            Name = "HD",
            Version = "1.0",
            ParentContentName = "ShockWave"
        };

        service.DeleteContent(paths, version.ContentKey);

        Directory.Exists(contentDirectory).Should().BeFalse();
        Directory.Exists(Path.Combine(paths.ModsDirectory, "ShockWave")).Should().BeFalse();
    }

    [Fact]
    public void DeleteVersionRefusesPathOutsideModsRoot()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        FileSystemLocalLauncherContentService service = CreateService();
        var version = new LauncherContentVersion
        {
            ModificationType = ModificationType.Mod,
            Name = "..",
            Version = "Outside"
        };

        Action act = () => service.DeleteVersion(paths, version.ContentKey);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DeleteImagesIfUnusedDeletesVersionImagesWhenNoCardReferencesContentName()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        FileSystemLocalLauncherContentService service = CreateService();
        string imageDirectory = paths.GetModificationImagesDirectory("ShockWave");
        string cardImage = Path.Combine(imageDirectory, "1.2.png");
        string backgroundImage = Path.Combine(imageDirectory, "1.2-background.jpg");
        string otherImage = Path.Combine(imageDirectory, "readme.txt");
        CreateFile(cardImage);
        CreateFile(backgroundImage);
        CreateFile(otherImage);
        var version = new LauncherContentVersion
        {
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = "1.2"
        };

        service.DeleteImagesIfUnused(paths, version.ContentKey, new LauncherData());

        File.Exists(cardImage).Should().BeFalse();
        File.Exists(backgroundImage).Should().BeFalse();
        File.Exists(otherImage).Should().BeTrue();
        Directory.Exists(imageDirectory).Should().BeTrue();
    }

    [Fact]
    public void DeleteImagesIfUnusedKeepsImagesWhenCardStillReferencesContentName()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        FileSystemLocalLauncherContentService service = CreateService();
        string imagePath = Path.Combine(paths.GetModificationImagesDirectory("ShockWave"), "1.2.png");
        CreateFile(imagePath);
        var version = new LauncherContentVersion
        {
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = "1.2"
        };
        var launcherData = new LauncherData();
        launcherData.AddOrUpdate(new LauncherContentVersion
        {
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = "1.0"
        });

        service.DeleteImagesIfUnused(paths, version.ContentKey, launcherData);

        File.Exists(imagePath).Should().BeTrue();
    }

    private static FileSystemLocalLauncherContentService CreateService()
    {
        return new FileSystemLocalLauncherContentService(
            NullLogger<FileSystemLocalLauncherContentService>.Instance);
    }

    private static LauncherPaths CreatePaths(string root)
    {
        return TestLauncherPaths.Create(root);
    }

    private static void CreateFile(string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, String.Empty);
    }
}
