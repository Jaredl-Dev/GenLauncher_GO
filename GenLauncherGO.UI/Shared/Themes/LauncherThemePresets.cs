using System;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.UI.Shared.Themes;

/// <summary>
/// Provides the authoritative built-in launcher themes.
/// </summary>
internal static class LauncherThemePresets
{
    private static readonly Uri _generalsBackgroundUri =
        new("avares://GenLauncherGO/Shared/Resources/Images/LauncherBackgroundGenerals.png");

    private static readonly Uri _zeroHourBackgroundUri =
        new("avares://GenLauncherGO/Shared/Resources/Images/LauncherBackgroundZeroHour.png");

    /// <summary>
    /// Creates the built-in theme for a managed game.
    /// </summary>
    public static ColorsInfo Create(SupportedGame managedGame)
    {
        return managedGame == SupportedGame.ZeroHour
            ? CreateZeroHour()
            : CreateGenerals();
    }

    private static IImageBrush CreateImageBrush(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        using Stream stream = uri.IsFile
            ? File.Open(uri.LocalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            : AssetLoader.Open(uri);
        return (IImageBrush)new ImageBrush(new Bitmap(stream))
        {
            Stretch = Stretch.Fill,
        }.ToImmutable();
    }

    private static ColorsInfo CreateZeroHour()
    {
        return new ColorsInfo(
            border: "#00e3ff",
            inactiveBorder: "DarkGray",
            inactiveBorder2: "#7a7db0",
            activeColor: "#baff0c",
            darkFill: "#232977",
            darkBackground: "#090502",
            lightBackground: "#B3000000",
            text: "White",
            text2: "#090502",
            selectionStartColor: "#F21d2057",
            selectionMiddleColor: "#F21d2057",
            buttonSelectionColor: "#2534ff",
            buttonPointerOverColor: "#141d8c",
            accentFillColor: "#173352",
            subtleAccentFillColor: "#070b0f",
            accentBorderColor: "#49a1ff",
            subtleAccentBorderColor: "#36495e",
            accentTextColor: "#c7e2ff",
            backgroundImage: CreateImageBrush(_zeroHourBackgroundUri));
    }

    private static ColorsInfo CreateGenerals()
    {
        return new ColorsInfo(
            border: "#ffbb00",
            inactiveBorder: "DarkGray",
            inactiveBorder2: "#ffbb00",
            activeColor: "#ffbb00",
            darkFill: "#e24c17",
            darkBackground: "#090502",
            lightBackground: "#B3000000",
            text: "White",
            text2: "#090502",
            selectionStartColor: "#8a2e0d",
            selectionMiddleColor: "#5a210d",
            buttonSelectionColor: "#e24c17",
            buttonPointerOverColor: "#7c2a0d",
            accentFillColor: "#523417",
            subtleAccentFillColor: "#0f0b07",
            accentBorderColor: "#ffa349",
            subtleAccentBorderColor: "#5e4a36",
            accentTextColor: "#ffe3c7",
            backgroundImage: CreateImageBrush(_generalsBackgroundUri));
    }
}
