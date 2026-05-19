using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Infrastructure.Common;

namespace GenLauncherGO.Infrastructure.Launching.Support;

/// <summary>
/// Resolves package files and directories for deployment without mutating the filesystem.
/// </summary>
internal static class DeploymentFilePlanner
{
    /// <summary>
    /// Resolves deployable package files and applies package precedence. Windows executable and library binaries remain
    /// in launcher-owned package storage and are never copied into the user's game directory.
    /// </summary>
    public static IReadOnlyList<ResolvedDeploymentFile> ResolveDeploymentFiles(
        IReadOnlyList<DeploymentPackage> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);

        var filesByTarget = new Dictionary<string, ResolvedDeploymentFile>(StringComparer.OrdinalIgnoreCase);
        foreach (DeploymentPackage package in packages.OrderBy(package => package.Precedence))
        {
            string packageRoot = LexicalPath.NormalizeFullPath(package.RootDirectory);
            if (!Directory.Exists(packageRoot))
            {
                throw new DirectoryNotFoundException(
                    $"Deployment package directory was not found: {package.RootDirectory}");
            }

            FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
                packageRoot,
                "Deployment package paths must be rooted.",
                "Deployment package directories must not contain reparse points.");
            FileSystemPathSafety.EnsureDirectoryTreeHasNoReparsePoints(
                packageRoot,
                "Deployment package directories must not contain reparse points.");

            foreach (string sourcePath in Directory.EnumerateFiles(
                         packageRoot,
                         "*",
                         FileSystemPathSafety.CreateRecursiveNoLinksOptions()))
            {
                string extension = Path.GetExtension(sourcePath);
                // Community packages can include their own launchers and tools. Treat those binaries as package-owned
                // code, not game-directory payload, so deployment cannot replace executable code in the user's install.
                if (string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relativePath = DeploymentPathResolver.ToRelativeManifestPath(packageRoot, sourcePath);
                string targetRelativePath = LexicalPath.NormalizeRelativePath(
                    BigFileVariantPath.GetDeploymentPath(relativePath));
                string normalizedTargetPath = DeploymentPathResolver.NormalizeManifestPath(targetRelativePath);
                filesByTarget[normalizedTargetPath] = new ResolvedDeploymentFile(
                    sourcePath,
                    normalizedTargetPath);
            }
        }

        return filesByTarget.Values.OrderBy(file => file.TargetRelativePath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Returns missing directories between a game root and target directory in parent-first order.
    /// </summary>
    public static IEnumerable<string> GetDirectoriesToCreate(string gameRoot, string targetDirectory)
    {
        var directories = new Stack<string>();
        string root = LexicalPath.NormalizeFullPath(gameRoot);
        string? current = LexicalPath.NormalizeFullPath(targetDirectory);
        while (!string.IsNullOrWhiteSpace(current) &&
               !string.Equals(LexicalPath.NormalizeFullPath(current), root, StringComparison.OrdinalIgnoreCase) &&
               !Directory.Exists(current))
        {
            directories.Push(current);
            current = Directory.GetParent(current)?.FullName;
        }

        return directories;
    }
}

internal sealed record ResolvedDeploymentFile(
    string SourcePath,
    string TargetRelativePath);

internal sealed record DeploymentPackage
{
    public DeploymentPackage(string rootDirectory, int precedence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        RootDirectory = rootDirectory;
        Precedence = precedence;
    }

    public string RootDirectory { get; }

    public int Precedence { get; }
}
