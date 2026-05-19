using System.Collections.Generic;

namespace GenLauncherGO.Infrastructure.Mods.Models;

// ReSharper disable InconsistentNaming

/// <summary>
///     Represents the legacy top-level remote launcher catalog document.
/// </summary>
/// <remarks>
///     These member names are owned by the remote backend and are intentionally preserved for exact YAML binding.
///     Infrastructure maps this transport shape to <see cref="RemoteLauncherCatalog" /> before exposing catalog data.
/// </remarks>
internal sealed class LegacyLauncherCatalogDocument
{
    public List<LegacyCatalogAdvertisingReference> AdvData { get; set; } = [];

    public List<string> globalAddonsData { get; set; } = [];

    public List<LegacyCatalogModificationReference> modDatas { get; set; } = [];

    public List<string> originalGameAddons { get; set; } = [];

    public List<string> originalGamePatches { get; set; } = [];

    public string LauncherVersion { get; set; } = string.Empty;
}

// ReSharper restore InconsistentNaming
