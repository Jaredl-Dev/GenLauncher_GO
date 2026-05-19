using System.Collections.Generic;

namespace GenLauncherGO.Infrastructure.Mods.Models;

/// <summary>
/// Represents one advertising entry in the legacy remote catalog document.
/// </summary>
internal sealed class LegacyCatalogAdvertisingReference
{
    public string ModName { get; set; } = string.Empty;

    public string ModLink { get; set; } = string.Empty;

    public List<string> ImagesData { get; set; } = new();
}
