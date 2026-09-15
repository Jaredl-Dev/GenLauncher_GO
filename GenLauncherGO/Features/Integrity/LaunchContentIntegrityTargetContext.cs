using System;
using GenLauncherGO.Features.Mods;

namespace GenLauncherGO.Features.Integrity;

internal sealed record LaunchContentIntegrityTargetContext
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
