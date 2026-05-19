using System;
using System.IO;
using GenLauncherGO.Core.IO;

namespace GenLauncherGO.Infrastructure.Common;

/// <summary>
/// Provides shared filesystem path-safety checks for infrastructure services.
/// </summary>
internal static class FileSystemPathSafety
{
    /// <summary>
    /// Resolves a candidate path and verifies that it stays within an owned root without traversing existing links.
    /// </summary>
    public static string ResolveOwnedSubpath(
        string ownedRoot,
        string candidatePath,
        string outsideRootMessage,
        string linkedPathMessage)
    {
        string normalizedRoot = LexicalPath.NormalizeFullPath(ownedRoot);
        string normalizedCandidate = LexicalPath.NormalizeFullPath(candidatePath);
        if (!LexicalPath.IsPathInDirectory(normalizedCandidate, normalizedRoot))
        {
            throw new InvalidDataException(outsideRootMessage);
        }

        EnsureExistingPathChainHasNoReparsePoints(
            normalizedRoot,
            "An owned root path must be rooted.",
            linkedPathMessage);
        EnsureExistingPathChainHasNoReparsePoints(
            normalizedCandidate,
            "An owned candidate path must be rooted.",
            linkedPathMessage);

        return normalizedCandidate;
    }

    /// <summary>
    /// Rejects paths whose existing filesystem chain contains a reparse point.
    /// </summary>
    public static void EnsureExistingPathChainHasNoReparsePoints(
        string path,
        string unrootedPathMessage,
        string linkedPathMessage)
    {
        if (ExistingPathChainContainsReparsePoint(path, unrootedPathMessage))
        {
            throw new InvalidDataException(linkedPathMessage);
        }
    }

    /// <summary>
    /// Rejects a directory tree whose root or child entries contain a reparse point.
    /// </summary>
    public static void EnsureDirectoryTreeHasNoReparsePoints(
        string directoryPath,
        string linkedPathMessage)
    {
        string rootPath = LexicalPath.NormalizeFullPath(directoryPath);
        if (IsReparsePoint(rootPath))
        {
            throw new InvalidDataException(linkedPathMessage);
        }

        EnsureDirectoryChildrenHaveNoReparsePoints(rootPath, linkedPathMessage);
    }

    /// <summary>
    /// Determines whether an existing path chain contains a reparse point.
    /// </summary>
    public static bool ExistingPathChainContainsReparsePoint(string path, string unrootedPathMessage)
    {
        string fullPath = LexicalPath.NormalizeFullPath(path);
        string root = Path.GetPathRoot(fullPath)
                      ?? throw new InvalidDataException(unrootedPathMessage);
        string relativePath = LexicalPath.GetRelativePath(root, fullPath);
        if (relativePath == ".")
        {
            return false;
        }

        string currentPath = root;
        string[] segments = relativePath.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        foreach (string segment in segments)
        {
            currentPath = Path.Combine(currentPath, segment);
            if (!TryGetAttributes(currentPath, out FileAttributes attributes))
            {
                return false;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }

    /// <summary>
    /// Determines whether a filesystem entry is a reparse point.
    /// </summary>
    public static bool IsReparsePoint(string path)
    {
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    /// <summary>
    /// Creates recursive enumeration options that never traverse reparse points.
    /// </summary>
    public static EnumerationOptions CreateRecursiveNoLinksOptions()
    {
        return new EnumerationOptions
        {
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false,
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false,
        };
    }

    private static void EnsureDirectoryChildrenHaveNoReparsePoints(
        string directoryPath,
        string linkedPathMessage)
    {
        foreach (string entryPath in Directory.EnumerateFileSystemEntries(directoryPath))
        {
            FileAttributes attributes = File.GetAttributes(entryPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(linkedPathMessage);
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                EnsureDirectoryChildrenHaveNoReparsePoints(entryPath, linkedPathMessage);
            }
        }
    }
}
