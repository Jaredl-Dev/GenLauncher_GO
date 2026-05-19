namespace GenLauncherGO.Core.Integrity.Models;

/// <summary>
///     Describes the authoritative source for installed launcher content.
/// </summary>
public enum ContentSourceKind
{
    /// <summary>
    ///     The source of the installed content has not yet been classified.
    /// </summary>
    UnknownLegacy,

    /// <summary>
    ///     The content is managed from an S3-compatible remote manifest.
    /// </summary>
    ManagedS3,

    /// <summary>
    ///     The content is managed from a remotely downloaded package file.
    /// </summary>
    ManagedSingleFile,

    /// <summary>
    ///     The content was manually imported or explicitly trusted by the user.
    /// </summary>
    Manual
}

/// <summary>
///     Defines shared classifications for launcher content sources.
/// </summary>
public static class ContentSourceKindExtensions
{
    /// <summary>
    ///     Determines whether the launcher can restore the content from a managed remote source.
    /// </summary>
    public static bool IsManagedRemote(this ContentSourceKind sourceKind)
    {
        return sourceKind is ContentSourceKind.ManagedS3 or ContentSourceKind.ManagedSingleFile;
    }
}
