using System.IO;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.IO;

namespace GenLauncherGO.Infrastructure.Integrity.Support;

/// <summary>
/// Applies integrity-target containment and ignore policy to canonical lexical paths.
/// </summary>
internal static class ContentIntegrityPath
{
    /// <summary>
    /// Gets a normalized relative path after proving that it does not leave the verified target.
    /// </summary>
    public static string GetRelativePath(string root, string path)
    {
        string relativePath = LexicalPath.GetRelativePath(root, path);
        if (LexicalPath.RelativePathLeavesRoot(relativePath))
        {
            throw new InvalidDataException("A scanned entry resolved outside the verified target.");
        }

        return relativePath;
    }

    /// <summary>
    /// Resolves an integrity issue path after proving that it remains in the verified target.
    /// </summary>
    public static string ResolveRelativePath(string root, string relativePath)
    {
        string candidate = LexicalPath.ResolvePath(root, relativePath);
        if (!LexicalPath.IsPathInDirectory(candidate, root))
        {
            throw new InvalidDataException("An integrity issue path resolved outside its target root.");
        }

        return candidate;
    }

    /// <summary>
    /// Determines whether a target-relative path belongs to preserved inactive content.
    /// </summary>
    public static bool IsIgnored(ContentIntegrityTarget target, string relativePath)
    {
        return target.IgnoredRelativePaths.Contains(LexicalPath.NormalizeRelativePath(relativePath));
    }
}
