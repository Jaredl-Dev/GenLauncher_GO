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
/// Builds launch-readiness integrity targets from launcher-owned file-system paths.
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
                isCache: false));

            if (version.ModificationType == ModificationType.Mod)
            {
                contexts.Add(new LaunchContentIntegrityTargetContext(
                    CreateCacheTarget(request, version),
                    version,
                    isCache: true));
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
            _logger.LogDebug("Skipped launch content integrity target construction because no active versions were selected.");
        }

        return contexts;
    }

    private ContentIntegrityTarget CreatePackageTarget(
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
            "Content metadata resolved outside a launcher-owned directory.",
            "Content metadata resolved through a linked launcher-owned directory.");

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
        HashSet<string> ignoredPaths = BuildInactiveCacheIgnoredPaths(request, version, cacheDirectory);

        return new ContentIntegrityTarget(
            CreateTargetId("cache", version.ContentKey),
            version.DisplayName + " " + request.CacheDisplayNameSuffix,
            cacheDirectory,
            version.EffectiveContentSourceKind,
            ignoredPaths);
    }

    /// <summary>
    /// Builds ignored cache paths that belong to inactive versions of the same modification.
    /// </summary>
    private HashSet<string> BuildInactiveCacheIgnoredPaths(
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

        var inactiveBaseNames = request.AllVersions
            .Where(candidate =>
                !IsExactVersion(candidate, version) &&
                candidate.ContentKey.HasName(version.Name))
            .SelectMany(candidate => new[]
            {
                candidate.Version,
                candidate.Version + "-background",
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string filePath in EnumerateFilesWithoutLinks(cacheDirectory))
        {
            string relativePath = LexicalPath.GetRelativePath(cacheDirectory, filePath);
            if (inactiveBaseNames.Contains(Path.GetFileNameWithoutExtension(filePath)))
            {
                ignoredPaths.Add(relativePath);
            }
        }

        if (ignoredPaths.Count > 0)
        {
            _logger.LogDebug(
                "Ignored {IgnoredPathCount} inactive image-cache file(s) while building integrity target for {ContentName} {ContentVersion}.",
                ignoredPaths.Count,
                version.Name,
                version.Version);
        }

        return ignoredPaths;
    }

    /// <summary>
    /// Creates a stable target identifier.
    /// </summary>
    private static string CreateTargetId(string prefix, LauncherContentKey contentKey)
    {
        return string.Concat(prefix, ":", contentKey.ToStableString());
    }

    private static bool IsExactVersion(
        LauncherContentVersion candidate,
        LauncherContentVersion version)
    {
        return candidate.ContentKey == version.ContentKey;
    }

    /// <summary>
    /// Enumerates files without following linked directories.
    /// </summary>
    private static IEnumerable<string> EnumerateFilesWithoutLinks(string rootDirectory)
    {
        return Directory.EnumerateFiles(
            rootDirectory,
            "*",
            FileSystemPathSafety.CreateRecursiveNoLinksOptions());
    }

}
