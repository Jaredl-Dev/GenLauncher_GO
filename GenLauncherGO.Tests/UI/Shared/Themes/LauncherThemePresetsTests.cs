using Avalonia.Media;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.Tests.UI.Shared.Themes;

[Collection("Avalonia")]
public sealed class LauncherThemePresetsTests
{
    /// <summary>
    ///     The two shells are not the same shell in different artwork: each game carries its accent on a different
    ///     part of the frame, which is why the palette keeps separate heading and action slots at all.
    /// </summary>
    [Fact]
    public void Create_GivesEachGameTheAccentsItsOwnShellIsDrawnWith()
    {
        StaTestRunner.Run(() =>
        {
            ColorsInfo generals = LauncherThemePresets.Create(SupportedGame.Generals);
            ColorsInfo zeroHour = LauncherThemePresets.Create(SupportedGame.ZeroHour);

            // Generals draws headings in its gold accent; Zero Hour leaves them white and accents the borders.
            generals.GenLauncherHeadingTextColor.Color.Should().Be(Color.Parse("#FFBB00"));
            zeroHour.GenLauncherHeadingTextColor.Color.Should().Be(Colors.White);

            // Generals draws its buttons and toggles in rust, with the gold reserved for headings.
            generals.GenLauncherActionTextColor.Color.Should().Be(Color.Parse("#E24C17"));
            zeroHour.GenLauncherActionTextColor.Color.Should().Be(Colors.White);

            generals.GenLauncherBorderColor.Color.Should().NotBe(zeroHour.GenLauncherBorderColor.Color);
        });
    }
}
