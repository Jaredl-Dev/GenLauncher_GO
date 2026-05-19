namespace GenLauncherGO.Core.Startup;

/// <summary>
/// Identifies the supported Command &amp; Conquer game variants GenLauncherGO can manage.
/// </summary>
public enum SupportedGame
{
    /// <summary>
    /// No supported game variant has been detected for this launcher session.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Command &amp; Conquer: Generals - Zero Hour.
    /// </summary>
    ZeroHour = 1,

    /// <summary>
    /// Command &amp; Conquer: Generals.
    /// </summary>
    Generals = 2
}
