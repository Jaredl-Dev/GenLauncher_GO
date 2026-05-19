using System;
using System.Collections.Generic;
using System.Linq;

namespace GenLauncherGO.Infrastructure.Launching.Support;

/// <summary>
/// Carries infrastructure diagnostics to the launch-preparation boundary for logging.
/// </summary>
internal sealed record DeploymentResult
{
    private DeploymentResult(IReadOnlyList<DeploymentFailure> failures)
    {
        Failures = failures.ToArray();
    }

    public bool Succeeded => Failures.Count == 0;

    public IReadOnlyList<DeploymentFailure> Failures { get; }

    public static DeploymentResult Success()
    {
        return new DeploymentResult(Array.Empty<DeploymentFailure>());
    }

    public static DeploymentResult Failure(IReadOnlyList<DeploymentFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        if (failures.Count == 0)
        {
            throw new ArgumentException("At least one deployment failure is required.", nameof(failures));
        }

        return new DeploymentResult(failures);
    }
}

internal sealed record DeploymentFailure(DeploymentFailureKind Kind, string Path, string Message);

internal enum DeploymentFailureKind
{
    FileSystem,
    Manifest
}

/// <summary>
/// Records whether cleanup must account for a deployed hard link or copy.
/// </summary>
internal enum DeploymentMethod
{
    HardLink,
    Copy
}
