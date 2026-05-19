using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Services;

public sealed class RemotePackageSizeResolverTests
{
    [Theory]
    [InlineData(3_355_443_200L, 3_355_443_200L)]
    [InlineData(0L, 0L)]
    [InlineData(null, null)]
    [InlineData(-1L, null)]
    public async Task GetTotalBytesAsync_ReturnsDirectFileContentLengthAsync(
        long? contentLength,
        long? expectedTotalBytes)
    {
        Uri downloadUri = new("https://example.test/contra.zip");
        StubDownloadFileMetadataReader metadataReader = new((uri, _) =>
            Task.FromResult(new DownloadFileMetadata(uri, "contra.zip", contentLength)));
        RecordingS3ObjectManifestReader manifestReader = new();
        RemotePackageSizeResolver resolver = CreateResolver(metadataReader, manifestReader);
        LauncherContentVersion version = TestLauncherContent.Version(
            "Contra",
            "009",
            simpleDownloadLink: downloadUri.ToString());

        long? totalBytes = await resolver.GetTotalBytesAsync(version, CancellationToken.None);

        totalBytes.Should().Be(expectedTotalBytes);
        metadataReader.RequestCount.Should().Be(1);
        manifestReader.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTotalBytesAsync_SumsS3ManifestIncludingEmptyManifestAsync()
    {
        StubDownloadFileMetadataReader metadataReader = new();
        RecordingS3ObjectManifestReader manifestReader = new();
        manifestReader.Enqueue(
            new RemoteFileManifestEntry("Data/one.big", "hash", 10),
            new RemoteFileManifestEntry("Data/two.big", "hash", 15));
        manifestReader.Enqueue();
        RemotePackageSizeResolver resolver = CreateResolver(metadataReader, manifestReader);

        long? populatedSize = await resolver.GetTotalBytesAsync(CreateS3Version("1.0"), CancellationToken.None);
        long? emptySize = await resolver.GetTotalBytesAsync(CreateS3Version("2.0"), CancellationToken.None);

        populatedSize.Should().Be(25);
        emptySize.Should().Be(0);
        metadataReader.RequestCount.Should().Be(0);
        manifestReader.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetTotalBytesAsync_ReturnsUnavailableForUnsupportedSourceAsync()
    {
        StubDownloadFileMetadataReader metadataReader = new();
        RecordingS3ObjectManifestReader manifestReader = new();
        RemotePackageSizeResolver resolver = CreateResolver(metadataReader, manifestReader);
        LauncherContentVersion version = TestLauncherContent.Version("Manual Mod");

        long? totalBytes = await resolver.GetTotalBytesAsync(version, CancellationToken.None);

        totalBytes.Should().BeNull();
        metadataReader.RequestCount.Should().Be(0);
        manifestReader.Requests.Should().BeEmpty();
    }

    /// <summary>
    ///     An unreachable provider is cached as "size unknown" so a catalog that lists many offline packages does not
    ///     repeat the same failing request for every card that asks again.
    /// </summary>
    [Fact]
    public async Task GetTotalBytesAsync_MapsMetadataFailureToUnavailableAsync()
    {
        StubDownloadFileMetadataReader metadataReader = new((_, _) =>
            Task.FromException<DownloadFileMetadata>(new HttpRequestException("Offline")));
        RemotePackageSizeResolver resolver = CreateResolver(metadataReader, new RecordingS3ObjectManifestReader());
        LauncherContentVersion version = TestLauncherContent.Version(
            "Contra",
            "009",
            simpleDownloadLink: "https://example.test/contra.zip");

        long? firstAttempt = await resolver.GetTotalBytesAsync(version, CancellationToken.None);
        long? secondAttempt = await resolver.GetTotalBytesAsync(version, CancellationToken.None);

        firstAttempt.Should().BeNull();
        secondAttempt.Should().BeNull();
        metadataReader.RequestCount.Should().Be(1);
    }

    /// <summary>
    ///     Cancellation is the caller's decision, not the provider's answer, so it must surface and must not be cached
    ///     as an unknown size that suppresses every later lookup.
    /// </summary>
    [Fact]
    public async Task GetTotalBytesAsync_CanceledMetadataRead_LeavesSizeResolvableAsync()
    {
        StubDownloadFileMetadataReader metadataReader = new((uri, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new DownloadFileMetadata(uri, "contra.zip", 42));
        });
        RemotePackageSizeResolver resolver = CreateResolver(metadataReader, new RecordingS3ObjectManifestReader());
        LauncherContentVersion version = TestLauncherContent.Version(
            "Contra",
            "009",
            simpleDownloadLink: "https://example.test/contra.zip");
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> canceledRead = () => resolver.GetTotalBytesAsync(version, cancellation.Token);

        await canceledRead.Should().ThrowAsync<OperationCanceledException>();
        (await resolver.GetTotalBytesAsync(version, CancellationToken.None)).Should().Be(42);
        metadataReader.RequestCount.Should().Be(2);
    }

    [Fact]
    public async Task GetTotalBytesAsync_CachesByContentVersionAndSourceMetadataAsync()
    {
        StubDownloadFileMetadataReader metadataReader = new((uri, _) =>
            Task.FromResult(new DownloadFileMetadata(uri, "package.zip", 42)));
        RemotePackageSizeResolver resolver = CreateResolver(metadataReader, new RecordingS3ObjectManifestReader());
        LauncherContentVersion firstVersion = TestLauncherContent.Version(
            "Contra",
            "1.0",
            simpleDownloadLink: "https://example.test/contra.zip");
        LauncherContentVersion changedVersion = TestLauncherContent.Version(
            "Contra",
            "2.0",
            simpleDownloadLink: "https://example.test/contra.zip");
        LauncherContentVersion relinkedVersion = TestLauncherContent.Version(
            "Contra",
            "2.0",
            simpleDownloadLink: "https://mirror.example.test/contra.zip");

        await resolver.GetTotalBytesAsync(firstVersion, CancellationToken.None);
        await resolver.GetTotalBytesAsync(firstVersion, CancellationToken.None);
        await resolver.GetTotalBytesAsync(changedVersion, CancellationToken.None);
        await resolver.GetTotalBytesAsync(relinkedVersion, CancellationToken.None);

        metadataReader.RequestCount.Should().Be(3);
    }

    /// <summary>
    ///     The same content version republished from another bucket folder is a different payload, so the cached size
    ///     from the previous folder must not be reused.
    /// </summary>
    [Fact]
    public async Task GetTotalBytesAsync_ChangedS3FolderName_ResolvesTheRelocatedPackageAsync()
    {
        RecordingS3ObjectManifestReader manifestReader = new();
        manifestReader.Enqueue(new RemoteFileManifestEntry("Data/one.big", "hash", 10));
        manifestReader.Enqueue(
            new RemoteFileManifestEntry("Data/one.big", "hash", 10),
            new RemoteFileManifestEntry("Data/two.big", "hash", 15));
        RemotePackageSizeResolver resolver = CreateResolver(new StubDownloadFileMetadataReader(), manifestReader);
        LauncherContentVersion version = CreateS3Version("1.0");
        LauncherContentVersion relocatedVersion = TestLauncherContent.S3Version(
            "Rise of the Reds",
            "1.0",
            s3FolderName: "rotr/1.0-mirror");

        long? originalSize = await resolver.GetTotalBytesAsync(version, CancellationToken.None);
        long? relocatedSize = await resolver.GetTotalBytesAsync(relocatedVersion, CancellationToken.None);

        originalSize.Should().Be(10);
        relocatedSize.Should().Be(25);
        manifestReader.Requests.Should().HaveCount(2);
    }

    /// <summary>
    ///     A manifest whose declared sizes cannot add up to a real package size is reported as "size unknown". Wrapping
    ///     the addition around, or truncating it into a negative <see cref="long" />, would put a plausible but wrong
    ///     download size in front of the user.
    /// </summary>
    [Theory]
    [InlineData(ulong.MaxValue)]
    [InlineData((ulong)long.MaxValue)]
    public async Task GetTotalBytesAsync_ManifestSizesExceedARealPackage_ReportsUnavailableAsync(ulong firstEntrySize)
    {
        RecordingS3ObjectManifestReader manifestReader = new();
        manifestReader.Enqueue(
            new RemoteFileManifestEntry("Data/one.big", "hash", firstEntrySize),
            new RemoteFileManifestEntry("Data/two.big", "hash", 1));
        RemotePackageSizeResolver resolver = CreateResolver(new StubDownloadFileMetadataReader(), manifestReader);

        long? totalBytes = await resolver.GetTotalBytesAsync(CreateS3Version("1.0"), CancellationToken.None);

        totalBytes.Should().BeNull();
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
        return TestLauncherContent.S3Version(
            "Rise of the Reds",
            version,
            s3FolderName: $"rotr/{version}");
    }
}
