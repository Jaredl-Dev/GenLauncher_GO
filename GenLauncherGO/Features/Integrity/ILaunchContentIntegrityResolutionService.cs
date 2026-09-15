using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenLauncherGO.Features.Integrity;

/// <summary>
///     Verifies and resolves launch-readiness integrity state for selected launcher content.
/// </summary>
internal interface ILaunchContentIntegrityResolutionService
{
    /// <summary>
    ///     Verifies active launch content and returns the target contexts used for any later resolution.
    /// </summary>
    Task<LaunchContentIntegrityVerificationResult> VerifyAsync(
        LaunchContentIntegrityTargetRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Captures initial snapshots for untracked managed remote content whose files already match the remote source,
    ///     and reports whether any target was initialized.
    /// </summary>
    /// <remarks>
    ///     Content that was installed by an earlier launcher, or moved in from another one, is byte-identical to what a
    ///     download would produce but carries no snapshot. Adopting it here is what keeps that content out of the launch
    ///     review dialog without redownloading a package the user already has.
    /// </remarks>
    Task<bool> InitializeUntrackedManagedContentAsync(
        LaunchContentIntegrityVerificationResult verification,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Applies confirmed launch-integrity resolutions, including snapshots, cleanup, package repair, and cache refresh.
    /// </summary>
    Task ResolveAsync(
        LaunchContentIntegrityVerificationResult verification,
        IProgress<LaunchContentIntegrityResolutionProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Marks a manually imported version as manual content, and captures its initial package and cache snapshots
    ///     only when the import still resolves to a managed remote package.
    /// </summary>
    Task RegisterManualImportAsync(
        LaunchContentIntegrityTargetRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Captures initial snapshots for a newly installed managed remote version.
    /// </summary>
    Task CaptureManagedInstallSnapshotAsync(
        LaunchContentIntegrityTargetRequest request,
        CancellationToken cancellationToken);
}
