using System;
using System.Collections.Generic;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Mods.Contracts;

namespace GenLauncherGO.Tests.Testing;

internal sealed class RecordingLocalLauncherContentService : ILocalLauncherContentService
{
    public IReadOnlyList<LauncherContentVersion> InstalledVersions { get; set; } =
        Array.Empty<LauncherContentVersion>();

    public List<(LauncherPaths Paths, LauncherContentKey ContentKey)> DeletedVersions { get; } = new();

    public List<(LauncherPaths Paths, LauncherContentKey ContentKey)> DeletedContents { get; } = new();

    public List<(
        LauncherPaths Paths,
        LauncherContentKey ContentKey,
        LauncherData Data)> ImageDeletionRequests
    { get; } = new();

    public IReadOnlyList<LauncherContentVersion> FindInstalledVersions(LauncherPaths paths)
    {
        return InstalledVersions;
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
        ImageDeletionRequests.Add((paths, contentKey, launcherData));
    }
}
