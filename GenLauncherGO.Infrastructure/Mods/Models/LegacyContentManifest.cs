using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Infrastructure.Mods.Models;

/// <summary>
/// Represents one content manifest using the exact property names accepted from the legacy remote backend.
/// </summary>
internal sealed class LegacyContentManifest
{
    public ModificationType ModificationType { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string SimpleDownloadLink { get; set; } = string.Empty;

    public string UIImageSourceLink { get; set; } = string.Empty;

    public string DiscordLink { get; set; } = string.Empty;

    public string ModDBLink { get; set; } = string.Empty;

    public string NewsLink { get; set; } = string.Empty;

    public string DependenceName { get; set; } = string.Empty;

    public string S3HostLink { get; set; } = string.Empty;

    public string S3BucketName { get; set; } = string.Empty;

    public string S3FolderName { get; set; } = string.Empty;

    public string S3HostPublicKey { get; set; } = string.Empty;

    public string S3HostSecretKey { get; set; } = string.Empty;

    public string NetworkInfo { get; set; } = string.Empty;

    public bool Deprecated { get; set; }

    public string SupportLink { get; set; } = string.Empty;

    public ContentSourceKind ContentSourceKind { get; set; } = ContentSourceKind.UnknownLegacy;
}
