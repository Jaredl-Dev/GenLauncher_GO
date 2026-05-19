using System;
using System.Collections.Generic;

namespace GenLauncherGO.Infrastructure.Mods.Models;

/// <summary>
/// Represents a normalized remote advertising manifest reference.
/// </summary>
internal sealed class RemoteAdvertisingReference
{
    public RemoteAdvertisingReference(
        string name,
        string manifestUrl,
        IReadOnlyList<string> imageUrls)
    {
        Name = name ?? string.Empty;
        ManifestUrl = manifestUrl ?? string.Empty;
        ImageUrls = imageUrls ?? Array.Empty<string>();
    }

    public string Name { get; }

    public string ManifestUrl { get; }

    public IReadOnlyList<string> ImageUrls { get; }
}
