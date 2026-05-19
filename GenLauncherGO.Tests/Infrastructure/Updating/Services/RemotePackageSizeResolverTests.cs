using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Services;

public sealed class RemotePackageSizeResolverTests
{
    [Fact]
    public async Task GetTotalBytesAsync_ReturnsDirectFileContentLengthAsync()
    {
        Uri downloadUri = new("https://example.test/contra.zip");
        RecordingMetadataReader metadataReader = new((uri, _) =>
            Task.FromResult(new DownloadFileMetadata(uri, "contra.zip", 3_355_443_200)));
        RecordingManifestReader manifestReader = new();
        RemotePackageSizeResolver resolver = CreateResolver(metadataReader, manifestReader);
        LauncherContentVersion version = new()
        {
            ModificationType = ModificationType.Mod,
            Name = "Contra",
            Version = "009",
            SimpleDownloadLink = downloadUri.ToString(),
        };

        long? totalBytes = await resolver.GetTotalBytesAsync(version, CancellationToken.None);

        totalBytes.Should().Be(3_355_443_200);
        metadataReader.RequestCount.Should().Be(1);
        manifestReader.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task GetTotalBytesAsync_SumsS3ManifestIncludingEmptyManifestAsync()
    {
        RecordingMetadataReader metadataReader = new();
        RecordingManifestReader manifestReader = new(
            new[]
            {
                new RemoteFileManifestEntry("Data/one.big", "hash", 10),
                new RemoteFileManifestEntry("Data/two.big", "hash", 15),
            },
            Array.Empty<RemoteFileManifestEntry>());
        RemotePackageSizeResolver resolver = CreateResolver(metadataReader, manifestReader);

        long? populatedSize = await resolver.GetTotalBytesAsync(CreateS3Version("1.0"), CancellationToken.None);
        long? emptySize = await resolver.GetTotalBytesAsync(CreateS3Version("2.0"), CancellationToken.None);

        populatedSize.Should().Be(25);
        emptySize.Should().Be(0);
        metadataReader.RequestCount.Should().Be(0);
        manifestReader.RequestCount.Should().Be(2);
    }

    [Fact]
    public async Task GetTotalBytesAsync_ReturnsUnavailableForUnsupportedSourceAsync()
    {
        RecordingMetadataReader metadataReader = new();
        RecordingManifestReader manifestReader = new();
        RemotePackageSizeResolver resolver = CreateResolver(metadataReader, manifestReader);
        LauncherContentVersion version = new()
        {
            ModificationType = ModificationType.Mod,
            Name = "Manual Mod",
            Version = "1.0",
        };

        long? totalBytes = await resolver.GetTotalBytesAsync(version, CancellationToken.None);

        totalBytes.Should().BeNull();
        metadataReader.RequestCount.Should().Be(0);
        manifestReader.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task GetTotalBytesAsync_MapsMetadataFailureToUnavailableAsync()
    {
        RecordingMetadataReader metadataReader = new((_, _) =>
            Task.FromException<DownloadFileMetadata>(new HttpRequestException("Offline")));
        RemotePackageSizeResolver resolver = CreateResolver(metadataReader, new RecordingManifestReader());
        LauncherContentVersion version = new()
        {
            ModificationType = ModificationType.Mod,
            Name = "Contra",
            Version = "009",
            SimpleDownloadLink = "https://example.test/contra.zip",
        };

        long? totalBytes = await resolver.GetTotalBytesAsync(version, CancellationToken.None);

        totalBytes.Should().BeNull();
    }

    [Fact]
    public async Task GetTotalBytesAsync_CachesByContentVersionAndSourceMetadataAsync()
    {
        RecordingMetadataReader metadataReader = new((uri, _) =>
            Task.FromResult(new DownloadFileMetadata(uri, "package.zip", 42)));
        RemotePackageSizeResolver resolver = CreateResolver(metadataReader, new RecordingManifestReader());
        LauncherContentVersion firstVersion = new()
        {
            ModificationType = ModificationType.Mod,
            Name = "Contra",
            Version = "1.0",
            SimpleDownloadLink = "https://example.test/contra.zip",
        };
        LauncherContentVersion changedVersion = new()
        {
            ModificationType = ModificationType.Mod,
            Name = "Contra",
            Version = "2.0",
            SimpleDownloadLink = "https://example.test/contra.zip",
        };

        await resolver.GetTotalBytesAsync(firstVersion, CancellationToken.None);
        await resolver.GetTotalBytesAsync(firstVersion, CancellationToken.None);
        await resolver.GetTotalBytesAsync(changedVersion, CancellationToken.None);

        metadataReader.RequestCount.Should().Be(2);
    }

    private static RemotePackageSizeResolver CreateResolver(
        IDownloadFileMetadataReader metadataReader,
        IS3ObjectManifestReader manifestReader)
    {
        return new RemotePackageSizeResolver(
            metadataReader,
            manifestReader,
            NullLogger<RemotePackageSizeResolver>.Instance);
    }

    private static LauncherContentVersion CreateS3Version(string version)
    {
        return new LauncherContentVersion
        {
            ModificationType = ModificationType.Mod,
            Name = "Rise of the Reds",
            Version = version,
            S3HostLink = "https://s3.example.test",
            S3BucketName = "mods",
            S3FolderName = $"rotr/{version}",
        };
    }

    private sealed class RecordingMetadataReader : IDownloadFileMetadataReader
    {
        private readonly Func<Uri, CancellationToken, Task<DownloadFileMetadata>>? _handler;

        public RecordingMetadataReader(
            Func<Uri, CancellationToken, Task<DownloadFileMetadata>>? handler = null)
        {
            _handler = handler;
        }

        public int RequestCount { get; private set; }

        public Task<DownloadFileMetadata> ReadMetadataAsync(Uri downloadUri, CancellationToken cancellationToken)
        {
            RequestCount++;
            return _handler?.Invoke(downloadUri, cancellationToken) ??
                   Task.FromException<DownloadFileMetadata>(new InvalidOperationException("Unexpected metadata read."));
        }
    }

    private sealed class RecordingManifestReader : IS3ObjectManifestReader
    {
        private readonly Queue<IReadOnlyList<RemoteFileManifestEntry>> _results;

        public RecordingManifestReader(params IReadOnlyList<RemoteFileManifestEntry>[] results)
        {
            _results = new Queue<IReadOnlyList<RemoteFileManifestEntry>>(results);
        }

        public int RequestCount { get; private set; }

        public Task<IReadOnlyList<RemoteFileManifestEntry>> ReadManifestAsync(
            S3ObjectManifestRequest request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return _results.Count > 0
                ? Task.FromResult(_results.Dequeue())
                : Task.FromException<IReadOnlyList<RemoteFileManifestEntry>>(
                    new InvalidOperationException("Unexpected manifest read."));
        }
    }
}
