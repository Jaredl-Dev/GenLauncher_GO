using System.IO;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Common;

namespace GenLauncherGO.Infrastructure.Launching.Support;

/// <summary>
///     Resolves deployment manifest paths inside launcher-owned deployment roots.
/// </summary>
internal static class DeploymentPathResolver
{
    /// <summary>
    ///     Resolves a game-directory-relative manifest path.
    /// </summary>
    public static string ResolveGamePath(LauncherPaths paths, string relativePath)
    {
        string normalizedPath = NormalizeManifestPath(relativePath);
        string gameRoot = LexicalPath.NormalizeFullPath(paths.GameDirectory);
        string candidatePath = LexicalPath.ResolvePath(gameRoot, normalizedPath);
        string ownedGameDataRoot = LexicalPath.NormalizeFullPath(paths.OwnedGameDataDirectory);

        if (!LexicalPath.IsPathInDirectory(candidatePath, gameRoot) ||
            LexicalPath.IsPathInDirectory(candidatePath, ownedGameDataRoot))
        {
            throw new InvalidDataException($"Deployment target path '{relativePath}' is outside the game directory.");
        }

        return candidatePath;
    }

    public static string NormalizeManifestPath(string relativePath)
    {
        return ManifestPathResolver.NormalizeForDeploymentManifest(relativePath);
    }

    public static string ToRelativeManifestPath(string rootDirectory, string path)
    {
        return NormalizeManifestPath(LexicalPath.GetRelativePath(rootDirectory, path));
    }

    /// <summary>
    ///     Resolves a deployment-state-relative path.
    /// </summary>
    public static string ResolveDeploymentStatePath(string deploymentDirectory, string relativePath)
    {
        return LexicalPath.ResolveContainedPath(
            deploymentDirectory,
            NormalizeManifestPath(relativePath),
            $"Deployment state path '{relativePath}' is outside the deployment directory.");
    }
}
