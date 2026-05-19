using System;
using System.Collections.Generic;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.UI.Features.Launcher.Models;

internal sealed class LauncherLaunchRequest(
    GameLaunchTargetKind targetKind,
    string executableName,
    bool useGeneralsOnline,
    IReadOnlyList<LauncherContentVersion> activeVersions)
{
    public IReadOnlyList<LauncherContentVersion> ActiveVersions { get; } =
        activeVersions ?? throw new ArgumentNullException(nameof(activeVersions));

    public string ExecutableName { get; } = executableName ?? string.Empty;

    public GameLaunchTargetKind TargetKind { get; } = targetKind;

    public bool UseGeneralsOnline { get; } = useGeneralsOnline;
}
