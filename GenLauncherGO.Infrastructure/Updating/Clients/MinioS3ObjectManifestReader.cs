using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Models;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;

namespace GenLauncherGO.Infrastructure.Updating.Clients;

/// <summary>
/// Reads S3-compatible object listings with MinIO.
/// </summary>
internal sealed class MinioS3ObjectManifestReader : IS3ObjectManifestReader
{
    private readonly Func<S3ObjectManifestRequest, CancellationToken, IAsyncEnumerable<S3ObjectManifestItem>> _listObjects;

    private readonly ILogger<MinioS3ObjectManifestReader> _logger;

    public MinioS3ObjectManifestReader(ILogger<MinioS3ObjectManifestReader> logger)
        : this(logger, ListObjectsAsync)
    {
    }

    internal MinioS3ObjectManifestReader(
        ILogger<MinioS3ObjectManifestReader> logger,
        Func<S3ObjectManifestRequest, CancellationToken, IAsyncEnumerable<S3ObjectManifestItem>> listObjects)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _listObjects = listObjects ?? throw new ArgumentNullException(nameof(listObjects));
    }

    /// <summary>
    /// Reads an authenticated S3-compatible object listing, returning manifest entries with prefix-relative names.
    /// </summary>
    public async Task<IReadOnlyList<RemoteFileManifestEntry>> ReadManifestAsync(
        S3ObjectManifestRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Prefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AccessKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SecretKey);

        List<RemoteFileManifestEntry> files = new();
        await foreach (S3ObjectManifestItem item in _listObjects(request, cancellationToken)
                           .ConfigureAwait(false))
        {
            files.Add(new RemoteFileManifestEntry(
                StripPrefix(item.Key, request.Prefix),
                NormalizeETag(item.ETag),
                item.Size));
        }

        _logger.LogInformation(
            "Read {FileCount} S3 manifest entries from bucket {BucketName}, prefix {Prefix}.",
            files.Count,
            request.BucketName,
            request.Prefix);
        return files;
    }

    [ExcludeFromCodeCoverage(Justification = "Wraps MinIO SDK network enumeration; behavior is covered through the injected listing adapter.")]
    private static async IAsyncEnumerable<S3ObjectManifestItem> ListObjectsAsync(
        S3ObjectManifestRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IMinioClient client = MinioClientFactory.Create(
            request.Endpoint,
            request.AccessKey,
            request.SecretKey,
            request.UseSsl);

        ListObjectsArgs args = new ListObjectsArgs()
            .WithBucket(request.BucketName)
            .WithPrefix(request.Prefix)
            .WithRecursive(true);

        await foreach (Item item in client.ListObjectsEnumAsync(args, cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return new S3ObjectManifestItem(
                item.Key,
                item.ETag,
                item.Size);
        }
    }

    private static string StripPrefix(string key, string prefix)
    {
        string normalizedPrefix = prefix.TrimEnd('/') + "/";
        if (key.StartsWith(normalizedPrefix, StringComparison.Ordinal))
        {
            return key[normalizedPrefix.Length..];
        }

        return key;
    }

    private static string NormalizeETag(string eTag)
    {
        return eTag.Trim().Trim('"');
    }

    internal sealed record S3ObjectManifestItem(
        string Key,
        string ETag,
        ulong Size);
}
