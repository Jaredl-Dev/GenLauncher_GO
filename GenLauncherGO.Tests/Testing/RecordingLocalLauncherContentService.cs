using System;
using System.Collections.Generic;
using System.Linq;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Mods.Contracts;

namespace GenLauncherGO.Tests.Testing;

internal sealed class RecordingLocalLauncherContentService : ILocalLauncherContentService
{
    public IReadOnlyList<LauncherContentVersion> InstalledVersions { get; set; } =
        Array.Empty<LauncherContentVersion>();

    public List<(LauncherPaths Paths, LauncherContentKey ContentKey)> DeletedVersions { get; } = [];

    public List<(LauncherPaths Paths, LauncherContentKey ContentKey)> DeletedContents { get; } = [];

    public List<LauncherPaths> EmptyPackageBackupCleanupRequests { get; } = [];

    /// <summary>
    ///     Records image-deletion requests with the catalog names as they stood when the request was made.
    /// </summary>
    /// <remarks>
    ///     <c>Data</c> is a live reference the caller keeps mutating, so only <c>ContentNames</c> can answer what the
    ///     catalog looked like at the moment deletion was decided. <c>Data</c> is retained for identity assertions.
    /// </remarks>
    public List<(
        LauncherPaths Paths,
        LauncherContentKey ContentKey,
        LauncherData Data,
        IReadOnlyList<string> ContentNames)> ImageDeletionRequests
    { get; } = [];

    public IReadOnlyList<LauncherContentVersion> FindInstalledVersions(LauncherPaths paths)
    {
        return InstalledVersions;
    }

    public void DeleteEmptyPackageBackupDirectories(LauncherPaths paths)
    {
        EmptyPackageBackupCleanupRequests.Add(paths);
    }

    public void DeleteVersion(LauncherPaths paths, LauncherContentKey contentKey)
    {
        DeletedVersions.Add((paths, contentKey));
    }

    public void DeleteContent(LauncherPaths paths, LauncherContentKey contentKey)
    {
        DeletedContents.Add((paths, contentKey));
    }

    public void DeleteImagesIfUnused(
        LauncherPaths paths,
        LauncherContentKey contentKey,
        LauncherData launcherData)
    {
        ImageDeletionRequests.Add((paths, contentKey, launcherData, SnapshotContentNames(launcherData)));
    }

    private static IReadOnlyList<string> SnapshotContentNames(LauncherData launcherData)
    {
        return launcherData.Modifications
            .Concat(launcherData.Patches)
            .Concat(launcherData.Addons)
            .Select(content => content.Name)
            .ToList();
    }
}
