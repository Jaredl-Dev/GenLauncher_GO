using System;
using System.IO;
using GenLauncherGO.Core.IO;

namespace GenLauncherGO.Infrastructure.Common;

/// <summary>
/// Defines the relative-path grammar shared by remote package manifests and durable deployment manifests.
/// </summary>
internal static class ManifestPathResolver
{
    /// <summary>
    /// Resolves a remote manifest file name to a full path under the specified root directory.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the root directory or manifest file name is empty, rooted, drive-qualified, or contains a current
    /// or parent directory segment.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// Thrown when the resolved path would leave <paramref name="rootDirectory"/>.
    /// </exception>
    public static string ResolvePath(string rootDirectory, string manifestFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestFileName);

        string normalizedFileName = NormalizeRelativePath(manifestFileName);
        string rootPath = LexicalPath.NormalizeFullPath(rootDirectory);
        string candidatePath = LexicalPath.ResolvePath(rootPath, normalizedFileName);

        if (!LexicalPath.IsPathInDirectory(candidatePath, rootPath))
        {
            throw new InvalidDataException(
                $"Manifest file '{manifestFileName}' would resolve outside the package directory.");
        }

        return candidatePath;
    }

    /// <summary>
    /// Normalizes a remote manifest path to the current platform directory separator after validation.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the path is rooted, drive-qualified, empty, or contains a current or parent directory segment.
    /// </exception>
    public static string NormalizeRelativePath(string manifestFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestFileName);

        return NormalizeRelativePathCore(
            manifestFileName.Trim(),
            useDeploymentErrors: false,
            nameof(manifestFileName));
    }

    /// <summary>
    /// Normalizes a remote manifest path to slash separators for manifest index lookups.
    /// </summary>
    public static string NormalizeForManifestIndex(string manifestFileName)
    {
        return LexicalPath.NormalizeRelativePath(NormalizeRelativePath(manifestFileName));
    }

    /// <summary>
    /// Normalizes a deployment manifest path to slash separators while preserving its durable-state error contract.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the path is empty or whitespace.</exception>
    /// <exception cref="InvalidDataException">
    /// Thrown when the path is rooted, drive-qualified, or contains a current or parent directory segment.
    /// </exception>
    public static string NormalizeForDeploymentManifest(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        return LexicalPath.NormalizeRelativePath(
            NormalizeRelativePathCore(
                relativePath,
                useDeploymentErrors: true,
                nameof(relativePath)));
    }

    private static string NormalizeRelativePathCore(
        string relativePath,
        bool useDeploymentErrors,
        string parameterName)
    {
        if (Path.IsPathRooted(relativePath) ||
            relativePath.Contains(':', StringComparison.Ordinal))
        {
            throw CreateValidationException(
                useDeploymentErrors,
                "Manifest file paths must be relative.",
                "Deployment manifest paths must be relative.",
                parameterName);
        }

        string[] segments = relativePath.Split(
            new[] { '/', '\\' },
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw CreateValidationException(
                useDeploymentErrors,
                "Manifest file paths must include a file name.",
                "Deployment manifest paths must include a file name.",
                parameterName);
        }

        foreach (string segment in segments)
        {
            if (string.Equals(segment, ".", StringComparison.Ordinal) ||
                string.Equals(segment, "..", StringComparison.Ordinal))
            {
                throw CreateValidationException(
                    useDeploymentErrors,
                    "Manifest file paths must not contain current or parent directory segments.",
                    "Deployment manifest paths must not contain parent directory segments.",
                    parameterName);
            }
        }

        return Path.Combine(segments);
    }

    private static Exception CreateValidationException(
        bool useDeploymentErrors,
        string manifestMessage,
        string deploymentMessage,
        string parameterName)
    {
        return useDeploymentErrors
            ? new InvalidDataException(deploymentMessage)
            : new ArgumentException(manifestMessage, parameterName);
    }
}
