using System;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Features.Mods.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Mods.ViewModels;

[Collection("Avalonia")]
public sealed class ModificationTileImageProviderTests
{
    [Fact]
    public void LoadGrayscaleImageForMod_LoadsCachedImageWhenPresent()
    {
        using TestDirectory directory = new();
        string imagePath = TestImageFile.Write(directory, "cached.png");
        IModificationImageFileService imageFileService = Substitute.For<IModificationImageFileService>();
        imageFileService.FindExistingImageFilePath("ShockWave", "1.0").Returns(imagePath);
        imageFileService.ImageExists(imagePath).Returns(true);
        ModificationImageSourceFactory imageSourceFactory = CreateImageSourceFactory();
        ModificationTileImageProvider provider = CreateProvider(imageFileService, imageSourceFactory);

        (IImage? Result, Bitmap DefaultImage) loaded = StaTestRunner.Run(() => (
            provider.LoadGrayscaleImage(
                new LauncherContent(TestLauncherContent.Version()),
                TestLauncherContent.Version(),
                false),
            imageSourceFactory.LoadDefaultImage(SupportedGame.ZeroHour, true)));

        loaded.Result.Should().NotBeNull();
        loaded.Result.Should().NotBeSameAs(loaded.DefaultImage);
    }

    [Fact]
    public void LoadColorImageForNonMod_ReturnsNullWithoutImageLookup()
    {
        IModificationImageFileService imageFileService = Substitute.For<IModificationImageFileService>();
        ModificationTileImageProvider provider = CreateProvider(imageFileService);
        LauncherContentVersion version = TestLauncherContent.Version(type: ModificationType.Patch);

        IImage? result = provider.LoadColorImage(new LauncherContent(version), version, false);

        result.Should().BeNull();
        imageFileService.DidNotReceive().FindExistingImageFilePath(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public void LoadGrayscaleImageForMissingModImage_UsesDefaultImage()
    {
        IModificationImageFileService imageFileService = Substitute.For<IModificationImageFileService>();
        imageFileService.FindExistingImageFilePath("ShockWave", "1.0").Returns((string?)null);
        imageFileService.ImageExists(null).Returns(false);
        ModificationImageSourceFactory imageSourceFactory = CreateImageSourceFactory();
        ModificationTileImageProvider provider = CreateProvider(imageFileService, imageSourceFactory);

        (IImage? Result, Bitmap DefaultImage) loaded = StaTestRunner.Run(() => (
            provider.LoadGrayscaleImage(
                new LauncherContent(TestLauncherContent.Version()),
                TestLauncherContent.Version(),
                false),
            imageSourceFactory.LoadDefaultImage(SupportedGame.ZeroHour, true)));

        loaded.Result.Should().BeSameAs(loaded.DefaultImage);
    }

    [Fact]
    public void LoadGrayscaleImageForAdvertisingWithoutCachedImages_ReturnsNull()
    {
        IModificationImageFileService imageFileService = Substitute.For<IModificationImageFileService>();
        imageFileService.CountImageFiles("Sponsor").Returns(0);
        ModificationTileImageProvider provider = CreateProvider(imageFileService);
        LauncherContentVersion version = TestLauncherContent.Version(":Sponsor:", type: ModificationType.Advertising);

        IImage? result = provider.LoadGrayscaleImage(new LauncherContent(version), version, false);

        result.Should().BeNull();
        imageFileService.Received(1).CountImageFiles("Sponsor");
    }

    [Fact]
    public void LoadGrayscaleImageForAdvertising_LoadsExistingIndexedImage()
    {
        using TestDirectory directory = new();
        string imagePath = TestImageFile.Write(directory, "advertising.png");
        IModificationImageFileService imageFileService = Substitute.For<IModificationImageFileService>();
        imageFileService.CountImageFiles("Sponsor").Returns(2);
        imageFileService.FindExistingImageFilePath("Sponsor", Arg.Any<string>()).Returns(imagePath);
        imageFileService.ImageExists(imagePath).Returns(true);
        ModificationTileImageProvider provider = CreateProvider(imageFileService);
        LauncherContentVersion version = TestLauncherContent.Version("Sponsor", type: ModificationType.Advertising);

        IImage? result = StaTestRunner.Run(() => provider.LoadGrayscaleImage(
            new LauncherContent(version),
            version,
            false));

        result.Should().NotBeNull();
    }

    [Fact]
    public void LoadColorImage_DeletesUnreadableCachedModImageAndUsesDefaultImage()
    {
        using TestDirectory directory = new();
        string imagePath = TestImageFile.Write(directory, "unreadable.png");
        using FileStream imageLock = File.Open(imagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        IModificationImageFileService imageFileService = Substitute.For<IModificationImageFileService>();
        imageFileService.FindExistingImageFilePath("ShockWave", "1.0").Returns(imagePath);
        imageFileService.ImageExists(imagePath).Returns(true);
        ModificationTileImageProvider provider = CreateProvider(imageFileService);

        IImage? result = StaTestRunner.Run(() => provider.LoadColorImage(
            new LauncherContent(TestLauncherContent.Version()),
            TestLauncherContent.Version(),
            false));

        result.Should().NotBeNull();
        imageFileService.Received(1).TryDeleteImage("ShockWave", "1.0");
    }

    [Fact]
    public void LoadGrayscaleImage_ReturnsNullWhenUnreadableAdvertisingImageCannotBeDeleted()
    {
        using TestDirectory directory = new();
        string imagePath = TestImageFile.Write(directory, "unreadable-advertising.png");
        using FileStream imageLock = File.Open(imagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        IModificationImageFileService imageFileService = Substitute.For<IModificationImageFileService>();
        imageFileService.CountImageFiles("Sponsor").Returns(2);
        imageFileService.FindExistingImageFilePath("Sponsor", Arg.Any<string>()).Returns(imagePath);
        imageFileService.ImageExists(imagePath).Returns(true);
        imageFileService
            .When(service => service.TryDeleteImage("Sponsor", Arg.Any<string>()))
            .Do(_ => throw new UnauthorizedAccessException("Denied."));
        ModificationTileImageProvider provider = CreateProvider(imageFileService);
        LauncherContentVersion version = TestLauncherContent.Version("Sponsor", type: ModificationType.Advertising);

        IImage? result = StaTestRunner.Run(() => provider.LoadGrayscaleImage(
            new LauncherContent(version),
            version,
            false));

        result.Should().BeNull();
        imageFileService.Received(1).TryDeleteImage("Sponsor", Arg.Any<string>());
    }

    [Fact]
    public void LoadThemeBackground_WithoutPublishedTheme_ReturnsNull()
    {
        IModificationImageFileService imageFileService = Substitute.For<IModificationImageFileService>();
        ModificationTileImageProvider provider = CreateProvider(imageFileService);
        LauncherContentVersion version = TestLauncherContent.Version();

        IImageBrush? result = provider.LoadThemeBackground(new LauncherContent(version), version);

        result.Should().BeNull();
        imageFileService.DidNotReceive().FindExistingImageFilePath(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public void LoadThemeBackground_WithoutBackgroundImageLink_ReturnsNull()
    {
        IModificationImageFileService imageFileService = Substitute.For<IModificationImageFileService>();
        ModificationTileImageProvider provider = CreateProvider(imageFileService);
        LauncherContentVersion version = TestLauncherContent.Version(theme: new LauncherContentTheme());

        IImageBrush? result = provider.LoadThemeBackground(new LauncherContent(version), version);

        result.Should().BeNull();
        imageFileService.DidNotReceive().FindExistingImageFilePath(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public void LoadThemeBackground_WithCachedArtwork_FillsTheShell()
    {
        using TestDirectory directory = new();
        string imagePath = TestImageFile.Write(directory, "background.png");
        LauncherContentVersion version = TestLauncherContent.Version(theme: new LauncherContentTheme
        {
            GenLauncherBackgroundImageLink = "https://example.test/background.png"
        });
        IModificationImageFileService imageFileService = Substitute.For<IModificationImageFileService>();
        imageFileService.FindExistingImageFilePath(Arg.Any<string>(), Arg.Any<string>()).Returns(imagePath);
        ModificationTileImageProvider provider = CreateProvider(imageFileService);

        IImageBrush? result = StaTestRunner.Run(() =>
            provider.LoadThemeBackground(new LauncherContent(version), version));

        result.Should().NotBeNull();
        result!.Stretch.Should().Be(Stretch.Fill);
        result.Source.Should().NotBeNull();
        imageFileService.Received(1).FindExistingImageFilePath(
            "ShockWave",
            LauncherContentTheme.ResolveBackgroundImageBaseName(version.Version));
    }

    private static ModificationImageSourceFactory CreateImageSourceFactory()
    {
        return new ModificationImageSourceFactory(NullLogger<ModificationImageSourceFactory>.Instance);
    }

    private static ModificationTileImageProvider CreateProvider(
        IModificationImageFileService imageFileService,
        ModificationImageSourceFactory? imageSourceFactory = null)
    {
        return new ModificationTileImageProvider(
            imageSourceFactory ?? CreateImageSourceFactory(),
            TestLauncherRuntimeContext.Create(),
            imageFileService,
            NullLogger.Instance);
    }
}
