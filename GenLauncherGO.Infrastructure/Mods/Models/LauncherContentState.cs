using System.Collections.Generic;

namespace GenLauncherGO.Infrastructure.Mods.Models;

/// <summary>
/// Stores compact launcher content state that is safe to persist locally.
/// </summary>
internal sealed class LauncherContentState
{
    public List<LauncherContentEntryState> Addons { get; set; } = new List<LauncherContentEntryState>();

    public List<LauncherContentEntryState> Modifications { get; set; } = new List<LauncherContentEntryState>();

    public List<LauncherContentEntryState> Patches { get; set; } = new List<LauncherContentEntryState>();
}
