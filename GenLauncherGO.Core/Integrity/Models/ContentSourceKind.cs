namespace GenLauncherGO.Core.Integrity.Models;

/// <summary>
/// Describes the authoritative source for installed launcher content.
/// </summary>
public enum ContentSourceKind
{
    /// <summary>
    /// The source of the installed content has not yet been classified.
    /// </summary>
    UnknownLegacy,

    /// <summary>
    /// The content is managed from an S3-compatible remote manifest.
    /// </summary>
    ManagedS3,

    /// <summary>
    /// The content is managed from a remotely downloaded package file.
    /// </summary>
    ManagedSingleFile,

    /// <summary>
    /// The content was manually imported or explicitly trusted by the user.
    /// </summary>
    Manual,
}
