using System;
using System.Collections.Generic;

namespace GenLauncherGO.Infrastructure.Mods.Models;

/// <summary>
///     Represents a normalized remote launcher catalog after third-party backend YAML has been mapped.
/// </summary>
internal sealed class RemoteLauncherCatalog
{
    public RemoteLauncherCatalog(
        IReadOnlyList<RemoteAdvertisingReference> advertisingEntries,
        IReadOnlyList<RemoteCatalogModificationReference> modifications,
        IReadOnlyList<string> originalGameAddonManifestUrls,
        IReadOnlyList<string> originalGamePatchManifestUrls)
    {
        AdvertisingEntries = advertisingEntries ?? Array.Empty<RemoteAdvertisingReference>();
        Modifications = modifications ?? Array.Empty<RemoteCatalogModificationReference>();
        OriginalGameAddonManifestUrls = originalGameAddonManifestUrls ?? Array.Empty<string>();
        OriginalGamePatchManifestUrls = originalGamePatchManifestUrls ?? Array.Empty<string>();
    }

    public static RemoteLauncherCatalog Empty { get; } = new(
        Array.Empty<RemoteAdvertisingReference>(),
        Array.Empty<RemoteCatalogModificationReference>(),
        Array.Empty<string>(),
        Array.Empty<string>());

    public IReadOnlyList<RemoteAdvertisingReference> AdvertisingEntries { get; }

    public IReadOnlyList<RemoteCatalogModificationReference> Modifications { get; }

    public IReadOnlyList<string> OriginalGameAddonManifestUrls { get; }

    public IReadOnlyList<string> OriginalGamePatchManifestUrls { get; }
}
