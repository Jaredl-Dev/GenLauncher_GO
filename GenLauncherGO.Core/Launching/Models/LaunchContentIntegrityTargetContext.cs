using System;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Core.Launching.Models;

public sealed record LaunchContentIntegrityTargetContext
{
    public LaunchContentIntegrityTargetContext(
        ContentIntegrityTarget target,
        LauncherContentVersion version,
        bool isCache)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(version);

        Target = target;
        Version = version;
        IsCache = isCache;
    }

    public ContentIntegrityTarget Target { get; }

    public LauncherContentVersion Version { get; }

    public bool IsCache { get; }
}
