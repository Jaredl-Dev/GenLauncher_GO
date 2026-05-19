using System;
using Minio;

namespace GenLauncherGO.Infrastructure.Updating.Clients;

internal static class MinioClientFactory
{
    /// <summary>
    /// Creates an authenticated MinIO client; explicit endpoint URI schemes override the host-only SSL preference.
    /// </summary>
    public static IMinioClient Create(
        string endpoint,
        string accessKey,
        string secretKey,
        bool useSsl = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretKey);

        string normalizedEndpoint = endpoint.Trim();
        bool resolvedUseSsl = useSsl;

        if (normalizedEndpoint.Contains("://", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(normalizedEndpoint, UriKind.Absolute, out Uri? endpointUri))
        {
            normalizedEndpoint = endpointUri.Authority;
            resolvedUseSsl = string.Equals(endpointUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }
        else if (normalizedEndpoint.EndsWith(":443", StringComparison.OrdinalIgnoreCase))
        {
            resolvedUseSsl = true;
        }

        IMinioClient client = new MinioClient()
            .WithEndpoint(normalizedEndpoint)
            .WithCredentials(accessKey, secretKey);

        if (resolvedUseSsl)
        {
            return client.WithSSL().Build();
        }

        return client.Build();
    }
}
