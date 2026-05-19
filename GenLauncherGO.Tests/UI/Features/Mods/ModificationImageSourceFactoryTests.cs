using System;
using System.IO;
using Avalonia.Media.Imaging;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Mods;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Mods;

[Collection("Avalonia")]
public sealed class ModificationImageSourceFactoryTests
{
    /// <summary>
    ///     A three-by-two PNG, so overwriting a cached one-pixel file with it changes the file's length.
    /// </summary>
    private static readonly byte[] _threeByTwoPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAMAAAACCAYAAACddGYaAAAAEUlEQVR4AWPgUbL4D8MMyBwAZBMIX0t99wsAAAAASUVORK5CYII=");

    [Fact]
    public void LoadDefaultImage_SeparatesTheTwoGameBanners()
    {
        StaTestRunner.Run(() =>
        {
            ModificationImageSourceFactory factory = CreateFactory();

            Bitmap generalsImage = factory.LoadDefaultImage(SupportedGame.Generals, false);
            Bitmap zeroHourImage = factory.LoadDefaultImage(SupportedGame.ZeroHour, false);

            generalsImage.Should().NotBeSameAs(zeroHourImage);
        });
    }

    [Fact]
    public void LoadDefaultImage_ReturnsCachedGrayscaleResourceImage()
    {
        StaTestRunner.Run(() =>
        {
            ModificationImageSourceFactory factory = CreateFactory();

            Bitmap colorImage = factory.LoadDefaultImage(SupportedGame.Generals, false);
            Bitmap grayscaleImage = factory.LoadDefaultImage(SupportedGame.Generals, true);
            Bitmap cachedGrayscaleImage = factory.LoadDefaultImage(
                SupportedGame.Generals,
                true);

            grayscaleImage.Should().NotBeSameAs(colorImage);
            cachedGrayscaleImage.Should().BeSameAs(grayscaleImage);
        });
    }

    [Fact]
    public void LoadFileImage_ReadsSourceIntoMemory()
    {
        StaTestRunner.Run(() =>
        {
            using TestDirectory testDirectory = new();
            string imagePath = TestImageFile.Write(testDirectory, "mod-image.png");
            ModificationImageSourceFactory factory = CreateFactory();

            Bitmap? image = factory.LoadFileImage(imagePath, false);
            File.WriteAllBytes(imagePath, _threeByTwoPng);
            Bitmap? replacedImage = factory.LoadFileImage(imagePath, false);
            File.Delete(imagePath);

            image.Should().NotBeNull();
            replacedImage.Should().NotBeNull();
            replacedImage.Should().NotBeSameAs(image);
            File.Exists(imagePath).Should().BeFalse();
        });
    }

    [Fact]
    public void LoadFileImage_ReturnsNullWhenPathIsMissing()
    {
        StaTestRunner.Run(() =>
        {
            ModificationImageSourceFactory factory = CreateFactory();

            Bitmap? image = factory.LoadFileImage("missing-image.png", false);

            image.Should().BeNull();
        });
    }

    private static ModificationImageSourceFactory CreateFactory()
    {
        return new ModificationImageSourceFactory(NullLogger<ModificationImageSourceFactory>.Instance);
    }
}
