using System.Collections.Generic;

namespace GenLauncherGO.Infrastructure.Mods.Models;

/// <summary>
///     Represents one modification and its child-manifest links in the legacy remote catalog document.
/// </summary>
internal sealed class LegacyCatalogModificationReference
{
    public string ModName { get; set; } = string.Empty;

    public string ModLink { get; set; } = string.Empty;

    public List<string> ModPatches { get; set; } = [];

    public List<string> ModAddons { get; set; } = [];
}
