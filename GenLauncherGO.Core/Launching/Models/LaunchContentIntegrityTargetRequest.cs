using System;
using System.Collections.Generic;
using System.Linq;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Core.Launching.Models;

public sealed record LaunchContentIntegrityTargetRequest
{
    public LaunchContentIntegrityTargetRequest(
        LauncherPaths paths,
        IReadOnlyList<LauncherContentVersion> activeVersions,
        IReadOnlyList<LauncherContentVersion> allVersions,
        string cacheDisplayNameSuffix)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(activeVersions);
        ArgumentNullException.ThrowIfNull(allVersions);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDisplayNameSuffix);

        Paths = paths;
        ActiveVersions = activeVersions.ToArray();
        AllVersions = allVersions.ToArray();
        CacheDisplayNameSuffix = cacheDisplayNameSuffix;
    }

    public LauncherPaths Paths { get; }

    public IReadOnlyList<LauncherContentVersion> ActiveVersions { get; }

    public IReadOnlyList<LauncherContentVersion> AllVersions { get; }

    public string CacheDisplayNameSuffix { get; }
}
