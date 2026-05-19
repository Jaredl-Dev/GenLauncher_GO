using System;
using System.Collections.Generic;
using System.Linq;
using GenLauncherGO.Core.Integrity.Models;

namespace GenLauncherGO.Core.Launching.Models;

public sealed record LaunchContentIntegrityVerificationResult
{
    public LaunchContentIntegrityVerificationResult(
        ContentIntegrityReport report,
        IReadOnlyList<LaunchContentIntegrityTargetContext> targetContexts)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(targetContexts);

        Report = report;
        TargetContexts = targetContexts.ToArray();
    }

    public ContentIntegrityReport Report { get; }

    public IReadOnlyList<LaunchContentIntegrityTargetContext> TargetContexts { get; }
}
