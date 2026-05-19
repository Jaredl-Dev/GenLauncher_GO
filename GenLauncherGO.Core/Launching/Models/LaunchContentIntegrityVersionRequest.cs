using System;
using System.Collections.Generic;
using System.Linq;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Core.Launching.Models;

public sealed record LaunchContentIntegrityVersionRequest
{
    public LaunchContentIntegrityVersionRequest(
        LauncherPaths paths,
        LauncherContentVersion version,
        IReadOnlyList<LauncherContentVersion> allVersions,
        string cacheDisplayNameSuffix)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(allVersions);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDisplayNameSuffix);

        Paths = paths;
        Version = version;
        AllVersions = allVersions.ToArray();
        CacheDisplayNameSuffix = cacheDisplayNameSuffix;
    }

    public LauncherPaths Paths { get; }

    public LauncherContentVersion Version { get; }

    public IReadOnlyList<LauncherContentVersion> AllVersions { get; }

    public string CacheDisplayNameSuffix { get; }
}
