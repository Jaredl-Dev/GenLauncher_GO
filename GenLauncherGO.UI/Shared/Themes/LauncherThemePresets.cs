using System;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.UI.Shared.Themes;

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
            Stretch = Stretch.Fill
        }.ToImmutable();
    }

    private static ColorsInfo CreateZeroHour()
    {
        return new ColorsInfo(
            "#00e3ff",
            "DarkGray",
            "#7a7db0",
            "#baff0c",
            "#232977",
            "#090502",
            "#B3000000",
            "White",
            "#090502",
            "#F21d2057",
            "#F21d2057",
            "#2534ff",
            "White",
            // The Zero Hour shell keeps headings white and carries its accent on the borders.
            "White",
            "Red",
            "#FF888888",
            "#FF000000",
            "#66000000",
            CreateImageBrush(_zeroHourBackgroundUri));
    }

    private static ColorsInfo CreateGenerals()
    {
        return new ColorsInfo(
            "#ffbb00",
            "DarkGray",
            "#ffbb00",
            "#ffbb00",
            "#e24c17",
            // The Generals shell panels are flat black rather than the warm near-black upstream shipped.
            "#000000",
            "#B3000000",
            "White",
            "#090502",
            "#8a2e0d",
            "#5a210d",
            "#e24c17",
            // The Generals shell draws its buttons and toggles in this rust, with gold reserved for headings.
            "#e24c17",
            "#ffbb00",
            "Red",
            "#FF888888",
            "#FF000000",
            "#66000000",
            CreateImageBrush(_generalsBackgroundUri));
    }
}
