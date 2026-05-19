using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Tests.Core.Mods.Models;

public sealed class LauncherContentThemeTests
{
    [Fact]
    public void ThemeSlots_ThatAModificationDidNotPublish_AreEmptyRatherThanNull()
    {
        LauncherContentTheme theme = new();

        theme.GenLauncherBorderColor.Should().BeEmpty();
        theme.GenLauncherInactiveBorder.Should().BeEmpty();
        theme.GenLauncherInactiveBorder2.Should().BeEmpty();
        theme.GenLauncherActiveColor.Should().BeEmpty();
        theme.GenLauncherDarkFillColor.Should().BeEmpty();
        theme.GenLauncherDarkBackGround.Should().BeEmpty();
        theme.GenLauncherLightBackGround.Should().BeEmpty();
        theme.GenLauncherDefaultTextColor.Should().BeEmpty();
        theme.GenLauncherDownloadTextColor.Should().BeEmpty();
        theme.GenLauncherListBoxSelectionColor1.Should().BeEmpty();
        theme.GenLauncherListBoxSelectionColor2.Should().BeEmpty();
        theme.GenLauncherButtonSelectionColor.Should().BeEmpty();
        theme.GenLauncherBackgroundImageLink.Should().BeEmpty();
    }

    [Fact]
    public void HasValues_WhenNoUsableValueWasPublished_ReturnsFalse()
    {
        LauncherContentTheme empty = new();
        var whitespace = new LauncherContentTheme { GenLauncherActiveColor = "   " };

        empty.HasValues.Should().BeFalse();
        whitespace.HasValues.Should().BeFalse();
    }

    [Fact]
    public void HasValues_WhenPaletteOrArtworkWasPublished_ReturnsTrue()
    {
        var palette = new LauncherContentTheme { GenLauncherActiveColor = "#102030" };
        var artwork = new LauncherContentTheme
        {
            GenLauncherBackgroundImageLink = "https://cdn.example.test/background.png"
        };

        palette.HasValues.Should().BeTrue();
        artwork.HasValues.Should().BeTrue();
    }

    /// <summary>
    ///     Pins the cache names the whole launcher agrees on. Every other caller — the download cache, the tile
    ///     presenter, content removal, and integrity scanning — asks these methods instead of spelling the suffixes
    ///     out, which is what keeps them consistent but also what leaves this the only place a changed suffix can be
    ///     noticed. A test that built its expectation from the same call would move with the change and see nothing.
    /// </summary>
    [Fact]
    public void CacheBaseNames_KeepArtworkAndPaletteApartFromTheTileImage()
    {
        const string Version = "1.2";

        string backgroundBaseName = LauncherContentTheme.ResolveBackgroundImageBaseName(Version);
        string paletteBaseName = LauncherContentTheme.ResolveCacheBaseName(Version);

        backgroundBaseName.Should().Be("1.2-background");
        paletteBaseName.Should().Be("1.2-theme");
        backgroundBaseName.Should().NotBe(Version, "the tile image is cached under the bare version");
        paletteBaseName.Should().NotBe(Version, "the tile image is cached under the bare version");
    }
}
