using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenLauncherGO.Shared.IO;

namespace GenLauncherGO.Features.Updating;

/// <summary>
///     Determines when S3 package files should be validated with reliable MD5 hashes.
/// </summary>
internal static class S3HashValidationPolicy
{
    /// <summary>
    ///     Gets the legacy-compatible file kinds whose manifest MD5 is checked during a normal installation.
    /// </summary>
    public static IReadOnlySet<string> InstallHashCheckedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".w3d",
            LauncherContentFileTypes.BigExtension,
            ".bik",
            LauncherContentFileTypes.GibExtension,
            ".dds",
            ".tga",
            ".ini",
            ".scb",
            ".wnd",
            ".csf",
            ".str"
        };

    /// <summary>
    ///     Builds the complete extension set used while repairing an integrity failure already observed on disk.
    /// </summary>
    public static IReadOnlySet<string> CreateRepairHashCheckedExtensions(
        IEnumerable<RemoteFileManifestEntry> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var extensions = files
            .Select(file => Path.GetExtension(file.FileName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        extensions.Add(LauncherContentFileTypes.GibExtension);
        return extensions;
    }

    /// <summary>
    ///     Returns whether a manifest entry should be validated with MD5.
    /// </summary>
    public static bool ShouldCheckHash(
        RemoteFileManifestEntry file,
        IReadOnlySet<string> hashCheckedExtensions)
    {
        return hashCheckedExtensions.Contains(Path.GetExtension(file.FileName)) &&
               IsReliableMd5Hash(file.Hash);
    }

    /// <summary>
    ///     Returns whether a manifest hash is a plain 32-character hexadecimal MD5 value.
    /// </summary>
    public static bool IsReliableMd5Hash(string hash)
    {
        if (hash.Length != 32)
        {
            return false;
        }

        foreach (char character in hash)
        {
            bool isHexDigit = character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!isHexDigit)
            {
                return false;
            }
        }

        return true;
    }
}
