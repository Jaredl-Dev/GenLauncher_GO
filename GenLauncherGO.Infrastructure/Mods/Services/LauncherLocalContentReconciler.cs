using System;
using System.Collections.Generic;
using System.Linq;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Mods.Contracts;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Mods.Services;

/// <summary>
/// Reconciles launcher catalog state with local content folders.
/// </summary>
internal sealed class LauncherLocalContentReconciler
{
    private readonly ILocalLauncherContentService _localContentService;

    private readonly ILogger<LauncherLocalContentReconciler> _logger;

    public LauncherLocalContentReconciler(
        ILocalLauncherContentService localContentService,
        ILogger<LauncherLocalContentReconciler> logger)
    {
        _localContentService = localContentService ?? throw new ArgumentNullException(nameof(localContentService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Reconcile(
        LauncherData launcherData,
        IReadOnlyCollection<LauncherContentKey> downloadedReposContent,
        LauncherPaths paths)
    {
        ArgumentNullException.ThrowIfNull(launcherData);
        ArgumentNullException.ThrowIfNull(downloadedReposContent);
        ArgumentNullException.ThrowIfNull(paths);

        IReadOnlyList<LauncherContentVersion> installedVersions =
            _localContentService.FindInstalledVersions(paths);

        AddUnregisteredModifications(launcherData, installedVersions);
        (int MarkedNotInstalledCount, int RemovedCount) changes = DeleteOutdatedModifications(
            launcherData,
            downloadedReposContent,
            installedVersions,
            paths);
        _logger.LogInformation(
            "Reconciled launcher catalog with local content folders. Local versions: {LocalVersionCount}; " +
            "marked not installed: {MarkedNotInstalledCount}; " +
            "removed stale catalog entries: {RemovedCatalogEntryCount}.",
            installedVersions.Count,
            changes.MarkedNotInstalledCount,
            changes.RemovedCount);
    }

    public void DeleteVersion(
        LauncherContentKey contentKey,
        LauncherPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        _localContentService.DeleteVersion(paths, contentKey);
    }

    public void DeleteContent(
        LauncherContentKey contentKey,
        LauncherPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        _localContentService.DeleteContent(paths, contentKey);
    }

    private void AddUnregisteredModifications(
        LauncherData launcherData,
        IEnumerable<LauncherContentVersion> installedVersions)
    {
        foreach (LauncherContentVersion version in installedVersions)
        {
            launcherData.AddOrUpdate(version);
        }
    }

    /// <summary>
    /// Removes local-only catalog entries whose folders no longer contain files.
    /// </summary>
    private (int MarkedNotInstalledCount, int RemovedCount) DeleteOutdatedModifications(
        LauncherData launcherData,
        IReadOnlyCollection<LauncherContentKey> downloadedReposContent,
        IReadOnlyCollection<LauncherContentVersion> installedVersions,
        LauncherPaths paths)
    {
        var installedVersionIds = installedVersions
            .Select(version => version.ContentKey)
            .ToHashSet();
        int markedNotInstalledCount = 0;
        int removedCount = 0;

        IReadOnlyList<LauncherContentVersion> contentVersions = launcherData.Modifications
            .Concat(launcherData.Addons)
            .Concat(launcherData.Patches)
            .SelectMany(content => content.Versions)
            .DistinctBy(version => version.ContentKey)
            .ToList();

        foreach (LauncherContentVersion version in contentVersions)
        {
            CountReconciliationResult(CheckContentExistence(
                launcherData,
                downloadedReposContent,
                version,
                paths,
                installedVersionIds), ref markedNotInstalledCount, ref removedCount);
        }

        return (markedNotInstalledCount, removedCount);
    }

    /// <summary>
    /// Removes or marks a content version when the local folder no longer contains files.
    /// </summary>
    private (bool MarkedNotInstalled, bool Removed) CheckContentExistence(
        LauncherData launcherData,
        IReadOnlyCollection<LauncherContentKey> downloadedReposContent,
        LauncherContentVersion modificationVersion,
        LauncherPaths paths,
        HashSet<LauncherContentKey> installedVersionIds)
    {
        if (installedVersionIds.Contains(modificationVersion.ContentKey))
        {
            return (false, false);
        }

        if (downloadedReposContent.Contains(modificationVersion.ContentKey) ||
            modificationVersion.EffectiveContentSourceKind is
                ContentSourceKind.ManagedS3 or ContentSourceKind.ManagedSingleFile)
        {
            if (modificationVersion.Installation.Installed)
            {
                modificationVersion.Installation.Installed = false;
                return (true, false);
            }
        }
        else
        {
            launcherData.DeleteVersion(modificationVersion.ContentKey);
            DeleteModificationImagesIfCardMissing(launcherData, modificationVersion.ContentKey, paths);
            return (false, true);
        }

        return (false, false);
    }

    private static void CountReconciliationResult(
        (bool MarkedNotInstalled, bool Removed) result,
        ref int markedNotInstalledCount,
        ref int removedCount)
    {
        if (result.MarkedNotInstalled)
        {
            markedNotInstalledCount++;
        }

        if (result.Removed)
        {
            removedCount++;
        }
    }

    private void DeleteModificationImagesIfCardMissing(
        LauncherData launcherData,
        LauncherContentKey contentKey,
        LauncherPaths paths)
    {
        _localContentService.DeleteImagesIfUnused(paths, contentKey, launcherData);
    }
}
