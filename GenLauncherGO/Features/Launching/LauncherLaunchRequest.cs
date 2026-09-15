using System;
using System.Collections.Generic;
using GenLauncherGO.Features.Mods;

namespace GenLauncherGO.Features.Launching;

internal sealed class LauncherLaunchRequest(
    GameLaunchTargetKind targetKind,
    string executablePath,
    bool useGeneralsOnline,
    IReadOnlyList<LauncherContentVersion> activeVersions)
{
    public IReadOnlyList<LauncherContentVersion> ActiveVersions { get; } =
        activeVersions ?? throw new ArgumentNullException(nameof(activeVersions));

    public string ExecutablePath { get; } = executablePath ?? string.Empty;

    public GameLaunchTargetKind TargetKind { get; } = targetKind;

    public bool UseGeneralsOnline { get; } = useGeneralsOnline;
}
