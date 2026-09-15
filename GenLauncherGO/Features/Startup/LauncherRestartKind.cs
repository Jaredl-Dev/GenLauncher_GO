namespace GenLauncherGO.Features.Startup;

/// <summary>
///     Identifies which replacement path the shutdown sequence must start.
/// </summary>
internal enum LauncherRestartKind
{
    None,
    Normal,
    ApplicationUpdate
}
