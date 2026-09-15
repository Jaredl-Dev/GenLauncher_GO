using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Features.Integrity;
using GenLauncherGO.Features.Mods;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Features.Updating;

/// <summary>
///     Resolves and caches the remote metadata shared by package sizing, installation, and repair.
/// </summary>
internal sealed class ManagedPackageSourceResolver : IRemotePackageSizeResolver
{
    private readonly Dictionary<CacheKey, PackageSource> _sourceCache = [];
    private readonly Lock _cacheSync = new();
    private readonly IDownloadFileMetadataReader _downloadFileMetadataReader;
    private readonly ILogger<ManagedPackageSourceResolver> _logger;
    private readonly IS3ObjectManifestReader _s3ObjectManifestReader;
    private readonly HashSet<CacheKey> _unavailableSizes = [];

    public ManagedPackageSourceResolver(
        IDownloadFileMetadataReader downloadFileMetadataReader,
        IS3ObjectManifestReader s3ObjectManifestReader,
        ILogger<ManagedPackageSourceResolver> logger)
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
            if (_sourceCache.TryGetValue(cacheKey, out PackageSource? cachedSource))
            {
                return cachedSource.TotalBytes;
            }

            if (_unavailableSizes.Contains(cacheKey))
            {
                return null;
            }
        }

        long? totalBytes;
        try
        {
            PackageSource? source = await ResolveAsync(version, cancellationToken).ConfigureAwait(false);
            totalBytes = source?.TotalBytes;
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
            if (!_sourceCache.ContainsKey(cacheKey))
            {
                _unavailableSizes.Add(cacheKey);
            }
        }

        return totalBytes;
    }

    /// <summary>
    ///     Resolves one managed package source while retaining successful provider metadata for later consumers.
    /// </summary>
    public async Task<PackageSource?> ResolveAsync(
        LauncherContentVersion version,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(version);
        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = CacheKey.Create(version);
        lock (_cacheSync)
        {
            if (_sourceCache.TryGetValue(cacheKey, out PackageSource? cachedSource))
            {
                return cachedSource;
            }
        }

        PackageSource? source = await ResolveCoreAsync(version, cancellationToken).ConfigureAwait(false);
        if (source is not null)
        {
            lock (_cacheSync)
            {
                _sourceCache[cacheKey] = source;
                _unavailableSizes.Remove(cacheKey);
            }
        }

        return source;
    }

    private async Task<PackageSource?> ResolveCoreAsync(
        LauncherContentVersion version,
        CancellationToken cancellationToken)
    {
        switch (version.EffectiveContentSourceKind)
        {
            case ContentSourceKind.ManagedS3:
                S3ObjectManifestRequest request = S3CatalogDefaults.CreateManifestRequest(version);
                IReadOnlyList<RemoteFileManifestEntry> files =
                    await _s3ObjectManifestReader.ReadManifestAsync(request, cancellationToken).ConfigureAwait(false);
                return new PackageSource.S3(request, files);

            case ContentSourceKind.ManagedSingleFile:
                Uri downloadUri = DownloadLinkResolver.ResolveDownloadUri(version.SimpleDownloadLink);
                DownloadFileMetadata metadata = await _downloadFileMetadataReader.ReadMetadataAsync(
                    downloadUri,
                    cancellationToken).ConfigureAwait(false);
                return new PackageSource.SingleFile(metadata);

            default:
                _logger.LogDebug(
                    "Package metadata is unavailable for {ContentIdentity} with unsupported source {SourceKind}.",
                    version.ContentKey.ToStableString(),
                    version.EffectiveContentSourceKind);
                return null;
        }
    }

    private static long? SumManifestBytes(IEnumerable<RemoteFileManifestEntry> files)
    {
        try
        {
            ulong totalSize = 0;
            foreach (RemoteFileManifestEntry entry in files)
            {
                totalSize = checked(totalSize + entry.Size);
            }

            return checked((long)totalSize);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    internal abstract record PackageSource(long? TotalBytes)
    {
        internal sealed record S3(
            S3ObjectManifestRequest Request,
            IReadOnlyList<RemoteFileManifestEntry> Files) : PackageSource(SumManifestBytes(Files));

        internal sealed record SingleFile(DownloadFileMetadata Metadata) :
            PackageSource(Metadata.TotalBytes is >= 0 ? Metadata.TotalBytes : null);
    }

    private readonly record struct CacheKey(
        LauncherContentKey ContentKey,
        ContentSourceKind SourceKind,
        string S3Host,
        string S3Bucket,
        string S3Folder,
        string S3PublicKey,
        string S3SecretKey,
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
                version.S3HostPublicKey,
                version.S3HostSecretKey,
                version.SimpleDownloadLink);
        }
    }
}
