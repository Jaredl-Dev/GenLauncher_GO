using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Infrastructure.Common;

/// <summary>
/// Mutates explicitly launcher-owned directory trees without traversing reparse points.
/// </summary>
internal static class OwnedDirectoryTree
{
    /// <summary>
    /// Creates an owned directory when it does not exist and rejects an existing linked entry.
    /// </summary>
    public static string EnsureExists(string ownedRoot, string directoryPath)
    {
        string normalizedPath = ResolveOwnedDirectoryPath(ownedRoot, directoryPath);
        EnsureSafeAncestors(ownedRoot, normalizedPath);
        if (TryGetAttributes(normalizedPath, out FileAttributes attributes))
        {
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Owned directories must not be reparse points.");
            }

            if ((attributes & FileAttributes.Directory) == 0)
            {
                throw new IOException("An owned directory path is occupied by a file.");
            }

            return normalizedPath;
        }

        Directory.CreateDirectory(normalizedPath);
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            normalizedPath,
            "Owned directory paths must be rooted.",
            "Owned directory paths must not cross reparse points.");
        return normalizedPath;
    }

    /// <summary>
    /// Ensures an owned path is a real directory, replacing a linked leaf with an empty real directory.
    /// </summary>
    public static string EnsureRealDirectory(string ownedRoot, string directoryPath)
    {
        string normalizedPath = ResolveOwnedDirectoryPath(ownedRoot, directoryPath);
        EnsureSafeAncestors(ownedRoot, normalizedPath);

        if (!TryGetAttributes(normalizedPath, out FileAttributes attributes))
        {
            Directory.CreateDirectory(normalizedPath);
            return normalizedPath;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            DeleteEntryWithoutFollowing(normalizedPath, attributes);
            Directory.CreateDirectory(normalizedPath);
            return normalizedPath;
        }

        if ((attributes & FileAttributes.Directory) == 0)
        {
            throw new IOException("An owned directory path is occupied by a file.");
        }

        return normalizedPath;
    }

    /// <summary>
    /// Deletes an owned directory tree when it exists.
    /// </summary>
    /// <remarks>
    /// Nested links are deleted as entries without traversing them, so their targets remain untouched.
    /// </remarks>
    public static bool DeleteIfExists(OwnedContentPath ownedPath)
    {
        ArgumentNullException.ThrowIfNull(ownedPath);

        return DeleteIfExists(ownedPath.OwnerRoot, ownedPath.FullPath);
    }

    /// <summary>
    /// Deletes a directory tree below an explicit owned root when it exists.
    /// </summary>
    /// <remarks>
    /// Nested links are deleted as entries without traversing them, so their targets remain untouched.
    /// </remarks>
    public static bool DeleteIfExists(string ownedRoot, string directoryPath)
    {
        string normalizedPath = ResolveOwnedDirectoryPath(ownedRoot, directoryPath);
        EnsureSafeAncestors(ownedRoot, normalizedPath);
        if (!TryGetAttributes(normalizedPath, out FileAttributes attributes))
        {
            return false;
        }

        DeleteEntryWithoutFollowing(normalizedPath, attributes);
        return true;
    }

    /// <summary>
    /// Creates an owned directory when necessary and deletes all of its existing child entries.
    /// </summary>
    /// <remarks>
    /// When the directory itself is a link, the link is deleted and replaced by a real directory. Nested links are
    /// deleted as entries without traversing them.
    /// </remarks>
    public static string PrepareEmpty(string ownedRoot, string directoryPath)
    {
        string normalizedPath = ResolveOwnedDirectoryPath(ownedRoot, directoryPath);
        EnsureSafeAncestors(ownedRoot, normalizedPath);

        if (TryGetAttributes(normalizedPath, out FileAttributes attributes))
        {
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                DeleteEntryWithoutFollowing(normalizedPath, attributes);
                Directory.CreateDirectory(normalizedPath);
                return normalizedPath;
            }

            if ((attributes & FileAttributes.Directory) == 0)
            {
                throw new IOException("An owned directory path is occupied by a file.");
            }

            DeleteDirectoryChildren(normalizedPath);
            return normalizedPath;
        }

        Directory.CreateDirectory(normalizedPath);
        return normalizedPath;
    }

    /// <summary>
    /// Deletes empty parent directories between a child path and its exclusive ownership boundary.
    /// </summary>
    public static IReadOnlyList<string> DeleteEmptyParents(string ownedRoot, string childPath)
    {
        string normalizedRoot = LexicalPath.NormalizeFullPath(ownedRoot);
        string normalizedChild = LexicalPath.NormalizeFullPath(childPath);
        if (!LexicalPath.IsPathInDirectory(normalizedChild, normalizedRoot))
        {
            throw new InvalidOperationException("Refusing to prune directories outside the owned root.");
        }

        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            normalizedRoot,
            "Owned directory roots must be rooted.",
            "Owned directory roots must not contain reparse points.");

        var deletedDirectories = new List<string>();
        DirectoryInfo? current = Directory.GetParent(normalizedChild);
        while (current is not null &&
               !string.Equals(current.FullName, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            string currentPath = current.FullName;
            DirectoryInfo? parent = current.Parent;
            if (!LexicalPath.IsPathInDirectory(currentPath, normalizedRoot))
            {
                break;
            }

            if (!TryGetAttributes(currentPath, out FileAttributes attributes))
            {
                current = parent;
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Owned directory parents must not contain reparse points.");
            }

            if ((attributes & FileAttributes.Directory) == 0 ||
                Directory.EnumerateFileSystemEntries(currentPath).Any())
            {
                break;
            }

            Directory.Delete(currentPath, recursive: false);
            deletedDirectories.Add(currentPath);
            current = parent;
        }

        return deletedDirectories;
    }

    /// <summary>
    /// Recursively deletes empty real directories below an owned content path without traversing or deleting links.
    /// </summary>
    public static bool DeleteEmptyDirectories(OwnedContentPath ownedPath)
    {
        ArgumentNullException.ThrowIfNull(ownedPath);

        EnsureSafeAncestors(ownedPath.OwnerRoot, ownedPath.FullPath);
        return DeleteEmptyDirectoriesCore(ownedPath.FullPath);
    }

    /// <summary>
    /// Deletes all reparse-point entries below an owned real directory without traversing their targets.
    /// </summary>
    public static IReadOnlyList<string> DeleteReparsePoints(OwnedContentPath ownedPath)
    {
        ArgumentNullException.ThrowIfNull(ownedPath);

        EnsureSafeAncestors(ownedPath.OwnerRoot, ownedPath.FullPath);
        var deletedPaths = new List<string>();
        DeleteReparsePointsCore(ownedPath.FullPath, deletedPaths);
        return deletedPaths;
    }

    private static string ResolveOwnedDirectoryPath(string ownedRoot, string directoryPath)
    {
        return new OwnedContentPath(ownedRoot, directoryPath).FullPath;
    }

    private static void EnsureSafeAncestors(string ownedRoot, string directoryPath)
    {
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            ownedRoot,
            "Owned directory roots must be rooted.",
            "Owned directory roots must not contain reparse points.");

        string? parentDirectory = Path.GetDirectoryName(directoryPath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
                parentDirectory,
                "Owned directory paths must be rooted.",
                "Owned directory paths must not cross reparse points.");
        }
    }

    private static void DeleteDirectoryChildren(string directoryPath)
    {
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            directoryPath,
            "Owned directory paths must be rooted.",
            "Owned directory paths must not cross reparse points.");

        foreach (string entryPath in Directory.EnumerateFileSystemEntries(directoryPath).ToList())
        {
            if (!TryGetAttributes(entryPath, out FileAttributes attributes))
            {
                continue;
            }

            DeleteEntryWithoutFollowing(entryPath, attributes);
        }
    }

    private static bool DeleteEmptyDirectoriesCore(string directoryPath)
    {
        if (!TryGetAttributes(directoryPath, out FileAttributes attributes) ||
            (attributes & FileAttributes.ReparsePoint) != 0 ||
            (attributes & FileAttributes.Directory) == 0)
        {
            return false;
        }

        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            directoryPath,
            "Owned directory paths must be rooted.",
            "Owned directory paths must not cross reparse points.");

        foreach (string entryPath in Directory.EnumerateDirectories(directoryPath).ToList())
        {
            if (!TryGetAttributes(entryPath, out FileAttributes childAttributes) ||
                (childAttributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            DeleteEmptyDirectoriesCore(entryPath);
        }

        if (Directory.EnumerateFileSystemEntries(directoryPath).Any())
        {
            return false;
        }

        Directory.Delete(directoryPath, recursive: false);
        return true;
    }

    private static void DeleteReparsePointsCore(string directoryPath, List<string> deletedPaths)
    {
        if (!TryGetAttributes(directoryPath, out FileAttributes directoryAttributes) ||
            (directoryAttributes & FileAttributes.ReparsePoint) != 0 ||
            (directoryAttributes & FileAttributes.Directory) == 0)
        {
            throw new InvalidDataException("Owned directory traversal requires a real directory.");
        }

        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            directoryPath,
            "Owned directory paths must be rooted.",
            "Owned directory paths must not cross reparse points.");

        foreach (string entryPath in Directory.EnumerateFileSystemEntries(directoryPath).ToList())
        {
            if (!TryGetAttributes(entryPath, out FileAttributes attributes))
            {
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                DeleteLinkEntry(entryPath, attributes);
                deletedPaths.Add(entryPath);
                continue;
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                DeleteReparsePointsCore(entryPath, deletedPaths);
            }
        }
    }

    private static void DeleteEntryWithoutFollowing(string entryPath, FileAttributes observedAttributes)
    {
        if ((observedAttributes & FileAttributes.ReparsePoint) != 0)
        {
            DeleteLinkEntry(entryPath, observedAttributes);
            return;
        }

        if ((observedAttributes & FileAttributes.Directory) != 0)
        {
            if (!TryGetAttributes(entryPath, out FileAttributes currentAttributes))
            {
                return;
            }

            if ((currentAttributes & FileAttributes.ReparsePoint) != 0)
            {
                DeleteLinkEntry(entryPath, currentAttributes);
                return;
            }

            DeleteDirectoryChildren(entryPath);
            Directory.Delete(entryPath, recursive: false);
            return;
        }

        File.Delete(entryPath);
    }

    private static void DeleteLinkEntry(string entryPath, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.Directory) != 0)
        {
            Directory.Delete(entryPath, recursive: false);
        }
        else
        {
            File.Delete(entryPath);
        }
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
}
