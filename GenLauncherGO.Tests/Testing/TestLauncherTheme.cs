using Avalonia.Media;
using GenLauncherGO.Shared.Themes;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Builds a launcher theme for tests from the shipped Zero Hour palette.
/// </summary>
/// <remarks>
///     Values are taken from the real preset rather than restated, so a test can never assert against a palette the
///     product does not actually produce. Override a slot only when a test needs to distinguish that one colour.
/// </remarks>
internal static class TestLauncherTheme
{
    public static ColorsInfo Create(IImageBrush? backgroundImage = null, string border = "#00E3FF")
    {
        return new ColorsInfo(
            borderColor: border,
            inactiveBorderColor: "DarkGray",
            inactiveBorder2: "#7A7DB0",
            activeColor: "#BAFF0C",
            darkFillColor: "#232977",
            darkBackgroundColor: "#090502",
            lightBackgroundColor: "#B3000000",
            defaultTextColor: "White",
            downloadTextColor: "#090502",
            selectionStartColor: "#F21D2057",
            selectionMiddleColor: "#F21D2057",
            buttonSelectionColor: "#2534FF",
            actionTextColor: "White",
            headingTextColor: "White",
            errorColor: "Red",
            disabledTextColor: "#FF888888",
            chromeBackgroundColor: "#FF000000",
            scrimColor: "#66000000",
            backgroundImage: backgroundImage);
    }
}
