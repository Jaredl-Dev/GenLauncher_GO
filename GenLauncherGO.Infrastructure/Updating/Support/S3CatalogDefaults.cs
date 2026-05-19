using System;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Infrastructure.Updating.Models;

namespace GenLauncherGO.Infrastructure.Updating.Support;

/// <summary>
/// Provides S3-compatible catalog defaults used by legacy remote modification metadata.
/// </summary>
/// <remarks>
/// These values are retained for compatibility with the original GenLauncher client and backend. The original client
/// already shipped them in client-side code before this GenLauncherGO rewrite/fork, so this project treats them as
/// public legacy credentials rather than private application secrets. The backend/object-storage policy must assume
/// every user can read these values and must keep their permissions limited accordingly.
/// </remarks>
internal static class S3CatalogDefaults
{
    /// <summary>
    /// Gets the default public S3 access key used when catalog metadata does not provide one.
    /// </summary>
    /// <remarks>
    /// This legacy value was already exposed by the original client and is kept only so old catalog entries continue
    /// to resolve.
    /// </remarks>
    public const string PublicAccessKey = "S58TYR9ISEZV8PBP8QG1";

    /// <summary>
    /// Gets the default public S3 secret key used when catalog metadata does not provide one.
    /// </summary>
    /// <remarks>
    /// This legacy value was already exposed by the original client. Do not replace it with a privileged secret unless
    /// downloads are moved behind a trusted backend or another non-client-side credential flow.
    /// </remarks>
    public const string PublicSecretKey = "b2RU1oqVU5toJRnb4gODrXX8sBSgoLcHRX6qPWxj";

    /// <summary>
    /// Creates a manifest request from one remote modification version.
    /// </summary>
    public static S3ObjectManifestRequest CreateManifestRequest(LauncherContentVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new S3ObjectManifestRequest(
            version.S3HostLink,
            version.S3BucketName,
            version.S3FolderName,
            ResolveAccessKey(version),
            ResolveSecretKey(version));
    }

    /// <summary>
    /// Resolves the access key for a modification version, falling back to the public catalog key.
    /// </summary>
    public static string ResolveAccessKey(LauncherContentVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return string.IsNullOrEmpty(version.S3HostPublicKey)
            ? PublicAccessKey
            : version.S3HostPublicKey;
    }

    /// <summary>
    /// Resolves the secret key for a modification version, falling back to the public catalog key.
    /// </summary>
    public static string ResolveSecretKey(LauncherContentVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return string.IsNullOrEmpty(version.S3HostSecretKey)
            ? PublicSecretKey
            : version.S3HostSecretKey;
    }
}
