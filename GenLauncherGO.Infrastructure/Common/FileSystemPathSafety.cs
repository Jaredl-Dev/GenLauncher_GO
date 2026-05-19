using System;
using System.Collections.Generic;
using System.IO;
using GenLauncherGO.Core.IO;

namespace GenLauncherGO.Infrastructure.Common;

/// <summary>
///     Provides shared filesystem path-safety checks for infrastructure services.
/// </summary>
/// <remarks>
///     Every check names the paths it guards with a plural noun phrase — "Deployment journal paths", "Package
///     staging paths" — and builds its rejection message from that phrase. Callers pass the subject rather than
///     finished sentences so one wording change reaches every safety failure in the launcher.
/// </remarks>
internal static class FileSystemPathSafety
{
    /// <summary>
    ///     Resolves a candidate path and verifies that it stays within an owned root without traversing existing links.
    /// </summary>
    /// <param name="ownedRoot">The launcher-owned directory the candidate must remain inside.</param>
    /// <param name="candidatePath">The path to resolve and verify.</param>
    /// <param name="pathSubject">The plural noun phrase naming the guarded paths.</param>
    /// <param name="ownerDescription">
    ///     The container named by the containment failure, such as "the deployment directory".
    /// </param>
    public static string ResolveOwnedSubpath(
        string ownedRoot,
        string candidatePath,
        string pathSubject,
        string ownerDescription)
    {
        string normalizedRoot = LexicalPath.NormalizeFullPath(ownedRoot);
        string normalizedCandidate = LexicalPath.NormalizeFullPath(candidatePath);
        if (!LexicalPath.IsPathInDirectory(normalizedCandidate, normalizedRoot))
        {
            throw new InvalidDataException($"{pathSubject} must stay inside {ownerDescription}.");
        }

        EnsureExistingPathChainHasNoReparsePoints(normalizedRoot, pathSubject);
        EnsureExistingPathChainHasNoReparsePoints(normalizedCandidate, pathSubject);

        return normalizedCandidate;
    }

    /// <summary>
    ///     Rejects paths whose existing filesystem chain contains a reparse point.
    /// </summary>
    /// <param name="path">The path whose existing ancestors are inspected.</param>
    /// <param name="pathSubject">The plural noun phrase naming the guarded paths.</param>
    public static void EnsureExistingPathChainHasNoReparsePoints(string path, string pathSubject)
    {
        if (ExistingPathChainContainsReparsePoint(path, pathSubject))
        {
            throw new InvalidDataException(CreateLinkedPathMessage(pathSubject));
        }
    }

    /// <summary>
    ///     Rejects a directory tree whose root or child entries contain a reparse point.
    /// </summary>
    /// <param name="directoryPath">The root of the tree to inspect.</param>
    /// <param name="pathSubject">The plural noun phrase naming the guarded paths.</param>
    public static void EnsureDirectoryTreeHasNoReparsePoints(string directoryPath, string pathSubject)
    {
        string rootPath = NormalizeAndValidateTreeRoot(directoryPath, pathSubject);
        InspectDirectoryChildren(rootPath, pathSubject, null);
    }

    /// <summary>
    ///     Returns every file below a directory after rejecting reparse points anywhere in the tree.
    /// </summary>
    /// <param name="directoryPath">The root of the tree to enumerate.</param>
    /// <param name="pathSubject">The plural noun phrase naming the guarded paths.</param>
    public static IReadOnlyList<string> GetDirectoryFilesWithNoReparsePoints(
        string directoryPath,
        string pathSubject)
    {
        string rootPath = NormalizeAndValidateTreeRoot(directoryPath, pathSubject);
        var files = new List<string>();
        InspectDirectoryChildren(rootPath, pathSubject, files);
        return files;
    }

    /// <summary>
    ///     Determines whether an existing path chain contains a reparse point.
    /// </summary>
    /// <param name="path">The path whose existing ancestors are inspected.</param>
    /// <param name="pathSubject">The plural noun phrase naming the guarded paths.</param>
    public static bool ExistingPathChainContainsReparsePoint(string path, string pathSubject)
    {
        string fullPath = LexicalPath.NormalizeFullPath(path);
        string root = Path.GetPathRoot(fullPath)
                      ?? throw new InvalidDataException(CreateUnrootedPathMessage(pathSubject));
        string relativePath = LexicalPath.GetRelativePath(root, fullPath);
        if (relativePath == ".")
        {
            return false;
        }

        string currentPath = root;
        string[] segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
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

    /// <summary>
    ///     Determines whether a filesystem entry is a reparse point.
    /// </summary>
    public static bool IsReparsePoint(string path)
    {
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    /// <summary>
    ///     Creates recursive enumeration options that never traverse reparse points.
    /// </summary>
    public static EnumerationOptions CreateRecursiveNoLinksOptions()
    {
        return new EnumerationOptions
        {
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false,
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false
        };
    }

    private static string CreateUnrootedPathMessage(string pathSubject)
    {
        return $"{pathSubject} must be rooted.";
    }

    private static string CreateLinkedPathMessage(string pathSubject)
    {
        return $"{pathSubject} must not contain reparse points.";
    }

    private static string NormalizeAndValidateTreeRoot(string directoryPath, string pathSubject)
    {
        string rootPath = LexicalPath.NormalizeFullPath(directoryPath);
        if (IsReparsePoint(rootPath))
        {
            throw new InvalidDataException(CreateLinkedPathMessage(pathSubject));
        }

        return rootPath;
    }

    internal static bool TryGetAttributes(string path, out FileAttributes attributes)
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

    private static void InspectDirectoryChildren(
        string directoryPath,
        string pathSubject,
        ICollection<string>? files)
    {
        foreach (string entryPath in Directory.EnumerateFileSystemEntries(directoryPath))
        {
            FileAttributes attributes = File.GetAttributes(entryPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(CreateLinkedPathMessage(pathSubject));
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                InspectDirectoryChildren(entryPath, pathSubject, files);
            }
            else
            {
                files?.Add(entryPath);
            }
        }
    }
}
