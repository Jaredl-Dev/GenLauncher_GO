using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Mods.Services;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Infrastructure.Launching.Contracts;
using GenLauncherGO.Infrastructure.Mods.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Infrastructure.Launching.Services;

/// <summary>
///     Builds launch-readiness integrity targets from launcher-owned file-system paths.
/// </summary>
internal sealed class FileSystemLaunchContentIntegrityTargetBuilder : ILaunchContentIntegrityTargetBuilder
{
    private static readonly HashSet<string> _emptyIgnoredPaths = new(StringComparer.OrdinalIgnoreCase);

    private readonly ILogger<FileSystemLaunchContentIntegrityTargetBuilder> _logger;

    public FileSystemLaunchContentIntegrityTargetBuilder(
        ILogger<FileSystemLaunchContentIntegrityTargetBuilder>? logger = null)
    {
        _logger = logger ?? NullLogger<FileSystemLaunchContentIntegrityTargetBuilder>.Instance;
    }

    public IReadOnlyList<LaunchContentIntegrityTargetContext> BuildTargets(
        LaunchContentIntegrityTargetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contexts = new List<LaunchContentIntegrityTargetContext>();
        foreach (LauncherContentVersion version in request.ActiveVersions)
        {
            contexts.Add(new LaunchContentIntegrityTargetContext(
                CreatePackageTarget(request, version),
                version,
                false));

            if (version.ModificationType == ModificationType.Mod)
            {
                contexts.Add(new LaunchContentIntegrityTargetContext(
                    CreateCacheTarget(request, version),
                    version,
                    true));
            }
        }

        if (contexts.Count > 0)
        {
            _logger.LogInformation(
                "Built {TargetCount} launch content integrity target(s) for {VersionCount} active version(s).",
                contexts.Count,
                request.ActiveVersions.Count);
        }
        else
        {
            _logger.LogDebug(
                "Skipped launch content integrity target construction because no active versions were selected.");
        }

        return contexts;
    }

    private static ContentIntegrityTarget CreatePackageTarget(
        LaunchContentIntegrityTargetRequest request,
        LauncherContentVersion version)
    {
        OwnedContentPath packagePath = LauncherContentPathResolver.ResolveVersionPath(
                                           request.Paths,
                                           version.ContentKey)
                                       ?? throw new InvalidDataException(
                                           "Content metadata did not resolve to a supported launcher content path.");
        string packageDirectory = FileSystemPathSafety.ResolveOwnedSubpath(
            packagePath.OwnerRoot,
            packagePath.FullPath,
            "Content metadata paths",
            "a launcher-owned directory");

        return new ContentIntegrityTarget(
            CreateTargetId("package", version.ContentKey),
            version.DisplayName,
            packageDirectory,
            version.EffectiveContentSourceKind,
            _emptyIgnoredPaths);
    }

    private ContentIntegrityTarget CreateCacheTarget(
        LaunchContentIntegrityTargetRequest request,
        LauncherContentVersion version)
    {
        string cacheDirectory = ModificationImageCachePath.ResolveDirectory(request.Paths, version.Name);
        HashSet<string> ignoredPaths = BuildCacheIgnoredPaths(request, version, cacheDirectory);

        return new ContentIntegrityTarget(
            CreateTargetId("cache", version.ContentKey),
            version.DisplayName + " " + request.CacheDisplayNameSuffix,
            cacheDirectory,
            version.EffectiveContentSourceKind,
            ignoredPaths);
    }

    /// <summary>
    ///     Builds ignored cache paths for inactive versions and the active version's locally serialized palette.
    /// </summary>
    private HashSet<string> BuildCacheIgnoredPaths(
        LaunchContentIntegrityTargetRequest request,
        LauncherContentVersion version,
        string cacheDirectory)
    {
        var ignoredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(cacheDirectory))
        {
            _logger.LogDebug(
                "Skipped inactive image-cache ignore discovery for {ContentName} {ContentVersion} because the cache directory does not exist.",
                version.Name,
                version.Version);
            return ignoredPaths;
        }

        if (FileSystemPathSafety.IsReparsePoint(cacheDirectory))
        {
            _logger.LogWarning(
                "Skipped inactive image-cache ignore discovery for {ContentName} {ContentVersion} because the cache directory is a reparse point.",
                version.Name,
                version.Version);
            return ignoredPaths;
        }

        var ignoredBaseNames = request.AllVersions
            .Where(candidate =>
                candidate.ContentKey != version.ContentKey &&
                candidate.ContentKey.HasName(version.Name))
            .SelectMany(candidate => new[]
            {
                candidate.Version,
                LauncherContentTheme.ResolveBackgroundImageBaseName(candidate.Version),
                LauncherContentTheme.ResolveCacheBaseName(candidate.Version)
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (version.Theme != null)
        {
            ignoredBaseNames.Add(LauncherContentTheme.ResolveCacheBaseName(version.Version));
        }

        foreach (string filePath in Directory.EnumerateFiles(
                     cacheDirectory,
                     "*",
                     FileSystemPathSafety.CreateRecursiveNoLinksOptions()))
        {
            string relativePath = LexicalPath.GetRelativePath(cacheDirectory, filePath);
            if (ignoredBaseNames.Contains(Path.GetFileNameWithoutExtension(filePath)))
            {
                ignoredPaths.Add(relativePath);
            }
        }

        if (ignoredPaths.Count > 0)
        {
            _logger.LogDebug(
                "Ignored {IgnoredPathCount} local or inactive image-cache file(s) while building integrity target for {ContentName} {ContentVersion}.",
                ignoredPaths.Count,
                version.Name,
                version.Version);
        }

        return ignoredPaths;
    }

    /// <summary>
    ///     Creates a stable target identifier.
    /// </summary>
    private static string CreateTargetId(string prefix, LauncherContentKey contentKey)
    {
        return string.Concat(prefix, ":", contentKey.ToStableString());
    }

}
