using Avalonia.Media;
using GenLauncherGO.UI.Shared.Themes;

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
            border,
            "DarkGray",
            "#7A7DB0",
            "#BAFF0C",
            "#232977",
            "#090502",
            "#B3000000",
            "White",
            "#090502",
            "#F21D2057",
            "#F21D2057",
            "#2534FF",
            "White",
            "White",
            "Red",
            "#FF888888",
            "#FF000000",
            "#66000000",
            backgroundImage);
    }
}
