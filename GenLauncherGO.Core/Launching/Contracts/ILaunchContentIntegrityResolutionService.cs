using System;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Launching.Models;

namespace GenLauncherGO.Core.Launching.Contracts;

/// <summary>
/// Verifies and resolves launch-readiness integrity state for selected launcher content.
/// </summary>
public interface ILaunchContentIntegrityResolutionService
{
    /// <summary>
    /// Verifies active launch content and returns the target contexts used for any later resolution.
    /// </summary>
    Task<LaunchContentIntegrityVerificationResult> VerifyAsync(
        LaunchContentIntegrityTargetRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Captures initial snapshots for matching managed remote caches and reports whether any target was initialized.
    /// </summary>
    Task<bool> InitializeUntrackedManagedCachesAsync(
        LaunchContentIntegrityResolutionRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Applies confirmed launch-integrity resolutions, including snapshots, cleanup, package repair, and cache refresh.
    /// </summary>
    Task ResolveAsync(
        LaunchContentIntegrityResolutionRequest request,
        IProgress<LaunchContentIntegrityResolutionProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks a manually imported version as manual content and captures its initial package and cache snapshots.
    /// </summary>
    Task RegisterManualImportAsync(
        LaunchContentIntegrityVersionRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Captures initial snapshots for a newly installed managed remote version.
    /// </summary>
    Task CaptureManagedInstallSnapshotAsync(
        LaunchContentIntegrityVersionRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Captures a trusted snapshot for a manually managed cached image target.
    /// </summary>
    Task CaptureManualImageSnapshotAsync(
        LaunchContentIntegrityVersionRequest request,
        CancellationToken cancellationToken);
}
