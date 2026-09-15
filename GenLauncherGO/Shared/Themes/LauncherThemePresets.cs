using System;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GenLauncherGO.Features.Startup;

namespace GenLauncherGO.Shared.Themes;

/// <summary>
///     Provides the authoritative built-in launcher themes.
/// </summary>
internal static class LauncherThemePresets
{
    private static readonly Uri _generalsBackgroundUri =
        new("avares://GenLauncherGO/Shared/Resources/Images/LauncherBackgroundGenerals.png");

    private static readonly Uri _zeroHourBackgroundUri =
        new("avares://GenLauncherGO/Shared/Resources/Images/LauncherBackgroundZeroHour.png");

    /// <summary>
    ///     Creates the built-in theme for a managed game.
    /// </summary>
    public static ColorsInfo Create(SupportedGame managedGame)
    {
        return managedGame switch
        {
            SupportedGame.Generals => CreateGenerals(),
            SupportedGame.ZeroHour => CreateZeroHour(),
            _ => throw PerGame.Unsupported(managedGame, nameof(managedGame))
        };
    }

    private static IImageBrush CreateImageBrush(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        using Stream stream = uri.IsFile
            ? File.Open(uri.LocalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            : AssetLoader.Open(uri);
        return (IImageBrush)new ImageBrush(new Bitmap(stream))
        {
            Stretch = Stretch.Fill
        }.ToImmutable();
    }

    private static ColorsInfo CreateZeroHour()
    {
        return new ColorsInfo(
            borderColor: "#00e3ff",
            inactiveBorderColor: "DarkGray",
            inactiveBorder2: "#7a7db0",
            activeColor: "#baff0c",
            darkFillColor: "#232977",
            darkBackgroundColor: "#090502",
            lightBackgroundColor: "#B3000000",
            defaultTextColor: "White",
            downloadTextColor: "#090502",
            selectionStartColor: "#F21d2057",
            selectionMiddleColor: "#F21d2057",
            buttonSelectionColor: "#2534ff",
            actionTextColor: "White",
            // The Zero Hour shell keeps headings white and carries its accent on the borders.
            headingTextColor: "White",
            errorColor: "Red",
            disabledTextColor: "#FF888888",
            chromeBackgroundColor: "#FF000000",
            scrimColor: "#66000000",
            backgroundImage: CreateImageBrush(_zeroHourBackgroundUri));
    }

    private static ColorsInfo CreateGenerals()
    {
        return new ColorsInfo(
            borderColor: "#ffbb00",
            inactiveBorderColor: "DarkGray",
            inactiveBorder2: "#ffbb00",
            activeColor: "#ffbb00",
            darkFillColor: "#e24c17",
            // The Generals shell panels are flat black rather than the warm near-black upstream shipped.
            darkBackgroundColor: "#000000",
            lightBackgroundColor: "#B3000000",
            defaultTextColor: "White",
            downloadTextColor: "#090502",
            selectionStartColor: "#8a2e0d",
            selectionMiddleColor: "#5a210d",
            buttonSelectionColor: "#e24c17",
            // The Generals shell draws its buttons and toggles in this rust, with gold reserved for headings.
            actionTextColor: "#e24c17",
            headingTextColor: "#ffbb00",
            errorColor: "Red",
            disabledTextColor: "#FF888888",
            chromeBackgroundColor: "#FF000000",
            scrimColor: "#66000000",
            backgroundImage: CreateImageBrush(_generalsBackgroundUri));
    }
}
