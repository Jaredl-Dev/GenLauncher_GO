using System;
using System.IO;

namespace GenLauncherGO.Infrastructure.Common;

/// <summary>
/// Resolves manifest, installed, deployment, and in-progress path variants for package <c>.big</c> files.
/// </summary>
internal static class BigFileVariantPath
{
    /// <summary>
    /// The extension used by remote manifests and game deployment targets.
    /// </summary>
    public const string BigExtension = ".big";

    /// <summary>
    /// The extension used for launcher-managed installed package files.
    /// </summary>
    public const string GibExtension = ".gib";

    /// <summary>
    /// Returns the installed path, converting a <c>.big</c> path to its <c>.gib</c> variant.
    /// </summary>
    public static string GetInstalledPath(string filePath)
    {
        return IsBigFilePath(filePath)
            ? GetGibVariantPath(filePath)
            : filePath;
    }

    /// <summary>
    /// Returns the deployment path, converting an installed <c>.gib</c> path to its game-facing <c>.big</c> variant.
    /// </summary>
    public static string GetDeploymentPath(string filePath)
    {
        return IsGibFilePath(filePath)
            ? Path.ChangeExtension(filePath, BigExtension)
            : filePath;
    }

    /// <summary>
    /// Returns the same-base <c>.gib</c> variant for a requested path.
    /// </summary>
    public static string GetGibVariantPath(string filePath)
    {
        return Path.ChangeExtension(filePath, GibExtension);
    }

    /// <summary>
    /// Returns the existing downloaded path, preferring the requested path and then the converted <c>.gib</c> path.
    /// </summary>
    public static string GetExistingDownloadedPath(string destinationFilePath)
    {
        if (File.Exists(destinationFilePath))
        {
            return destinationFilePath;
        }

        string gibFilePath = GetGibVariantPath(destinationFilePath);
        return File.Exists(gibFilePath) ? gibFilePath : string.Empty;
    }

    /// <summary>
    /// Converts a downloaded <c>.big</c> file to its installed <c>.gib</c> path.
    /// </summary>
    public static void ConvertBigFileToGib(string destinationFilePath)
    {
        if (!IsBigFilePath(destinationFilePath))
        {
            return;
        }

        string gibFilePath = GetGibVariantPath(destinationFilePath);
        if (File.Exists(gibFilePath))
        {
            File.Delete(gibFilePath);
        }

        File.Move(destinationFilePath, gibFilePath);
    }

    /// <summary>
    /// Moves an existing <c>.gib</c> file back to <c>.big</c> so a resumed download can append to it.
    /// </summary>
    public static void PrepareBigFileResumePath(string destinationFilePath)
    {
        if (!IsBigFilePath(destinationFilePath))
        {
            return;
        }

        string gibFilePath = GetGibVariantPath(destinationFilePath);
        if (!File.Exists(gibFilePath) || File.Exists(destinationFilePath))
        {
            return;
        }

        File.Move(gibFilePath, destinationFilePath);
    }

    private static bool IsBigFilePath(string filePath)
    {
        return string.Equals(Path.GetExtension(filePath), BigExtension, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGibFilePath(string filePath)
    {
        return string.Equals(Path.GetExtension(filePath), GibExtension, StringComparison.OrdinalIgnoreCase);
    }
}
