using System;
using System.IO;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Updating.Models;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Builds package ownership boundaries from the launcher's own directories, so no test restates where staging,
///     installed content, or recovery backups live.
/// </summary>
internal static class TestPackageUpdatePaths
{
    public static PackageUpdatePathSet Create(
        LauncherPaths paths,
        string temporaryRelativePath,
        string installedRelativePath,
        string? latestRelativePath = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryRelativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(installedRelativePath);

        return new PackageUpdatePathSet(
            Owned(paths.PackagesDirectory, temporaryRelativePath),
            Owned(paths.ModsDirectory, installedRelativePath),
            Owned(paths.PackageBackupsDirectory, installedRelativePath),
            latestRelativePath is null ? null : Owned(paths.ModsDirectory, latestRelativePath));
    }

    private static OwnedContentPath Owned(string ownerRoot, string relativePath)
    {
        return new OwnedContentPath(ownerRoot, Path.Combine(ownerRoot, relativePath));
    }
}
