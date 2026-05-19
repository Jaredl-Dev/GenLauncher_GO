using System.Collections.Generic;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Infrastructure.Updating.Models;

/// <summary>
/// Describes selected S3-backed package files that should be repaired in place.
/// </summary>
internal sealed record S3PackageFileRepairRequest(
    IReadOnlyList<RemoteFileManifestEntry> Files,
    S3ObjectManifestRequest Source,
    OwnedContentPath InstalledPath,
    IReadOnlySet<string> HashCheckedExtensions);
