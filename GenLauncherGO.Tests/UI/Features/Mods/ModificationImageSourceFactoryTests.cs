using System;
using System.IO;
using Avalonia.Media.Imaging;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Mods;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Mods;

public sealed class ModificationImageSourceFactoryTests
{
    [Theory]
    [InlineData(SupportedGame.Generals)]
    [InlineData(SupportedGame.ZeroHour)]
    public void LoadDefaultImageReturnsDecodedResourceImage(SupportedGame supportedGame)
    {
        StaTestRunner.Run(() =>
        {
            ModificationImageSourceFactory factory = CreateFactory();

            Bitmap image = factory.LoadDefaultImage(supportedGame, grayscale: false);

            image.PixelSize.Width.Should().BeGreaterThan(0);
            image.PixelSize.Height.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void LoadDefaultImageReturnsCachedGrayscaleResourceImage()
    {
        StaTestRunner.Run(() =>
        {
            ModificationImageSourceFactory factory = CreateFactory();

            Bitmap colorImage = factory.LoadDefaultImage(SupportedGame.Generals, grayscale: false);
            Bitmap grayscaleImage = factory.LoadDefaultImage(SupportedGame.Generals, grayscale: true);
            Bitmap cachedGrayscaleImage = factory.LoadDefaultImage(
                SupportedGame.Generals,
                grayscale: true);

            grayscaleImage.Should().NotBeSameAs(colorImage);
            cachedGrayscaleImage.Should().BeSameAs(grayscaleImage);
        });
    }

    [Fact]
    public void LoadFileImageReadsSourceIntoMemory()
    {
        StaTestRunner.Run(() =>
        {
            using TestDirectory testDirectory = new();
            string imagePath = Path.Combine(testDirectory.Path, "mod-image.png");
            SaveTestImage(imagePath);
            ModificationImageSourceFactory factory = CreateFactory();

            Bitmap? image = factory.LoadFileImage(imagePath, grayscale: false);
            File.Delete(imagePath);

            image.Should().NotBeNull();
            image!.PixelSize.Width.Should().Be(1);
            image.PixelSize.Height.Should().Be(1);
            File.Exists(imagePath).Should().BeFalse();
        });
    }

    [Fact]
    public void LoadFileImageReturnsNullWhenPathIsMissing()
    {
        StaTestRunner.Run(() =>
        {
            ModificationImageSourceFactory factory = CreateFactory();

            Bitmap? image = factory.LoadFileImage("missing-image.png", grayscale: false);

            image.Should().BeNull();
        });
    }

    private static ModificationImageSourceFactory CreateFactory()
    {
        return new ModificationImageSourceFactory(NullLogger<ModificationImageSourceFactory>.Instance);
    }

    private static void SaveTestImage(string path)
    {
        byte[] png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        File.WriteAllBytes(path, png);
    }

}
