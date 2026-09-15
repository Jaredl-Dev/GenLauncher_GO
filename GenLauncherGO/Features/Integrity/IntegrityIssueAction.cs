namespace GenLauncherGO.Features.Integrity;

/// <summary>
///     Describes the resolution offered for an integrity issue.
/// </summary>
internal enum IntegrityIssueAction
{
    /// <summary>
    ///     Launch remains blocked and no automatic resolution is available.
    /// </summary>
    Block,

    /// <summary>
    ///     The unexpected managed entry will be deleted.
    /// </summary>
    Delete,

    /// <summary>
    ///     The managed content will be repaired from its remote manifest.
    /// </summary>
    Repair,

    /// <summary>
    ///     The managed package will be downloaded and installed again.
    /// </summary>
    Redownload
}
