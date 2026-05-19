using GenLauncherGO.Core.Integrity.Models;

namespace GenLauncherGO.Core.Mods.Models;

/// <summary>
/// Stores mutable local state for one launcher content version.
/// </summary>
/// <remarks>
/// Remote metadata is immutable on <see cref="LauncherContentVersion"/>. Installation discovery, selection, and
/// integrity trust decisions mutate this separate state and are the only values persisted locally for a version.
/// </remarks>
public sealed class LauncherContentInstallation
{
    public bool Installed { get; set; }

    public bool IsSelected { get; set; }

    public ContentSourceKind ContentSourceKind { get; set; } = ContentSourceKind.UnknownLegacy;
}
