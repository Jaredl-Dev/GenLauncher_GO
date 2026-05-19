using System;
using System.Collections.Generic;
using System.Linq;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Core.Launching.Models;

public sealed record LaunchContentIntegrityResolutionRequest
{
    public LaunchContentIntegrityResolutionRequest(
        LauncherPaths paths,
        ContentIntegrityReport report,
        IReadOnlyList<LaunchContentIntegrityTargetContext> targetContexts)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(targetContexts);

        Paths = paths;
        Report = report;
        TargetContexts = targetContexts.ToArray();
    }

    public LauncherPaths Paths { get; init; }

    public ContentIntegrityReport Report { get; init; }

    public IReadOnlyList<LaunchContentIntegrityTargetContext> TargetContexts { get; init; }
}
