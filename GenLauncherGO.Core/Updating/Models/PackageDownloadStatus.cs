namespace GenLauncherGO.Core.Updating.Models;

/// <summary>
/// Identifies the single terminal status of a package download.
/// </summary>
public enum PackageDownloadStatus
{
    /// <summary>
    /// The package was downloaded, verified, and installed.
    /// </summary>
    Succeeded,

    /// <summary>
    /// The caller cooperatively canceled the operation before installation committed.
    /// </summary>
    Canceled,

    /// <summary>
    /// An expected provider, package, or local-environment condition prevented installation.
    /// </summary>
    RecoverableFailure,

    /// <summary>
    /// An unexpected failure prevented installation and was recorded for diagnostics.
    /// </summary>
    UnexpectedFailure,
}
