using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Mods.Models;
using GenLauncherGO.Infrastructure.Mods.Services;
using GenLauncherGO.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Mods.Services;

public sealed class LauncherCatalogImageCacheTests
{
    [Theory]
    [InlineData("https://cdn.example.test/card.jpeg", "1.2.jpeg")]
    [InlineData("https://cdn.example.test/card.webp", "1.2.png")]
    public async Task CacheModificationImagesAsyncDownloadsCardImageToExpectedPathAsync(
        string cardLink,
        string expectedImageFileName)
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var assetDownloader = new RecordingRemoteAssetDownloader();
        var cache = new LauncherCatalogImageCache(
            assetDownloader,
            NullLogger<LauncherCatalogImageCache>.Instance);
        var cardUri = new Uri(cardLink);
        var modification = new LauncherContentVersion
        {
            Name = "ShockWave",
            Version = "1.2",
            UIImageSourceLink = cardUri.ToString(),
        };

        await cache.CacheModificationImagesAsync(modification, paths, CancellationToken.None);

        assetDownloader.Calls.Should().ContainSingle(call =>
            call.SourceUri == cardUri &&
            call.DestinationFilePath == paths.GetModificationImageFilePath("ShockWave", expectedImageFileName));
    }

    [Fact]
    public async Task CacheModificationImagesAsyncSkipsEmptyImageLinksAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var assetDownloader = new RecordingRemoteAssetDownloader();
        var cache = new LauncherCatalogImageCache(
            assetDownloader,
            NullLogger<LauncherCatalogImageCache>.Instance);
        var modification = new LauncherContentVersion
        {
            Name = "ShockWave",
            Version = "1.2",
            UIImageSourceLink = string.Empty,
        };

        await cache.CacheModificationImagesAsync(modification, paths, CancellationToken.None);

        assetDownloader.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task CacheModificationImagesAsyncContinuesWhenCardImageDownloadFailsAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var assetDownloader = new RecordingRemoteAssetDownloader();
        var cache = new LauncherCatalogImageCache(
            assetDownloader,
            NullLogger<LauncherCatalogImageCache>.Instance);
        var cardUri = new Uri("https://cdn.example.test/card.png");
        var modification = new LauncherContentVersion
        {
            Name = "ShockWave",
            Version = "1.2",
            UIImageSourceLink = cardUri.ToString(),
        };
        assetDownloader.Handler = (_, _, _) => Task.FromException(new IOException("Download failed."));

        await cache.CacheModificationImagesAsync(modification, paths, CancellationToken.None);

        assetDownloader.Calls.Should().ContainSingle(call =>
            call.SourceUri == cardUri &&
            call.DestinationFilePath == paths.GetModificationImageFilePath("ShockWave", "1.2.png"));
    }

    [Fact]
    public async Task CacheModificationImagesAsyncRethrowsCancellationAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var assetDownloader = new RecordingRemoteAssetDownloader();
        var cache = new LauncherCatalogImageCache(
            assetDownloader,
            NullLogger<LauncherCatalogImageCache>.Instance);
        var cancellationTokenSource = new CancellationTokenSource();
        var imageUri = new Uri("https://cdn.example.test/card.png");
        var modification = new LauncherContentVersion
        {
            Name = "ShockWave",
            Version = "1.2",
            UIImageSourceLink = imageUri.ToString()
        };
        cancellationTokenSource.Cancel();
        assetDownloader.Handler = (_, _, _) => Task.FromCanceled(cancellationTokenSource.Token);

        Func<Task> act = () => cache.CacheModificationImagesAsync(
            modification,
            paths,
            cancellationTokenSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [SymbolicLinkFact]
    public async Task CacheModificationImagesAsyncDoesNotDownloadThroughLinkedImageDirectoryAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        string outsideDirectory = directory.CreateDirectory("OutsideImages");
        SymbolicLinkTestSupport.CreateDirectoryLink(
            paths.GetModificationImagesDirectory("ShockWave"),
            outsideDirectory);
        var assetDownloader = new RecordingRemoteAssetDownloader();
        var cache = new LauncherCatalogImageCache(
            assetDownloader,
            NullLogger<LauncherCatalogImageCache>.Instance);
        var modification = new LauncherContentVersion
        {
            Name = "ShockWave",
            Version = "1.2",
            UIImageSourceLink = "https://cdn.example.test/card.png",
        };

        await cache.CacheModificationImagesAsync(modification, paths, CancellationToken.None);

        assetDownloader.Calls.Should().BeEmpty();
        Directory.EnumerateFileSystemEntries(outsideDirectory).Should().BeEmpty();
    }

    [Fact]
    public async Task CacheAdvertisingImagesAsyncDeletesStaleImagesWhenImageCountChangesAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        Directory.CreateDirectory(paths.GetModificationImagesDirectory("Featured Mod"));
        string staleImagePath = paths.GetModificationImageFilePath("Featured Mod", "old.png");
        await File.WriteAllTextAsync(staleImagePath, "stale");
        var assetDownloader = new RecordingRemoteAssetDownloader();
        var cache = new LauncherCatalogImageCache(
            assetDownloader,
            NullLogger<LauncherCatalogImageCache>.Instance);
        var advertisingData = new RemoteAdvertisingReference(
            "Featured Mod",
            "https://example.test/featured.yaml",
            new List<string>
            {
                "https://cdn.example.test/0.jpg",
                "https://cdn.example.test/1.jpg"
            });

        await cache.CacheAdvertisingImagesAsync(advertisingData, paths, CancellationToken.None);

        File.Exists(staleImagePath).Should().BeFalse();
        assetDownloader.Calls.Should().ContainSingle(call =>
            call.SourceUri == new Uri("https://cdn.example.test/0.jpg") &&
            call.DestinationFilePath == paths.GetModificationImageFilePath("Featured Mod", "0.jpg"));
        assetDownloader.Calls.Should().ContainSingle(call =>
            call.SourceUri == new Uri("https://cdn.example.test/1.jpg") &&
            call.DestinationFilePath == paths.GetModificationImageFilePath("Featured Mod", "1.jpg"));
    }

    [Fact]
    public async Task CacheAdvertisingImagesAsyncContinuesWhenStaleImageCannotBeDeletedAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        Directory.CreateDirectory(paths.GetModificationImagesDirectory("Featured Mod"));
        string staleImagePath = paths.GetModificationImageFilePath("Featured Mod", "old.png");
        await File.WriteAllTextAsync(staleImagePath, "stale");
        await using FileStream lockedImage = File.Open(
            staleImagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);
        var assetDownloader = new RecordingRemoteAssetDownloader();
        var cache = new LauncherCatalogImageCache(
            assetDownloader,
            NullLogger<LauncherCatalogImageCache>.Instance);
        var advertisingData = new RemoteAdvertisingReference(
            "Featured Mod",
            "https://example.test/featured.yaml",
            new List<string>
            {
                "https://cdn.example.test/0.jpg",
                "https://cdn.example.test/1.jpg"
            });

        Func<Task> act = () => cache.CacheAdvertisingImagesAsync(
            advertisingData,
            paths,
            CancellationToken.None);

        await act.Should().NotThrowAsync();
        assetDownloader.Calls.Should().ContainSingle(call =>
            call.SourceUri == new Uri("https://cdn.example.test/0.jpg") &&
            call.DestinationFilePath == paths.GetModificationImageFilePath("Featured Mod", "0.jpg"));
    }

    [Fact]
    public async Task CacheAdvertisingImagesAsyncKeepsExistingImagesWhenImageCountMatchesAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        Directory.CreateDirectory(paths.GetModificationImagesDirectory("Featured Mod"));
        string firstExistingImagePath = paths.GetModificationImageFilePath("Featured Mod", "0.png");
        string secondExistingImagePath = paths.GetModificationImageFilePath("Featured Mod", "1.png");
        await File.WriteAllTextAsync(firstExistingImagePath, "existing");
        await File.WriteAllTextAsync(secondExistingImagePath, "existing");
        var assetDownloader = new RecordingRemoteAssetDownloader();
        var cache = new LauncherCatalogImageCache(
            assetDownloader,
            NullLogger<LauncherCatalogImageCache>.Instance);
        var advertisingData = new RemoteAdvertisingReference(
            "Featured Mod",
            "https://example.test/featured.yaml",
            new List<string>
            {
                "https://cdn.example.test/0.png",
                "https://cdn.example.test/1.png"
            });

        await cache.CacheAdvertisingImagesAsync(advertisingData, paths, CancellationToken.None);

        File.Exists(firstExistingImagePath).Should().BeTrue();
        File.Exists(secondExistingImagePath).Should().BeTrue();
        assetDownloader.Calls.Should().ContainSingle(call =>
            call.SourceUri == new Uri("https://cdn.example.test/0.png") &&
            call.DestinationFilePath == paths.GetModificationImageFilePath("Featured Mod", "0.png"));
        assetDownloader.Calls.Should().ContainSingle(call =>
            call.SourceUri == new Uri("https://cdn.example.test/1.png") &&
            call.DestinationFilePath == paths.GetModificationImageFilePath("Featured Mod", "1.png"));
    }

    [SymbolicLinkFact]
    public async Task CacheAdvertisingImagesAsyncDoesNotMutateThroughLinkedImageDirectoryAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        string outsideDirectory = directory.CreateDirectory("OutsideImages");
        string firstOutsideImage = Path.Combine(outsideDirectory, "old-1.png");
        string secondOutsideImage = Path.Combine(outsideDirectory, "old-2.png");
        await File.WriteAllTextAsync(firstOutsideImage, "first");
        await File.WriteAllTextAsync(secondOutsideImage, "second");
        SymbolicLinkTestSupport.CreateDirectoryLink(
            paths.GetModificationImagesDirectory("Featured Mod"),
            outsideDirectory);
        var assetDownloader = new RecordingRemoteAssetDownloader();
        var cache = new LauncherCatalogImageCache(
            assetDownloader,
            NullLogger<LauncherCatalogImageCache>.Instance);
        var advertisingData = new RemoteAdvertisingReference(
            "Featured Mod",
            "https://example.test/featured.yaml",
            new List<string>
            {
                "https://cdn.example.test/0.jpg",
            });

        await cache.CacheAdvertisingImagesAsync(advertisingData, paths, CancellationToken.None);

        assetDownloader.Calls.Should().BeEmpty();
        File.ReadAllText(firstOutsideImage).Should().Be("first");
        File.ReadAllText(secondOutsideImage).Should().Be("second");
    }

    private static LauncherPaths CreatePaths(string root)
    {
        return TestLauncherPaths.Create(root);
    }
}
