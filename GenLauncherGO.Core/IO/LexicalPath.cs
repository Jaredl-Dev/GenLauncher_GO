using System;
using System.IO;

namespace GenLauncherGO.Core.IO;

/// <summary>
///     Provides side-effect-free Windows path normalization, relative-path, and containment operations.
/// </summary>
/// <remarks>
///     These operations are lexical only. Callers that traverse or mutate the filesystem must separately inspect the
///     physical path for reparse points and other unsafe entries.
/// </remarks>
public static class LexicalPath
{
    /// <summary>
    ///     Returns a fully qualified path without a non-root trailing directory separator.
    /// </summary>
    public static string NormalizeFullPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    /// <summary>
    ///     Determines whether two paths identify the same lexical location using Windows case semantics.
    /// </summary>
    /// <remarks>
    ///     Missing or malformed paths are not equivalent. Valid paths are fully normalized first, so differences in
    ///     case, trailing separators, and dot segments do not affect the comparison.
    /// </remarks>
    public static bool AreEquivalent(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return AreNormalizedPathsEquivalent(NormalizeFullPath(left), NormalizeFullPath(right));
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Normalizes a relative path to slash separators for persisted metadata and comparisons.
    /// </summary>
    public static string NormalizeRelativePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.Replace('\\', '/').Trim('/');
    }

    /// <summary>
    ///     Gets a normalized slash-separated path from a root to another path.
    /// </summary>
    /// <remarks>
    ///     The returned path can identify the root itself or leave it. Call <see cref="RelativePathLeavesRoot" /> or a
    ///     containment operation when a caller requires an ownership boundary.
    /// </remarks>
    public static string GetRelativePath(string root, string path)
    {
        return NormalizeRelativePath(Path.GetRelativePath(
            NormalizeFullPath(root),
            NormalizeFullPath(path)));
    }

    /// <summary>
    ///     Resolves a path against a root without inspecting the physical filesystem.
    /// </summary>
    /// <remarks>
    ///     Rooted or traversing inputs can resolve outside <paramref name="root" />. Use
    ///     <see cref="ResolveContainedPath" /> when containment is required.
    /// </remarks>
    public static string ResolvePath(string root, string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return NormalizeFullPath(Path.Combine(
            NormalizeFullPath(root),
            path.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>
    ///     Resolves a path against a root and proves the result stays inside it.
    /// </summary>
    /// <param name="root">The directory the resolved path must remain within.</param>
    /// <param name="path">The relative or absolute path to resolve.</param>
    /// <param name="containmentFailureMessage">The message describing what escaped which boundary.</param>
    /// <remarks>
    ///     This is the single place that pairs resolution with its containment proof. Resolving without the proof
    ///     is what lets a traversing or rooted input escape an owned directory, so callers that need the boundary
    ///     enforced should reach for this rather than combining <see cref="ResolvePath" /> and
    ///     <see cref="IsPathInDirectory" /> themselves.
    /// </remarks>
    /// <exception cref="InvalidDataException">Thrown when the resolved path leaves <paramref name="root" />.</exception>
    public static string ResolveContainedPath(string root, string path, string containmentFailureMessage)
    {
        string normalizedRoot = NormalizeFullPath(root);
        string candidatePath = ResolvePath(normalizedRoot, path);
        if (!IsPathInDirectory(candidatePath, normalizedRoot))
        {
            throw new InvalidDataException(containmentFailureMessage);
        }

        return candidatePath;
    }

    /// <summary>
    ///     Determines whether a path is a directory or one of its children using Windows case semantics.
    /// </summary>
    public static bool IsPathInDirectory(string path, string directory)
    {
        string normalizedDirectory = NormalizeFullPath(directory);
        string normalizedPath = NormalizeFullPath(path);
        return IsNormalizedPathInDirectory(normalizedPath, normalizedDirectory);
    }

    /// <summary>
    ///     Determines whether a path is strictly below a directory using Windows case semantics.
    /// </summary>
    internal static bool IsPathBelowDirectory(string path, string directory)
    {
        string normalizedDirectory = NormalizeFullPath(directory);
        string normalizedPath = NormalizeFullPath(path);
        return !AreNormalizedPathsEquivalent(normalizedPath, normalizedDirectory) &&
               IsNormalizedPathInDirectory(normalizedPath, normalizedDirectory);
    }

    /// <summary>
    ///     Determines whether a relative path identifies an entry outside its origin.
    /// </summary>
    public static bool RelativePathLeavesRoot(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        string normalizedPath = NormalizeRelativePath(relativePath);
        return string.Equals(normalizedPath, "..", StringComparison.Ordinal) ||
               normalizedPath.StartsWith("../", StringComparison.Ordinal) ||
               Path.IsPathRooted(relativePath);
    }

    /// <summary>
    ///     Normalizes one user-supplied Windows path segment and rejects reserved or traversing names.
    /// </summary>
    internal static string NormalizePathSegment(string? segment, string paramName)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            throw new ArgumentException("Path segments must not be empty.", paramName);
        }

        string normalizedSegment = segment.Trim();
        if (Path.IsPathRooted(normalizedSegment) ||
            normalizedSegment.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            normalizedSegment.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal) ||
            normalizedSegment.Contains(':', StringComparison.Ordinal) ||
            normalizedSegment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Path segments must not contain rooted paths or directory separators.",
                paramName);
        }

        if (string.Equals(normalizedSegment, ".", StringComparison.Ordinal) ||
            string.Equals(normalizedSegment, "..", StringComparison.Ordinal) ||
            normalizedSegment.EndsWith('.') ||
            IsReservedDeviceName(normalizedSegment))
        {
            throw new ArgumentException("Path segments must not use reserved file-system names.", paramName);
        }

        return normalizedSegment;
    }

    private static bool IsReservedDeviceName(string segment)
    {
        string name = segment.Split('.')[0];
        if (string.Equals(name, "CON", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "PRN", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "AUX", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "NUL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return name.Length == 4 &&
               (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
               name[3] is >= '1' and <= '9';
    }

    private static bool AreNormalizedPathsEquivalent(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNormalizedPathInDirectory(string path, string directory)
    {
        if (AreNormalizedPathsEquivalent(path, directory))
        {
            return true;
        }

        string directoryPrefix = Path.EndsInDirectorySeparator(directory)
            ? directory
            : directory + Path.DirectorySeparatorChar;
        return path.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
