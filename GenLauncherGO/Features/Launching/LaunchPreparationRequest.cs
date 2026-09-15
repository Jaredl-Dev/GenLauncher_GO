using System;
using System.Collections.Generic;
using System.Linq;
using GenLauncherGO.Features.Mods;
using GenLauncherGO.Features.Startup;

namespace GenLauncherGO.Features.Launching;

/// <summary>
///     Carries selected installed content and the base-script deployment policy for launch preparation.
/// </summary>
internal sealed record LaunchPreparationRequest
{
    public LaunchPreparationRequest(
        LauncherPaths paths,
        IReadOnlyList<LauncherContentVersion> versions,
        bool disableBaseGameScriptFiles)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(versions);

        Paths = paths;
        Versions = versions.ToArray();
        DisableBaseGameScriptFiles = disableBaseGameScriptFiles;
    }

    public LauncherPaths Paths { get; }

    public IReadOnlyList<LauncherContentVersion> Versions { get; }

    public bool DisableBaseGameScriptFiles { get; }
}
