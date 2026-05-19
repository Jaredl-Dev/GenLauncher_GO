using System;
using System.Collections.Generic;

namespace GenLauncherGO.Infrastructure.Mods.Models;

/// <summary>
/// Represents a normalized remote modification manifest reference.
/// </summary>
internal sealed class RemoteCatalogModificationReference
{
    public RemoteCatalogModificationReference(
        string name,
        string manifestUrl,
        IReadOnlyList<string> patchManifestUrls,
        IReadOnlyList<string> addonManifestUrls)
    {
        Name = name ?? string.Empty;
        ManifestUrl = manifestUrl ?? string.Empty;
        PatchManifestUrls = patchManifestUrls ?? Array.Empty<string>();
        AddonManifestUrls = addonManifestUrls ?? Array.Empty<string>();
    }

    public string Name { get; }

    public string ManifestUrl { get; }

    public IReadOnlyList<string> PatchManifestUrls { get; }

    public IReadOnlyList<string> AddonManifestUrls { get; }
}
