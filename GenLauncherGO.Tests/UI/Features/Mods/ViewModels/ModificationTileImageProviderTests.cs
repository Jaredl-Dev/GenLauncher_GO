using System;
using System.IO;
using Avalonia.Media;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Features.Mods.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Mods.ViewModels;

public sealed class ModificationTileImageProviderTests
{
    private static readonly byte[] _png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public void LoadGrayscaleImageForModLoadsCachedImageWhenPresent()
    {
        using TestDirectory directory = new();
        string imagePath = CreateImageFile(directory, "cached.png");
        IModificationImageFileService imageFileService = Substitute.For<IModificationImageFileService>();
        imageFileService.FindExistingImageFilePath("ShockWave", "1.0").Returns(imagePath);
        imageFileService.ImageExists(imagePath).Returns(true);
        ModificationTileImageProvider provider = CreateProvider(imageFileService);

        IImage? result = StaTestRunner.Run(() => provider.LoadGrayscaleImage(
            new LauncherContent(CreateVersion()),
            CreateVersion(),
            localMod: false));

        result.Should().NotBeNull();
    }

    [Fact]
    public void LoadColorImageForNonModReturnsNullWithoutImageLookup()
    {
        IModificationImageFileService imageFileService = Substitute.For<IModificationImageFileService>();
        ModificationTileImageProvider provider = CreateProvider(imageFileService);
        LauncherContentVersion version = CreateVersion(ModificationType.Patch);

        IImage? result = provider.LoadColorImage(new LauncherContent(version), version, localMod: false);

        result.Should().BeNull();
        imageFileService.DidNotReceive().FindExistingImageFilePath(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public void LoadGrayscaleImageForMissingModImageUsesDefaultImage()
    {
        IModificationImageFileService imageFileService = Substitute.For<IModificationImageFileService>();
        imageFileService.FindExistingImageFilePath("ShockWave", "1.0").Returns((string?)null);
        imageFileService.ImageExists(null).Returns(false);
        ModificationTileImageProvider provider = CreateProvider(imageFileService);

        IImage? result = StaTestRunner.Run(() => provider.LoadGrayscaleImage(
            new LauncherContent(CreateVersion()),
            CreateVersion(),
            localMod: false));

        result.Should().NotBeNull();
    }

    [Fact]
    public void LoadGrayscaleImageForAdvertisingWithoutCachedImagesReturnsNull()
    {
        IModificationImageFileService imageFileService = Substitute.For<IModificationImageFileService>();
        imageFileService.CountImageFiles("Sponsor").Returns(0);
        ModificationTileImageProvider provider = CreateProvider(imageFileService);
        LauncherContentVersion version = CreateVersion(ModificationType.Advertising, ":Sponsor:");

        IImage? result = provider.LoadGrayscaleImage(new LauncherContent(version), version, localMod: false);

        result.Should().BeNull();
        imageFileService.Received(1).CountImageFiles("Sponsor");
    }

    [Fact]
    public void LoadGrayscaleImageForAdvertisingLoadsExistingIndexedImage()
    {
        using TestDirectory directory = new();
        string imagePath = CreateImageFile(directory, "advertising.png");
        IModificationImageFileService imageFileService = Substitute.For<IModificationImageFileService>();
        imageFileService.CountImageFiles("Sponsor").Returns(2);
        imageFileService.FindExistingImageFilePath("Sponsor", Arg.Any<string>()).Returns(imagePath);
        imageFileService.ImageExists(imagePath).Returns(true);
        ModificationTileImageProvider provider = CreateProvider(imageFileService);
        LauncherContentVersion version = CreateVersion(ModificationType.Advertising, "Sponsor");

        IImage? result = StaTestRunner.Run(() => provider.LoadGrayscaleImage(
            new LauncherContent(version),
            version,
            localMod: false));

        result.Should().NotBeNull();
    }

    [Fact]
    public void LoadColorImageDeletesUnreadableCachedModImageAndUsesDefaultImage()
    {
        using TestDirectory directory = new();
        string imagePath = CreateImageFile(directory, "unreadable.png");
        using FileStream imageLock = File.Open(imagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        IModificationImageFileService imageFileService = Substitute.For<IModificationImageFileService>();
        imageFileService.FindExistingImageFilePath("ShockWave", "1.0").Returns(imagePath);
        imageFileService.ImageExists(imagePath).Returns(true);
        ModificationTileImageProvider provider = CreateProvider(imageFileService);

        IImage? result = StaTestRunner.Run(() => provider.LoadColorImage(
            new LauncherContent(CreateVersion()),
            CreateVersion(),
            localMod: false));

        result.Should().NotBeNull();
        imageFileService.Received(1).TryDeleteImage("ShockWave", "1.0");
    }

    [Fact]
    public void LoadGrayscaleImageReturnsNullWhenUnreadableAdvertisingImageCannotBeDeleted()
    {
        using TestDirectory directory = new();
        string imagePath = CreateImageFile(directory, "unreadable-advertising.png");
        using FileStream imageLock = File.Open(imagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        IModificationImageFileService imageFileService = Substitute.For<IModificationImageFileService>();
        imageFileService.CountImageFiles("Sponsor").Returns(2);
        imageFileService.FindExistingImageFilePath("Sponsor", Arg.Any<string>()).Returns(imagePath);
        imageFileService.ImageExists(imagePath).Returns(true);
        imageFileService
            .When(service => service.TryDeleteImage("Sponsor", Arg.Any<string>()))
            .Do(_ => throw new UnauthorizedAccessException("Denied."));
        ModificationTileImageProvider provider = CreateProvider(imageFileService);
        LauncherContentVersion version = CreateVersion(ModificationType.Advertising, "Sponsor");

        IImage? result = StaTestRunner.Run(() => provider.LoadGrayscaleImage(
            new LauncherContent(version),
            version,
            localMod: false));

        result.Should().BeNull();
        imageFileService.Received(1).TryDeleteImage("Sponsor", Arg.Any<string>());
    }

    private static ModificationTileImageProvider CreateProvider(
        IModificationImageFileService imageFileService)
    {
        return new ModificationTileImageProvider(
            new ModificationImageSourceFactory(NullLogger<ModificationImageSourceFactory>.Instance),
            TestLauncherRuntimeContext.Create(),
            imageFileService,
            NullLogger.Instance);
    }

    private static string CreateImageFile(TestDirectory directory, string fileName)
    {
        string path = Path.Combine(directory.Path, fileName);
        File.WriteAllBytes(path, _png);
        return path;
    }

    private static LauncherContentVersion CreateVersion(
        ModificationType modificationType = ModificationType.Mod,
        string name = "ShockWave")
    {
        return new LauncherContentVersion
        {
            Name = name,
            Version = "1.0",
            ModificationType = modificationType
        };
    }
}
