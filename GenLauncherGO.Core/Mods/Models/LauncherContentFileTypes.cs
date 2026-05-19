using System;
using System.Collections.Generic;
using System.IO;

namespace GenLauncherGO.Core.Mods.Models;

/// <summary>
///     Names the file formats the launcher accepts as content.
/// </summary>
/// <remarks>
///     Extraction, the artwork cache, package download naming, and the pickers a user chooses files with all branch
///     on these sets, and they agree only because they read them from here. A format offered by a picker but absent
///     from the extractor hands the user a file the launcher then refuses to unpack, and the mismatch is invisible
///     until someone tries it.
/// </remarks>
public static class LauncherContentFileTypes
{
    /// <summary>
    ///     The extension a game package uses while it is downloaded and deployed.
    /// </summary>
    public const string BigExtension = ".big";

    /// <summary>
    ///     The extension a game package is stored under once installed, keeping it inert for the game.
    /// </summary>
    public const string GibExtension = ".gib";

    /// <summary>
    ///     The extension artwork falls back to when a remote image URL declares none the launcher accepts.
    /// </summary>
    public const string DefaultImageExtension = ".png";

    /// <summary>
    ///     Gets the archive formats the launcher unpacks after a download or a manual import.
    /// </summary>
    public static IReadOnlyList<string> ArchiveExtensions { get; } =
        Array.AsReadOnly<string>([".zip", ".rar", ".7z"]);

    /// <summary>
    ///     Gets the game package files a user can import directly, without extraction.
    /// </summary>
    public static IReadOnlyList<string> GamePackageExtensions { get; } =
        Array.AsReadOnly<string>([BigExtension, GibExtension]);

    /// <summary>
    ///     Gets the image formats accepted for modification artwork.
    /// </summary>
    public static IReadOnlyList<string> ImageExtensions { get; } =
        Array.AsReadOnly<string>([DefaultImageExtension, ".jpg", ".jpeg"]);

    /// <summary>
    ///     Determines whether a path names an archive the launcher unpacks rather than copies.
    /// </summary>
    public static bool IsArchive(string filePath)
    {
        return HasExtension(ArchiveExtensions, Path.GetExtension(filePath));
    }

    /// <summary>
    ///     Determines whether an extension names an artwork format the launcher caches as published.
    /// </summary>
    public static bool IsImage(string extension)
    {
        return HasExtension(ImageExtensions, extension);
    }

    private static bool HasExtension(IReadOnlyList<string> extensions, string extension)
    {
        foreach (string candidate in extensions)
        {
            if (string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
