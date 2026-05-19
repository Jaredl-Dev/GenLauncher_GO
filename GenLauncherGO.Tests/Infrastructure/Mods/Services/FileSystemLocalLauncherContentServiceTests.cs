using System;
using System.Collections.Generic;
using System.IO;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Mods.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Mods.Services;

public sealed class FileSystemLocalLauncherContentServiceTests
{
    [Fact]
    public void FindInstalledVersions_ReturnsInstalledModsPatchesAndAddons()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
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

    /// <summary>
    ///     A linked Mods tree is refused outright rather than walked, because the versions it would report are folders
    ///     the launcher does not own and would later delete on the user's behalf.
    /// </summary>
    [Fact]
    public void FindInstalledVersions_RefusesModsTreeContainingLinkedDirectory()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        FileSystemLocalLauncherContentService service = CreateService();
        CreateFile(Path.Combine(paths.ModsDirectory, "ShockWave", "1.2", "Data", "INI.big"));
        string outsideDirectory = directory.CreateDirectory("OutsideMods");
        string outsideVersionFile = directory.CreateFile("OutsideMods/1.0/Outside.big", "outside");
        ReparsePointTestSupport.CreateDirectoryJunction(
            Path.Combine(paths.ModsDirectory, "Linked"),
            outsideDirectory);

        Action act = () => service.FindInstalledVersions(paths);

        act.Should().Throw<InvalidDataException>();
        File.ReadAllText(outsideVersionFile).Should().Be("outside");
    }

    /// <summary>
    ///     A launcher-owned data root that is itself a link resolves to a tree the launcher never created, and every
    ///     version reported from it would later be deleted on the user's behalf, so the scan is refused before it
    ///     walks a single folder.
    /// </summary>
    [Fact]
    public void FindInstalledVersions_RefusesModsTreeReachedThroughALinkedAncestor()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        FileSystemLocalLauncherContentService service = CreateService();
        Directory.Delete(paths.OwnedGameDataDirectory, true);
        string outsideDirectory = directory.CreateDirectory("OutsideData");
        string outsideVersionFile = directory.CreateFile("OutsideData/Mods/ShockWave/1.2/Outside.big", "outside");
        ReparsePointTestSupport.CreateDirectoryJunction(paths.OwnedGameDataDirectory, outsideDirectory);

        Action act = () => service.FindInstalledVersions(paths);

        act.Should().Throw<InvalidDataException>();
        File.ReadAllText(outsideVersionFile).Should().Be("outside");
    }

    [Fact]
    public void FindInstalledVersions_ReturnsNothingWhenTheModsFolderHasNotBeenCreated()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        FileSystemLocalLauncherContentService service = CreateService();

        IReadOnlyList<LauncherContentVersion> versions = service.FindInstalledVersions(paths);

        versions.Should().BeEmpty();
    }

    /// <summary>
    ///     An empty version folder is a leftover, not an installation. That holds for a patch or add-on version folder
    ///     exactly as it does for a modification's own, or the launcher would offer to launch content with no files.
    /// </summary>
    [Fact]
    public void FindInstalledVersions_IgnoresEmptyChildVersionDirectories()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        FileSystemLocalLauncherContentService service = CreateService();
        CreateFile(Path.Combine(paths.ModsDirectory, "ShockWave", "1.2", "INI.big"));
        Directory.CreateDirectory(Path.Combine(paths.ModsDirectory, "ShockWave", "Addons", "HD", "1.0"));
        Directory.CreateDirectory(Path.Combine(paths.ModsDirectory, "ShockWave", "Patches", "Balance", "2.0"));

        IReadOnlyList<LauncherContentVersion> versions = service.FindInstalledVersions(paths);

        versions.Should().ContainSingle().Which.Name.Should().Be("ShockWave");
    }

    [Fact]
    public void DeleteVersion_DeletesVersionAndPrunesEmptyParents()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
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
    public void DeleteVersion_DeletesPackageStagingAndRecoveryFoldersWhenInstalledFolderIsMissing()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        FileSystemLocalLauncherContentService service = CreateService();
        string versionDirectory = Path.Combine(paths.ModsDirectory, "ShockWave", "1.2");
        var installedPath = new OwnedContentPath(paths.ModsDirectory, versionDirectory);
        string packageStagingDirectory = paths.GetPackageTemporaryPath(installedPath).FullPath;
        OwnedContentPath packageBackupPath = paths.GetPackageBackupPath(installedPath);
        CreateFile(Path.Combine(packageStagingDirectory, "Data", "INI.big"));
        CreateFile(Path.Combine(packageBackupPath.FullPath, "Data", "OldINI.big"));
        var version = new LauncherContentVersion
        {
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = "1.2"
        };

        service.DeleteVersion(paths, version.ContentKey);

        Directory.Exists(packageStagingDirectory).Should().BeFalse();
        Directory.Exists(Path.Combine(paths.TempDirectory, "Packages", "ShockWave")).Should().BeFalse();
        Directory.Exists(packageBackupPath.FullPath).Should().BeFalse();
        Directory.Exists(Path.Combine(packageBackupPath.OwnerRoot, "ShockWave")).Should().BeFalse();
        Directory.Exists(packageBackupPath.OwnerRoot).Should().BeFalse();
        Directory.Exists(versionDirectory).Should().BeFalse();
    }

    [Fact]
    public void DeleteVersion_DeletesPackageStagingFolderForChildContentWhenInstalledFolderIsMissing()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
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
    public void DeleteContent_DeletesModRootAndPackageStagingAndRecoveryRoots()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        FileSystemLocalLauncherContentService service = CreateService();
        string contentDirectory = Path.Combine(paths.ModsDirectory, "ShockWave");
        var installedPath = new OwnedContentPath(paths.ModsDirectory, contentDirectory);
        string packageStagingDirectory = paths.GetPackageTemporaryPath(installedPath).FullPath;
        OwnedContentPath packageBackupPath = paths.GetPackageBackupPath(installedPath);
        CreateFile(Path.Combine(contentDirectory, "1.2", "Data", "INI.big"));
        CreateFile(Path.Combine(contentDirectory, "Addons", "HD", "1.0", "HD.big"));
        CreateFile(Path.Combine(packageStagingDirectory, "1.2", "Data", "INI.big"));
        CreateFile(Path.Combine(packageBackupPath.FullPath, "1.2", "Data", "OldINI.big"));
        var version = new LauncherContentVersion
        {
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = "1.2"
        };

        service.DeleteContent(paths, version.ContentKey);

        Directory.Exists(contentDirectory).Should().BeFalse();
        Directory.Exists(packageStagingDirectory).Should().BeFalse();
        Directory.Exists(packageBackupPath.FullPath).Should().BeFalse();
        Directory.Exists(packageBackupPath.OwnerRoot).Should().BeFalse();
        Directory.Exists(paths.ModsDirectory).Should().BeTrue();
    }

    [Fact]
    public void DeleteContent_DeletesChildContentRoot()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
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
    public void DeleteEmptyPackageBackupDirectories_RemovesOnlyEmptyRecoveryDirectories()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        FileSystemLocalLauncherContentService service = CreateService();
        string emptyBackupDirectory = Path.Combine(paths.PackageBackupsDirectory, "Unused", "1.0");
        string retainedBackupFile = Path.Combine(paths.PackageBackupsDirectory, "Active", "1.0", "asset.big");
        Directory.CreateDirectory(emptyBackupDirectory);
        CreateFile(retainedBackupFile);

        service.DeleteEmptyPackageBackupDirectories(paths);

        Directory.Exists(emptyBackupDirectory).Should().BeFalse();
        File.Exists(retainedBackupFile).Should().BeTrue();
        Directory.Exists(paths.PackageBackupsDirectory).Should().BeTrue();
    }

    /// <summary>
    ///     Removing a version the launcher does not have installed deletes nothing, so it must not prune folders as a
    ///     side effect either: the tree is only tidied after something was actually removed from it.
    /// </summary>
    [Fact]
    public void DeleteVersion_LeavesTheContentFolderWhenTheVersionIsNotInstalled()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        FileSystemLocalLauncherContentService service = CreateService();
        string contentDirectory = Path.Combine(paths.ModsDirectory, "ShockWave");
        Directory.CreateDirectory(contentDirectory);
        var version = new LauncherContentVersion
        {
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = "1.2"
        };

        service.DeleteVersion(paths, version.ContentKey);

        Directory.Exists(contentDirectory).Should().BeTrue();
    }

    [Fact]
    public void DeleteVersion_RefusesPathOutsideModsRoot()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
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
    public void DeleteImagesIfUnused_DeletesOwnedCacheFolderWhenNoCardReferencesContentName()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        FileSystemLocalLauncherContentService service = CreateService();
        string imageDirectory = paths.GetModificationImagesDirectory("ShockWave");
        string cardImage = Path.Combine(imageDirectory, "1.2.png");
        string backgroundImage = Path.Combine(
            imageDirectory,
            LauncherContentTheme.ResolveBackgroundImageBaseName("1.2") + ".jpg");
        string cachedTheme = Path.Combine(
            imageDirectory,
            LauncherContentTheme.ResolveCacheBaseName("1.2") + ".yaml");
        string otherImage = Path.Combine(imageDirectory, "readme.txt");
        CreateFile(cardImage);
        CreateFile(backgroundImage);
        CreateFile(cachedTheme);
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
        File.Exists(cachedTheme).Should().BeFalse();
        File.Exists(otherImage).Should().BeFalse();
        Directory.Exists(imageDirectory).Should().BeFalse();
    }

    /// <summary>
    ///     Someone can point a modification's image cache at a folder of their own. Removing the content card must
    ///     still clear the cache entry, and must do it by unlinking rather than by deleting what is on the far side.
    /// </summary>
    [Fact]
    public void DeleteImagesIfUnused_RemovesLinkedImageCacheWithoutDeletingItsTarget()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        FileSystemLocalLauncherContentService service = CreateService();
        string outsideDirectory = directory.CreateDirectory("OutsideImages");
        string firstOutsideImage = directory.CreateFile("OutsideImages/1.2.png", "first");
        string secondOutsideImage = directory.CreateFile("OutsideImages/holiday.png", "second");
        string imageDirectory = paths.GetModificationImagesDirectory("ShockWave");
        ReparsePointTestSupport.CreateDirectoryJunction(imageDirectory, outsideDirectory);
        var version = new LauncherContentVersion
        {
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = "1.2"
        };

        service.DeleteImagesIfUnused(paths, version.ContentKey, new LauncherData());

        Directory.Exists(imageDirectory).Should().BeFalse();
        File.ReadAllText(firstOutsideImage).Should().Be("first");
        File.ReadAllText(secondOutsideImage).Should().Be("second");
    }

    [Fact]
    public void DeleteImagesIfUnused_KeepsImagesWhenCardStillReferencesContentName()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
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

    private static void CreateFile(string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, string.Empty);
    }
}
