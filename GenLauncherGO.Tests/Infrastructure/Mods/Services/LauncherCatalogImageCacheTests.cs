using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Mods.Models;
using GenLauncherGO.Infrastructure.Mods.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Mods.Services;

public sealed class LauncherCatalogImageCacheTests
{
    [Theory]
    [InlineData("https://cdn.example.test/card.jpeg", "1.2.jpeg")]
    [InlineData("https://cdn.example.test/card.webp", "1.2.png")]
    [InlineData("https://cdn.example.test/card", "1.2.png")]
    public async Task CacheModificationImagesAsync_DownloadsCardImageToExpectedPathAsync(
        string cardLink,
        string expectedImageFileName)
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        var assetDownloader = new RecordingRemoteAssetDownloader();
        LauncherCatalogImageCache cache = CreateCache(assetDownloader);
        var cardUri = new Uri(cardLink);
        var modification = new LauncherContentVersion
        {
            Name = "ShockWave",
            Version = "1.2",
            UIImageSourceLink = cardUri.ToString()
        };

        await cache.CacheModificationImagesAsync(modification, paths, CancellationToken.None);

        assetDownloader.Calls.Should().ContainSingle(call =>
            call.SourceUri == cardUri &&
            call.DestinationFilePath == paths.GetModificationImageFilePath("ShockWave", expectedImageFileName));
    }

    [Fact]
    public async Task CacheModificationImagesAsync_SkipsEmptyImageLinksAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        var assetDownloader = new RecordingRemoteAssetDownloader();
        LauncherCatalogImageCache cache = CreateCache(assetDownloader);
        var modification = new LauncherContentVersion
        {
            Name = "ShockWave",
            Version = "1.2",
            UIImageSourceLink = string.Empty
        };

        await cache.CacheModificationImagesAsync(modification, paths, CancellationToken.None);

        assetDownloader.Calls.Should().BeEmpty();
    }

    /// <summary>
    ///     A themed modification re-skins the shell the moment it is picked, and still does so after an offline
    ///     restart, which only works if both halves of the theme are cached ahead of selection.
    /// </summary>
    [Fact]
    public async Task CacheModificationImagesAsync_CachesPublishedPaletteAndBackgroundArtworkAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        var assetDownloader = new RecordingRemoteAssetDownloader();
        var themeCache = new FakeModificationThemeCache();
        LauncherCatalogImageCache cache = CreateCache(assetDownloader, themeCache);
        var backgroundUri = new Uri("https://cdn.example.test/background.jpg");
        var theme = new LauncherContentTheme
        {
            GenLauncherActiveColor = "#baff0c",
            GenLauncherBackgroundImageLink = backgroundUri.ToString()
        };
        var modification = new LauncherContentVersion
        {
            Name = "ShockWave",
            Version = "1.2",
            UIImageSourceLink = "https://cdn.example.test/card.png",
            Theme = theme
        };

        await cache.CacheModificationImagesAsync(modification, paths, CancellationToken.None);

        themeCache.Entries[("ShockWave", "1.2")].Should().BeSameAs(theme);
        assetDownloader.Calls.Should().ContainSingle(call =>
            call.SourceUri == backgroundUri &&
            call.DestinationFilePath == paths.GetModificationImageFilePath(
                "ShockWave",
                LauncherContentTheme.ResolveBackgroundImageBaseName("1.2") + ".jpg"));
    }

    [Fact]
    public async Task CacheModificationImagesAsync_CachesPublishedPaletteThatDeclaresNoBackgroundArtworkAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        var assetDownloader = new RecordingRemoteAssetDownloader();
        var themeCache = new FakeModificationThemeCache();
        LauncherCatalogImageCache cache = CreateCache(assetDownloader, themeCache);
        var cardUri = new Uri("https://cdn.example.test/card.png");
        var theme = new LauncherContentTheme { GenLauncherActiveColor = "#baff0c" };
        var modification = new LauncherContentVersion
        {
            Name = "ShockWave",
            Version = "1.2",
            UIImageSourceLink = cardUri.ToString(),
            Theme = theme
        };

        await cache.CacheModificationImagesAsync(modification, paths, CancellationToken.None);

        themeCache.Entries[("ShockWave", "1.2")].Should().BeSameAs(theme);
        assetDownloader.Calls.Should().Equal(
            (cardUri, paths.GetModificationImageFilePath("ShockWave", "1.2.png")));
    }

    [Fact]
    public async Task CacheModificationImagesAsync_ContinuesWhenCardImageDownloadFailsAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        var assetDownloader = new RecordingRemoteAssetDownloader();
        LauncherCatalogImageCache cache = CreateCache(assetDownloader);
        var cardUri = new Uri("https://cdn.example.test/card.png");
        var modification = new LauncherContentVersion
        {
            Name = "ShockWave",
            Version = "1.2",
            UIImageSourceLink = cardUri.ToString()
        };
        assetDownloader.Handler = (_, _, _) => Task.FromException(new IOException("Download failed."));

        await cache.CacheModificationImagesAsync(modification, paths, CancellationToken.None);

        assetDownloader.Calls.Should().ContainSingle(call =>
            call.SourceUri == cardUri &&
            call.DestinationFilePath == paths.GetModificationImageFilePath("ShockWave", "1.2.png"));
    }

    [Fact]
    public async Task CacheModificationImagesAsync_RethrowsCancellationAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        var assetDownloader = new RecordingRemoteAssetDownloader();
        LauncherCatalogImageCache cache = CreateCache(assetDownloader);
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

    [Fact]
    public async Task CacheModificationImagesAsync_DoesNotDownloadThroughLinkedImageDirectoryAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        string outsideDirectory = directory.CreateDirectory("OutsideImages");
        ReparsePointTestSupport.CreateDirectoryJunction(
            paths.GetModificationImagesDirectory("ShockWave"),
            outsideDirectory);
        var assetDownloader = new RecordingRemoteAssetDownloader();
        LauncherCatalogImageCache cache = CreateCache(assetDownloader);
        var modification = new LauncherContentVersion
        {
            Name = "ShockWave",
            Version = "1.2",
            UIImageSourceLink = "https://cdn.example.test/card.png"
        };

        await cache.CacheModificationImagesAsync(modification, paths, CancellationToken.None);

        assetDownloader.Calls.Should().BeEmpty();
        Directory.EnumerateFileSystemEntries(outsideDirectory).Should().BeEmpty();
    }

    [Fact]
    public async Task CacheAdvertisingImagesAsync_DeletesStaleImagesWhenImageCountChangesAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        Directory.CreateDirectory(paths.GetModificationImagesDirectory("Featured Mod"));
        string staleImagePath = paths.GetModificationImageFilePath("Featured Mod", "old.png");
        await File.WriteAllTextAsync(staleImagePath, "stale");
        var assetDownloader = new RecordingRemoteAssetDownloader();
        LauncherCatalogImageCache cache = CreateCache(assetDownloader);
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
    public async Task CacheAdvertisingImagesAsync_TrimsInvalidTrailingNameCharactersLikeLegacyCacheAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        var assetDownloader = new RecordingRemoteAssetDownloader();
        LauncherCatalogImageCache cache = CreateCache(assetDownloader);
        var advertisingData = new RemoteAdvertisingReference(
            "Do you like GenLauncher?",
            "https://example.test/advertising.yaml",
            new[] { "https://cdn.example.test/0.jpg" });

        await cache.CacheAdvertisingImagesAsync(advertisingData, paths, CancellationToken.None);

        assetDownloader.Calls.Should().ContainSingle(call =>
            call.SourceUri == new Uri("https://cdn.example.test/0.jpg") &&
            call.DestinationFilePath == paths.GetModificationImageFilePath("Do you like GenLauncher", "0.jpg"));
    }

    [Fact]
    public async Task CacheAdvertisingImagesAsync_ContinuesWhenStaleImageCannotBeDeletedAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        Directory.CreateDirectory(paths.GetModificationImagesDirectory("Featured Mod"));
        string staleImagePath = paths.GetModificationImageFilePath("Featured Mod", "old.png");
        await File.WriteAllTextAsync(staleImagePath, "stale");
        await using FileStream lockedImage = File.Open(
            staleImagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);
        var assetDownloader = new RecordingRemoteAssetDownloader();
        LauncherCatalogImageCache cache = CreateCache(assetDownloader);
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
    public async Task CacheAdvertisingImagesAsync_KeepsExistingImagesWhenImageCountMatchesAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory.Path);
        Directory.CreateDirectory(paths.GetModificationImagesDirectory("Featured Mod"));
        string firstExistingImagePath = paths.GetModificationImageFilePath("Featured Mod", "0.png");
        string secondExistingImagePath = paths.GetModificationImageFilePath("Featured Mod", "1.png");
        await File.WriteAllTextAsync(firstExistingImagePath, "existing");
        await File.WriteAllTextAsync(secondExistingImagePath, "existing");
        var assetDownloader = new RecordingRemoteAssetDownloader();
        LauncherCatalogImageCache cache = CreateCache(assetDownloader);
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

    [Fact]
    public async Task CacheAdvertisingImagesAsync_DoesNotMutateThroughLinkedImageDirectoryAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        string outsideDirectory = directory.CreateDirectory("OutsideImages");
        string firstOutsideImage = Path.Combine(outsideDirectory, "old-1.png");
        string secondOutsideImage = Path.Combine(outsideDirectory, "old-2.png");
        await File.WriteAllTextAsync(firstOutsideImage, "first");
        await File.WriteAllTextAsync(secondOutsideImage, "second");
        ReparsePointTestSupport.CreateDirectoryJunction(
            paths.GetModificationImagesDirectory("Featured Mod"),
            outsideDirectory);
        var assetDownloader = new RecordingRemoteAssetDownloader();
        LauncherCatalogImageCache cache = CreateCache(assetDownloader);
        var advertisingData = new RemoteAdvertisingReference(
            "Featured Mod",
            "https://example.test/featured.yaml",
            new List<string>
            {
                "https://cdn.example.test/0.jpg"
            });

        await cache.CacheAdvertisingImagesAsync(advertisingData, paths, CancellationToken.None);

        assetDownloader.Calls.Should().BeEmpty();
        (await File.ReadAllTextAsync(firstOutsideImage)).Should().Be("first");
        (await File.ReadAllTextAsync(secondOutsideImage)).Should().Be("second");
    }

    /// <summary>
    ///     A cache folder is only fully owned when nothing inside it leads out of the launcher tree. One link below it
    ///     is enough to stop both halves of the refresh: the stale-image sweep, which deletes files the launcher would
    ///     no longer be able to account for, and the download that would write into that same folder.
    /// </summary>
    [Fact]
    public async Task CacheAdvertisingImagesAsync_KeepsCachedImagesWhenTheCacheFolderContainsALinkAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        string imageFolder = paths.GetModificationImagesDirectory("Featured Mod");
        Directory.CreateDirectory(imageFolder);
        string staleImagePath = Path.Combine(imageFolder, "old.png");
        await File.WriteAllTextAsync(staleImagePath, "stale");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(
            directory,
            Path.Combine(imageFolder, "Linked"));
        var assetDownloader = new RecordingRemoteAssetDownloader();
        LauncherCatalogImageCache cache = CreateCache(assetDownloader);
        var advertisingData = new RemoteAdvertisingReference(
            "Featured Mod",
            "https://example.test/featured.yaml",
            new List<string>
            {
                "https://cdn.example.test/0.jpg",
                "https://cdn.example.test/1.jpg"
            });

        await cache.CacheAdvertisingImagesAsync(advertisingData, paths, CancellationToken.None);

        File.Exists(staleImagePath).Should().BeTrue();
        assetDownloader.Calls.Should().BeEmpty();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    private static LauncherCatalogImageCache CreateCache(
        RecordingRemoteAssetDownloader assetDownloader,
        FakeModificationThemeCache? themeCache = null)
    {
        return new LauncherCatalogImageCache(
            assetDownloader,
            themeCache ?? new FakeModificationThemeCache(),
            NullLogger<LauncherCatalogImageCache>.Instance);
    }
}
