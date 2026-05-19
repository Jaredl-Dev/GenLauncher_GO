namespace GenLauncherGO.Core.Integrity.Models;

/// <summary>
/// Describes a detected content-integrity problem.
/// </summary>
public enum IntegrityIssueKind
{
    /// <summary>
    /// No trusted snapshot exists for the content.
    /// </summary>
    Untracked,

    /// <summary>
    /// A required file is missing.
    /// </summary>
    MissingFile,

    /// <summary>
    /// A file differs from its trusted SHA-256 snapshot.
    /// </summary>
    ModifiedFile,

    /// <summary>
    /// A file is present but is not part of the trusted snapshot.
    /// </summary>
    UnexpectedFile,

    /// <summary>
    /// An unexpected empty directory is present.
    /// </summary>
    EmptyDirectory,

    /// <summary>
    /// A reparse point or symbolic link was found inside verified content.
    /// </summary>
    UnsafeLink,

    /// <summary>
    /// Verification could not complete for an entry.
    /// </summary>
    VerificationError,
}
