using System.Collections.Generic;

namespace GenLauncherGO.Infrastructure.Mods.Models;

#pragma warning disable IDE1006 // Member names preserve the third-party YAML schema exactly.

/// <summary>
/// Represents the legacy top-level remote launcher catalog document.
/// </summary>
/// <remarks>
/// These member names are owned by the remote backend and are intentionally preserved for exact YAML binding.
/// Infrastructure maps this transport shape to <see cref="RemoteLauncherCatalog"/> before exposing catalog data.
/// </remarks>
internal sealed class LegacyLauncherCatalogDocument
{
    public List<LegacyCatalogAdvertisingReference> AdvData { get; set; } = new();

    public List<string> globalAddonsData { get; set; } = new();

    public List<LegacyCatalogModificationReference> modDatas { get; set; } = new();

    public List<string> originalGameAddons { get; set; } = new();

    public List<string> originalGamePatches { get; set; } = new();

    public string LauncherVersion { get; set; } = string.Empty;
}

#pragma warning restore IDE1006
