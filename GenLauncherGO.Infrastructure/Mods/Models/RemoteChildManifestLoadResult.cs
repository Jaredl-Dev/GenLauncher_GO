using System;
using System.Collections.Generic;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Infrastructure.Mods.Models;

/// <summary>
/// Describes a child-manifest load that may contain partial remote results.
/// </summary>
internal sealed class RemoteChildManifestLoadResult
{
    public RemoteChildManifestLoadResult(
        IReadOnlyList<LauncherContentVersion> contentVersions,
        int failedCount)
    {
        ContentVersions = contentVersions ?? Array.Empty<LauncherContentVersion>();
        FailedCount = failedCount;
    }

    public IReadOnlyList<LauncherContentVersion> ContentVersions { get; }

    public int FailedCount { get; }

    public bool Succeeded => FailedCount == 0;
}
