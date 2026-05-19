namespace GenLauncherGO.Core.Updating.Models;

/// <summary>
///     Identifies the single terminal status of a package download.
/// </summary>
public enum PackageDownloadStatus
{
    /// <summary>
    ///     The package was downloaded, verified, and installed.
    /// </summary>
    Succeeded,

    /// <summary>
    ///     The caller cooperatively canceled the operation before installation committed.
    /// </summary>
    Canceled,

    /// <summary>
    ///     The launcher stopped the operation to shut down and deliberately kept its partial content for resuming.
    /// </summary>
    /// <remarks>
    ///     Distinct from <see cref="Canceled" /> precisely because cancellation discards partial content: the transport
    ///     stops the same way, but nothing is cleaned up afterwards.
    /// </remarks>
    Suspended,

    /// <summary>
    ///     An expected provider, package, or local-environment condition prevented installation.
    /// </summary>
    RecoverableFailure,

    /// <summary>
    ///     An unexpected failure prevented installation and was recorded for diagnostics.
    /// </summary>
    UnexpectedFailure
}
