using Avalonia.Media;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.Tests.Testing;

internal static class TestLauncherTheme
{
    public static ColorsInfo Create(IImageBrush? backgroundImage = null)
    {
        return new ColorsInfo(
            border: "#00E3FF",
            inactiveBorder: "DarkGray",
            inactiveBorder2: "#7A7DB0",
            activeColor: "#BAFF0C",
            darkFill: "#232977",
            darkBackground: "#090502",
            lightBackground: "#B3000000",
            text: "White",
            text2: "Black",
            selectionStartColor: "#F21D2057",
            selectionMiddleColor: "#E61D2057",
            buttonSelectionColor: "#2534FF",
            buttonPointerOverColor: "#141D8C",
            accentFillColor: "#173352",
            subtleAccentFillColor: "#070B0F",
            accentBorderColor: "#49A1FF",
            subtleAccentBorderColor: "#36495E",
            accentTextColor: "#C7E2FF",
            backgroundImage: backgroundImage);
    }
}
