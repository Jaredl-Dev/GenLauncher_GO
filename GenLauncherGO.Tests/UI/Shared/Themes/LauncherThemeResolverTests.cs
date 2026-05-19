using Avalonia.Media;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.Tests.UI.Shared.Themes;

[Collection("Avalonia")]
public sealed class LauncherThemeResolverTests
{
    [Fact]
    public void ModificationWithoutATheme_KeepsTheActiveGamePalette()
    {
        StaTestRunner.Run(() =>
        {
            ColorsInfo preset = LauncherThemePresets.Create(SupportedGame.Generals);

            ColorsInfo resolved = LauncherThemeResolver.Resolve(null, SupportedGame.Generals);

            resolved.GenLauncherBorderColor.Color.Should().Be(preset.GenLauncherBorderColor.Color);
            resolved.GenLauncherDarkBackGround.Color.Should().Be(preset.GenLauncherDarkBackGround.Color);
        });
    }

    /// <summary>
    ///     Upstream rejected a palette that did not fill every slot. A modification that names one colour has to get
    ///     that colour and keep a coherent shell around it. Distinct values per slot also pin the mapping: upstream
    ///     swapped the two list-selection colours on the manifest path, so a mod's start colour rendered at the far
    ///     stop.
    /// </summary>
    [Fact]
    public void PartialThemeTakesDeclaredSlotsAnd_FallsBackForTheRest()
    {
        StaTestRunner.Run(() =>
        {
            ColorsInfo preset = LauncherThemePresets.Create(SupportedGame.ZeroHour);
            LauncherContentTheme theme = new()
            {
                GenLauncherBorderColor = "#FF00FF",
                GenLauncherListBoxSelectionColor1 = "#010101",
                GenLauncherListBoxSelectionColor2 = "#020202",
                GenLauncherInactiveBorder = "#030303",
                GenLauncherDarkBackGround = "#040404"
            };

            ColorsInfo resolved = LauncherThemeResolver.Resolve(theme, SupportedGame.ZeroHour);

            resolved.GenLauncherBorderColor.Color.Should().Be(Color.Parse("#FF00FF"));
            resolved.ListSelectionStartColor.Should().Be(Color.Parse("#010101"));
            resolved.ListSelectionMiddleColor.Should().Be(Color.Parse("#020202"));
            resolved.GenLauncherInactiveBorder.Color.Should().Be(Color.Parse("#030303"));
            resolved.GenLauncherDarkBackGround.Color.Should().Be(Color.Parse("#040404"));
            resolved.GenLauncherDarkFillColor.Color.Should().Be(preset.GenLauncherDarkFillColor.Color);
            resolved.GenLauncherDefaultTextColor.Color.Should().Be(preset.GenLauncherDefaultTextColor.Color);
            resolved.GenLauncherLightBackGround.Color.Should().Be(preset.GenLauncherLightBackGround.Color);
        });
    }

    /// <summary>
    ///     The published palette is untrusted input, so a value the renderer cannot parse degrades to the built-in
    ///     colour rather than taking down the launcher.
    /// </summary>
    [Theory]
    [InlineData("not-a-color")]
    [InlineData("#GGGGGG")]
    [InlineData("")]
    public void UnrenderableColour_FallsBackInsteadOfThrowing(string published)
    {
        StaTestRunner.Run(() =>
        {
            ColorsInfo preset = LauncherThemePresets.Create(SupportedGame.Generals);
            LauncherContentTheme theme = new() { GenLauncherBorderColor = published };

            ColorsInfo resolved = LauncherThemeResolver.Resolve(theme, SupportedGame.Generals);

            resolved.GenLauncherBorderColor.Color.Should().Be(preset.GenLauncherBorderColor.Color);
        });
    }

    /// <summary>
    ///     A published modification cannot repaint the launcher's own signals: the error colour marks content the
    ///     user cannot launch, and the scrim is what keeps a modal readable over the shell behind it.
    /// </summary>
    [Fact]
    public void Theme_DeclaringEveryRemoteSlot_KeepsTheErrorAndScrimColours()
    {
        StaTestRunner.Run(() =>
        {
            ColorsInfo preset = LauncherThemePresets.Create(SupportedGame.ZeroHour);
            LauncherContentTheme theme = new()
            {
                GenLauncherBorderColor = "#010101",
                GenLauncherInactiveBorder = "#020202",
                GenLauncherInactiveBorder2 = "#030303",
                GenLauncherActiveColor = "#040404",
                GenLauncherDarkFillColor = "#050505",
                GenLauncherDarkBackGround = "#060606",
                GenLauncherLightBackGround = "#070707",
                GenLauncherDefaultTextColor = "#080808",
                GenLauncherDownloadTextColor = "#090909",
                GenLauncherListBoxSelectionColor1 = "#0A0A0A",
                GenLauncherListBoxSelectionColor2 = "#0B0B0B",
                GenLauncherButtonSelectionColor = "#0C0C0C"
            };

            ColorsInfo resolved = LauncherThemeResolver.Resolve(theme, SupportedGame.ZeroHour);

            resolved.GenLauncherErrorColor.Color.Should().Be(preset.GenLauncherErrorColor.Color);
            resolved.GenLauncherScrimColor.Color.Should().Be(preset.GenLauncherScrimColor.Color);
        });
    }

    /// <summary>
    ///     Upstream only applied a modification's colours when its background artwork was already on disk, so a mod
    ///     with colours and no artwork silently inherited whatever was selected before it.
    /// </summary>
    [Fact]
    public void ColoursApply_MissingBackgroundArtwork_KeepsGameArtwork()
    {
        StaTestRunner.Run(() =>
        {
            LauncherContentTheme theme = new() { GenLauncherActiveColor = "#123456" };

            ColorsInfo resolved = LauncherThemeResolver.Resolve(theme, SupportedGame.ZeroHour);

            resolved.GenLauncherActiveColor.Color.Should().Be(Color.Parse("#123456"));
            resolved.GenLauncherBackgroundImage.Should().NotBeNull(
                "a theme without artwork keeps the game's own shell artwork rather than an empty background");
        });
    }

    [Fact]
    public void Resolve_WithCachedBackgroundArtwork_WearsThatArtwork()
    {
        StaTestRunner.Run(() =>
        {
            IImageBrush cachedArtwork =
                LauncherThemePresets.Create(SupportedGame.Generals).GenLauncherBackgroundImage!;
            LauncherContentTheme theme = new() { GenLauncherActiveColor = "#123456" };

            ColorsInfo resolved = LauncherThemeResolver.Resolve(
                theme,
                SupportedGame.ZeroHour,
                cachedArtwork);

            resolved.GenLauncherActiveColor.Color.Should().Be(Color.Parse("#123456"));
            resolved.GenLauncherBackgroundImage.Should().BeSameAs(cachedArtwork);
        });
    }

    /// <summary>
    ///     The remote contract predates the launcher's separate heading and action colours, so a themed shell has to
    ///     derive them from what the modification did publish instead of keeping the previous game's accents.
    /// </summary>
    [Fact]
    public void Theme_DrivesTheSlotsTheRemoteContractDoesNotName()
    {
        StaTestRunner.Run(() =>
        {
            LauncherContentTheme theme = new()
            {
                GenLauncherActiveColor = "#111111",
                GenLauncherDefaultTextColor = "#222222"
            };

            ColorsInfo resolved = LauncherThemeResolver.Resolve(theme, SupportedGame.Generals);

            resolved.GenLauncherHeadingTextColor.Color.Should().Be(Color.Parse("#111111"));
            resolved.GenLauncherActionTextColor.Color.Should().Be(Color.Parse("#222222"));
        });
    }
}
