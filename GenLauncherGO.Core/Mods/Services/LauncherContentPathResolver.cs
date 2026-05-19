using System;
using System.IO;
using System.Linq;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Core.Mods.Services;

/// <summary>
///     Resolves launcher-owned content paths from canonical content identities.
/// </summary>
public static class LauncherContentPathResolver
{
    /// <summary>
    ///     Resolves the owned installed version directory, or returns <see langword="null" /> for an incomplete or
    ///     unsupported identity.
    /// </summary>
    public static OwnedContentPath? ResolveVersionPath(
        LauncherPaths paths,
        LauncherContentKey contentKey)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (string.IsNullOrWhiteSpace(contentKey.Version))
        {
            return null;
        }

        OwnedContentPath? contentPath = ResolveContentPath(paths, contentKey);
        if (contentPath is null)
        {
            return null;
        }

        string safeVersion = LexicalPath.NormalizePathSegment(contentKey.Version, nameof(contentKey.Version));
        string versionPath = LexicalPath.ResolvePath(contentPath.FullPath, safeVersion);
        return new OwnedContentPath(contentPath.OwnerRoot, versionPath);
    }

    /// <summary>
    ///     Resolves the owned content-card directory, or returns <see langword="null" /> for an incomplete identity.
    /// </summary>
    public static OwnedContentPath? ResolveContentPath(
        LauncherPaths paths,
        LauncherContentKey contentKey)
    {
        ArgumentNullException.ThrowIfNull(paths);
        string[]? pathSegments = ResolveContentPathSegments(contentKey);
        return pathSegments is null
            ? null
            : ResolvePackagePath(paths.ModsDirectory, pathSegments);
    }

    /// <summary>
    ///     Resolves the owned cleanup root, or returns <see langword="null" /> for an incomplete identity.
    /// </summary>
    public static OwnedContentPath? ResolveCleanupRootPath(
        LauncherPaths paths,
        LauncherContentKey contentKey)
    {
        ArgumentNullException.ThrowIfNull(paths);
        string[]? pathSegments = ResolveContentPathSegments(contentKey);
        return pathSegments is null
            ? null
            : ResolvePackagePath(paths.ModsDirectory, pathSegments[0]);
    }

    private static string[]? ResolveContentPathSegments(LauncherContentKey contentKey)
    {
        if (string.IsNullOrWhiteSpace(contentKey.Name))
        {
            return null;
        }

        return contentKey.ContentType switch
        {
            ModificationType.Addon when !string.IsNullOrWhiteSpace(contentKey.ParentIdentity) =>
                [contentKey.ParentIdentity, LauncherFileSystemLayout.AddonsFolderName, contentKey.Name],
            ModificationType.Patch when !string.IsNullOrWhiteSpace(contentKey.ParentIdentity) =>
                [contentKey.ParentIdentity, LauncherFileSystemLayout.PatchesFolderName, contentKey.Name],
            ModificationType.Mod => [contentKey.Name],
            _ => null
        };
    }

    private static OwnedContentPath ResolvePackagePath(string modsDirectory, params string?[] segments)
    {
        string[] safeSegments = segments
            .Select((segment, index) => LexicalPath.NormalizePathSegment(segment, $"segment{index}"))
            .ToArray();
        string fullPath = LexicalPath.ResolvePath(modsDirectory, Path.Combine(safeSegments));
        return new OwnedContentPath(modsDirectory, fullPath);
    }
}
