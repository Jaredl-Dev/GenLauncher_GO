using System;
using System.IO;
using GenLauncherGO.Core.IO;

namespace GenLauncherGO.Infrastructure.Common;

/// <summary>
///     Defines the relative-path grammar shared by remote package manifests and durable deployment manifests.
/// </summary>
internal static class ManifestPathResolver
{
    /// <summary>
    ///     Resolves a remote manifest file name to a full path under the specified root directory.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     Thrown when the root directory or manifest file name is empty, rooted, drive-qualified, or contains a current
    ///     or parent directory segment.
    /// </exception>
    /// <exception cref="InvalidDataException">
    ///     Thrown when the resolved path would leave <paramref name="rootDirectory" />.
    /// </exception>
    public static string ResolvePath(string rootDirectory, string manifestFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestFileName);

        return LexicalPath.ResolveContainedPath(
            rootDirectory,
            NormalizeRelativePath(manifestFileName),
            $"Manifest file '{manifestFileName}' would resolve outside the package directory.");
    }

    /// <summary>
    ///     Resolves the installed path for a remote manifest file, including the canonical <c>.big</c>-to-<c>.gib</c>
    ///     conversion.
    /// </summary>
    public static string ResolveInstalledPath(string rootDirectory, string manifestFileName)
    {
        return BigFileVariantPath.GetInstalledPath(ResolvePath(rootDirectory, manifestFileName));
    }

    /// <summary>
    ///     Normalizes a remote manifest path to the current platform directory separator after validation.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     Thrown when the path is rooted, drive-qualified, empty, or contains a current or parent directory segment.
    /// </exception>
    public static string NormalizeRelativePath(string manifestFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestFileName);

        return NormalizeRelativePathCore(
            manifestFileName.Trim(),
            "Manifest file paths",
            message => new ArgumentException(message, nameof(manifestFileName)));
    }

    /// <summary>
    ///     Normalizes a remote manifest path to slash separators for manifest index lookups.
    /// </summary>
    public static string NormalizeForManifestIndex(string manifestFileName)
    {
        return LexicalPath.NormalizeRelativePath(NormalizeRelativePath(manifestFileName));
    }

    /// <summary>
    ///     Normalizes the installed relative path used for manifest lookups, including the canonical
    ///     <c>.big</c>-to-<c>.gib</c> conversion.
    /// </summary>
    public static string NormalizeInstalledPathForManifestIndex(string manifestFileName)
    {
        return BigFileVariantPath.GetInstalledPath(NormalizeForManifestIndex(manifestFileName));
    }

    /// <summary>
    ///     Normalizes a deployment manifest path to slash separators while preserving its durable-state error contract.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the path is empty or whitespace.</exception>
    /// <exception cref="InvalidDataException">
    ///     Thrown when the path is rooted, drive-qualified, or contains a current or parent directory segment.
    /// </exception>
    public static string NormalizeForDeploymentManifest(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        return LexicalPath.NormalizeRelativePath(
            NormalizeRelativePathCore(
                relativePath,
                "Deployment manifest paths",
                message => new InvalidDataException(message)));
    }

    /// <summary>
    ///     Applies the shared relative-path grammar, letting each caller name its paths and choose its failure type.
    /// </summary>
    /// <remarks>
    ///     Remote manifest names are caller input and fail as <see cref="ArgumentException" />; deployment manifest
    ///     paths are read back from durable journal state, where the same malformed value means corrupt data. Only
    ///     the exception type and the subject differ, so the grammar itself lives here once.
    /// </remarks>
    private static string NormalizeRelativePathCore(
        string relativePath,
        string pathSubject,
        Func<string, Exception> createValidationException)
    {
        if (Path.IsPathRooted(relativePath) ||
            relativePath.Contains(':', StringComparison.Ordinal))
        {
            throw createValidationException($"{pathSubject} must be relative.");
        }

        string[] segments = relativePath.Split(
            ['/', '\\'],
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw createValidationException($"{pathSubject} must include a file name.");
        }

        foreach (string segment in segments)
        {
            if (string.Equals(segment, ".", StringComparison.Ordinal) ||
                string.Equals(segment, "..", StringComparison.Ordinal))
            {
                throw createValidationException(
                    $"{pathSubject} must not contain parent directory segments.");
            }
        }

        return Path.Combine(segments);
    }
}
