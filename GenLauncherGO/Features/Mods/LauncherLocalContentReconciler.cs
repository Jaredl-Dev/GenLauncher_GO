using System;
using System.Collections.Generic;
using System.Linq;
using GenLauncherGO.Features.Integrity;
using GenLauncherGO.Features.Startup;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Features.Mods;

/// <summary>
///     Reconciles launcher catalog state with local content folders.
/// </summary>
internal sealed class LauncherLocalContentReconciler
{
    private readonly IContentIntegrityService _integrityService;

    private readonly ILocalLauncherContentService _localContentService;

    private readonly ILogger<LauncherLocalContentReconciler> _logger;

    public LauncherLocalContentReconciler(
        ILocalLauncherContentService localContentService,
        IContentIntegrityService integrityService,
        ILogger<LauncherLocalContentReconciler> logger)
    {
        _localContentService = localContentService ?? throw new ArgumentNullException(nameof(localContentService));
        _integrityService = integrityService ?? throw new ArgumentNullException(nameof(integrityService));
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

        _localContentService.DeleteEmptyPackageBackupDirectories(paths);
        IReadOnlyList<LauncherContentVersion> installedVersions =
            _localContentService.FindInstalledVersions(paths);

        AddUnregisteredModifications(launcherData, installedVersions);
        (int MarkedNotInstalledCount, int RemovedCount) changes = DeleteOutdatedModifications(
            launcherData,
            downloadedReposContent,
            installedVersions,
            paths);
        int deletedSnapshotCount = DeleteOrphanedIntegritySnapshots(launcherData, paths);
        _logger.LogInformation(
            "Reconciled launcher catalog with local content folders. Local versions: {LocalVersionCount}; " +
            "marked not installed: {MarkedNotInstalledCount}; " +
            "removed stale catalog entries: {RemovedCatalogEntryCount}; " +
            "deleted orphaned integrity snapshots: {DeletedSnapshotCount}.",
            installedVersions.Count,
            changes.MarkedNotInstalledCount,
            changes.RemovedCount,
            deletedSnapshotCount);
    }

    /// <summary>
    ///     Deletes integrity snapshots whose content is no longer installed.
    /// </summary>
    /// <remarks>
    ///     Deleting content removes its files but never its snapshot, so the snapshot directory is swept here against
    ///     the reconciled catalog instead. Uninstalled content is safe to drop because integrity targets are only ever
    ///     built for installed versions, and reinstalling captures a fresh snapshot.
    /// </remarks>
    private int DeleteOrphanedIntegritySnapshots(LauncherData launcherData, LauncherPaths paths)
    {
        var retainedTargetIds = launcherData.AllContent
            .SelectMany(content => content.Versions)
            .Where(version => version.Installation.Installed)
            .SelectMany(version => ContentIntegrityTargetId.ForContent(version.ContentKey))
            .ToHashSet(StringComparer.Ordinal);

        return _integrityService.PruneSnapshots(paths, retainedTargetIds);
    }

    public void DeleteVersion(
        LauncherContentKey contentKey,
        LauncherPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        _localContentService.DeleteVersion(paths, contentKey);
    }

    public void DiscardVersion(
        LauncherData launcherData,
        LauncherContentKey contentKey,
        LauncherPaths paths)
    {
        ArgumentNullException.ThrowIfNull(launcherData);
        ArgumentNullException.ThrowIfNull(paths);

        _localContentService.DeleteVersion(paths, contentKey);
        launcherData.DeleteVersion(contentKey);
        _localContentService.DeleteImagesIfUnused(paths, contentKey, launcherData);
    }

    public void DiscardContent(
        LauncherData launcherData,
        LauncherContentKey contentKey,
        LauncherPaths paths)
    {
        ArgumentNullException.ThrowIfNull(launcherData);
        ArgumentNullException.ThrowIfNull(paths);

        _localContentService.DeleteContent(paths, contentKey);
        launcherData.DeleteContent(contentKey);
        _localContentService.DeleteImagesIfUnused(paths, contentKey, launcherData);
    }

    private static void AddUnregisteredModifications(
        LauncherData launcherData,
        IEnumerable<LauncherContentVersion> installedVersions)
    {
        foreach (LauncherContentVersion version in installedVersions)
        {
            launcherData.AddOrUpdate(version);
        }
    }

    /// <summary>
    ///     Removes local-only catalog entries whose folders no longer contain files.
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

        IReadOnlyList<LauncherContentVersion> contentVersions = launcherData.AllContent
            .SelectMany(content => content.Versions)
            .DistinctBy(version => version.ContentKey)
            .ToList();

        foreach (LauncherContentVersion version in contentVersions)
        {
            (bool markedNotInstalled, bool removed) = CheckContentExistence(
                launcherData,
                downloadedReposContent,
                version,
                paths,
                installedVersionIds);
            if (markedNotInstalled)
            {
                markedNotInstalledCount++;
            }

            if (removed)
            {
                removedCount++;
            }
        }

        return (markedNotInstalledCount, removedCount);
    }

    /// <summary>
    ///     Removes or marks a content version when the local folder no longer contains files.
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
            modificationVersion.EffectiveContentSourceKind.IsManagedRemote())
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
            _localContentService.DeleteImagesIfUnused(paths, modificationVersion.ContentKey, launcherData);
            return (false, true);
        }

        return (false, false);
    }

}
