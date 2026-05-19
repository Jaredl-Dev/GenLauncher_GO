using System;
using System.Collections.Generic;
using System.Linq;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Core.Launching.Models;

/// <summary>
///     Carries selected installed content and the base-script deployment policy from UI to Infrastructure.
/// </summary>
public sealed record LaunchPreparationRequest
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
