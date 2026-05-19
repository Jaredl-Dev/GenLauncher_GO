using System;
using System.Collections.Generic;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Infrastructure.Mods.Models;

/// <summary>
/// Represents a normalized remote modification manifest with its child manifest references.
/// </summary>
internal sealed class RemoteModificationManifest
{
    public RemoteModificationManifest(
        LauncherContentVersion content,
        IReadOnlyList<string> patchManifestUrls,
        IReadOnlyList<string> addonManifestUrls)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        PatchManifestUrls = patchManifestUrls ?? Array.Empty<string>();
        AddonManifestUrls = addonManifestUrls ?? Array.Empty<string>();
    }

    public LauncherContentVersion Content { get; }

    public IReadOnlyList<string> PatchManifestUrls { get; }

    public IReadOnlyList<string> AddonManifestUrls { get; }
}
