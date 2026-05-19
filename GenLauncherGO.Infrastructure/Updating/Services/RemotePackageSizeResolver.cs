using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Support;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Updating.Services;

/// <summary>
///     Resolves and session-caches package sizes from HTTP or S3-compatible metadata without downloading payloads.
/// </summary>
internal sealed class RemotePackageSizeResolver : IRemotePackageSizeResolver
{
    private readonly Dictionary<CacheKey, long?> _cache = [];

    private readonly Lock _cacheSync = new();
    private readonly IDownloadFileMetadataReader _downloadFileMetadataReader;

    private readonly ILogger<RemotePackageSizeResolver> _logger;

    private readonly IS3ObjectManifestReader _s3ObjectManifestReader;

    public RemotePackageSizeResolver(
        IDownloadFileMetadataReader downloadFileMetadataReader,
        IS3ObjectManifestReader s3ObjectManifestReader,
        ILogger<RemotePackageSizeResolver> logger)
    {
        _downloadFileMetadataReader = downloadFileMetadataReader ??
                                      throw new ArgumentNullException(nameof(downloadFileMetadataReader));
        _s3ObjectManifestReader = s3ObjectManifestReader ??
                                  throw new ArgumentNullException(nameof(s3ObjectManifestReader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<long?> GetTotalBytesAsync(
        LauncherContentVersion version,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(version);

        var cacheKey = CacheKey.Create(version);
        lock (_cacheSync)
        {
            if (_cache.TryGetValue(cacheKey, out long? cachedSize))
            {
                return cachedSize;
            }
        }

        long? totalBytes;
        try
        {
            totalBytes = await ResolveTotalBytesAsync(version, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Failed to resolve package size for {ContentIdentity} from {SourceKind}; failure type: {FailureType}.",
                version.ContentKey.ToStableString(),
                version.EffectiveContentSourceKind,
                exception.GetType().Name);
            totalBytes = null;
        }

        lock (_cacheSync)
        {
            _cache[cacheKey] = totalBytes;
        }

        return totalBytes;
    }

    private async Task<long?> ResolveTotalBytesAsync(
        LauncherContentVersion version,
        CancellationToken cancellationToken)
    {
        switch (version.EffectiveContentSourceKind)
        {
            case ContentSourceKind.ManagedS3:
                IReadOnlyList<RemoteFileManifestEntry> manifest =
                    await _s3ObjectManifestReader.ReadManifestAsync(
                        S3CatalogDefaults.CreateManifestRequest(version),
                        cancellationToken).ConfigureAwait(false);
                ulong totalSize = 0;
                foreach (RemoteFileManifestEntry entry in manifest)
                {
                    totalSize = checked(totalSize + entry.Size);
                }

                return checked((long)totalSize);

            case ContentSourceKind.ManagedSingleFile:
                Uri downloadUri = DownloadLinkResolver.ResolveDownloadUri(version.SimpleDownloadLink);
                DownloadFileMetadata metadata = await _downloadFileMetadataReader.ReadMetadataAsync(
                    downloadUri,
                    cancellationToken).ConfigureAwait(false);
                return metadata.TotalBytes is >= 0 ? metadata.TotalBytes : null;

            default:
                _logger.LogDebug(
                    "Package size is unavailable for {ContentIdentity} with unsupported source {SourceKind}.",
                    version.ContentKey.ToStableString(),
                    version.EffectiveContentSourceKind);
                return null;
        }
    }

    private readonly record struct CacheKey(
        LauncherContentKey ContentKey,
        ContentSourceKind SourceKind,
        string S3Host,
        string S3Bucket,
        string S3Folder,
        string DirectDownloadLink)
    {
        public static CacheKey Create(LauncherContentVersion version)
        {
            return new CacheKey(
                version.ContentKey,
                version.EffectiveContentSourceKind,
                version.S3HostLink,
                version.S3BucketName,
                version.S3FolderName,
                version.SimpleDownloadLink);
        }
    }
}
