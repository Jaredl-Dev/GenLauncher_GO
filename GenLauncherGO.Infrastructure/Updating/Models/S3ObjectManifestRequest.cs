namespace GenLauncherGO.Infrastructure.Updating.Models;

/// <summary>
///     Describes an S3-compatible object listing request for a modification version.
/// </summary>
/// <remarks>
///     <c>UseSsl</c> defaults to <see langword="false" /> for compatibility with legacy catalog endpoints that expose
///     plain MinIO ports; an explicit endpoint URI scheme takes precedence.
/// </remarks>
internal sealed record S3ObjectManifestRequest(
    string Endpoint,
    string BucketName,
    string Prefix,
    string AccessKey,
    string SecretKey,
    bool UseSsl = false);
