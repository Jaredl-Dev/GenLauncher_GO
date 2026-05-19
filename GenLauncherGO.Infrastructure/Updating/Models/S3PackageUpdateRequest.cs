using System.Collections.Generic;

namespace GenLauncherGO.Infrastructure.Updating.Models;

/// <summary>
/// Describes an S3-backed package update.
/// </summary>
internal sealed record S3PackageUpdateRequest(
    IReadOnlyList<RemoteFileManifestEntry> Files,
    S3ObjectManifestRequest Source,
    PackageUpdatePathSet PathSet,
    IReadOnlySet<string> HashCheckedExtensions);
