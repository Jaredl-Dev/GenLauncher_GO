using System;
using System.Collections.Generic;
using System.Linq;
using GenLauncherGO.Features.Startup;

namespace GenLauncherGO.Features.Integrity;

internal sealed record LaunchContentIntegrityVerificationResult
{
    public LaunchContentIntegrityVerificationResult(
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

    public LauncherPaths Paths { get; }

    public ContentIntegrityReport Report { get; }

    public IReadOnlyList<LaunchContentIntegrityTargetContext> TargetContexts { get; }
}
