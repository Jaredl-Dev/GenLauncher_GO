using System.Collections;
using System.Collections.Generic;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Tests.Core.Mods.Models;

/// <summary>
///     Pins the single authority for which files the launcher treats as content.
/// </summary>
/// <remarks>
///     Manual import, single-file package updates, the artwork cache, and the pickers a user chooses files with all
///     read these sets. Widening one silently makes the importer extract a file it should have copied; narrowing one
///     leaves a downloaded archive unextracted or an offered file rejected after the user picked it.
/// </remarks>
public sealed class LauncherContentFileTypesTests
{
    [Theory]
    [InlineData("mod.zip")]
    [InlineData("mod.rar")]
    [InlineData("mod.7z")]
    [InlineData("MOD.ZIP")]
    [InlineData("MOD.Rar")]
    [InlineData("MOD.7Z")]
    [InlineData(@"C:\downloads\nested.folder\mod.zip")]
    public void IsArchive_WithArchiveExtension_ReturnsTrue(string filePath)
    {
        LauncherContentFileTypes.IsArchive(filePath).Should().BeTrue();
    }

    [Theory]
    [InlineData("asset.big")]
    [InlineData("asset.gib")]
    [InlineData("readme.txt")]
    [InlineData("installer.exe")]
    [InlineData("mod.tar")]
    [InlineData("mod.gz")]
    [InlineData("mod.zipx")]
    [InlineData("mod.z")]
    [InlineData("zip")]
    [InlineData("mod")]
    [InlineData("")]
    public void IsArchive_WithoutArchiveExtension_ReturnsFalse(string filePath)
    {
        LauncherContentFileTypes.IsArchive(filePath).Should().BeFalse();
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".PNG")]
    [InlineData(".JPeG")]
    public void IsImage_WithAcceptedArtworkExtension_ReturnsTrue(string extension)
    {
        LauncherContentFileTypes.IsImage(extension).Should().BeTrue();
    }

    [Theory]
    [InlineData(".bmp")]
    [InlineData(".gif")]
    [InlineData(".webp")]
    [InlineData(".png.exe")]
    [InlineData("png")]
    [InlineData("")]
    public void IsImage_WithOtherExtension_ReturnsFalse(string extension)
    {
        LauncherContentFileTypes.IsImage(extension).Should().BeFalse();
    }

    /// <summary>
    ///     Spells the sets out once. Every other caller asks these properties instead of listing extensions, which is
    ///     what keeps them consistent but also leaves this the only place a changed set can be noticed — a test that
    ///     built its expectation from the same properties would move with the change and see nothing.
    /// </summary>
    [Fact]
    public void AcceptedFormats_PinTheSetsEveryLayerReadsFromHere()
    {
        LauncherContentFileTypes.ArchiveExtensions.Should().Equal(".zip", ".rar", ".7z");
        LauncherContentFileTypes.GamePackageExtensions.Should().Equal(".big", ".gib");
        LauncherContentFileTypes.ImageExtensions.Should().Equal(".png", ".jpg", ".jpeg");
        LauncherContentFileTypes.DefaultImageExtension.Should().Be(".png");
    }

    /// <summary>
    ///     A game package is copied into place, not unpacked. If the two sets ever overlapped, manual import would
    ///     try to extract a <c>.big</c> the game is meant to read directly.
    /// </summary>
    [Fact]
    public void GamePackagesAndArchives_DoNotOverlap()
    {
        LauncherContentFileTypes.GamePackageExtensions.Should()
            .NotIntersectWith(LauncherContentFileTypes.ArchiveExtensions);
    }

    [Fact]
    public void AcceptedFormats_CannotBeChangedByConsumers()
    {
        AssertReadOnly(LauncherContentFileTypes.ArchiveExtensions);
        AssertReadOnly(LauncherContentFileTypes.GamePackageExtensions);
        AssertReadOnly(LauncherContentFileTypes.ImageExtensions);
    }

    private static void AssertReadOnly(IReadOnlyList<string> extensions)
    {
        extensions.Should().NotBeAssignableTo<string[]>();

        if (extensions is ICollection<string> genericCollection)
        {
            genericCollection.IsReadOnly.Should().BeTrue();
        }

        if (extensions is IList collection)
        {
            collection.IsReadOnly.Should().BeTrue();
        }
    }
}
