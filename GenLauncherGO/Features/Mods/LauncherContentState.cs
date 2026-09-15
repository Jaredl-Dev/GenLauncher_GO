using System.Collections.Generic;

namespace GenLauncherGO.Features.Mods;

/// <summary>
///     Stores compact launcher content state that is safe to persist locally.
/// </summary>
internal sealed class LauncherContentState
{
    public List<LauncherContentEntryState> Addons { get; set; } = [];

    public List<LauncherContentEntryState> Modifications { get; set; } = [];

    public List<LauncherContentEntryState> Patches { get; set; } = [];
}
