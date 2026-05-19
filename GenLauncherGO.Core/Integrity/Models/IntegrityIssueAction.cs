namespace GenLauncherGO.Core.Integrity.Models;

/// <summary>
///     Describes the resolution offered for an integrity issue.
/// </summary>
public enum IntegrityIssueAction
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
    Redownload,

    /// <summary>
    ///     The current manual content will replace its trusted snapshot.
    /// </summary>
    Absorb,

    /// <summary>
    ///     The legacy content will be permanently classified and snapshotted as manual content.
    /// </summary>
    TrustAsManual
}
