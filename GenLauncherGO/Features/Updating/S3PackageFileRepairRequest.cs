using System.Collections.Generic;
using GenLauncherGO.Shared.IO;

namespace GenLauncherGO.Features.Updating;

/// <summary>
///     Describes selected S3-backed package files that should be repaired in place.
/// </summary>
internal sealed record S3PackageFileRepairRequest(
    IReadOnlyList<RemoteFileManifestEntry> Files,
    S3ObjectManifestRequest Source,
    OwnedContentPath InstalledPath,
    IReadOnlySet<string> HashCheckedExtensions);
