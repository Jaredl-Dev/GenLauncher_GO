using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Locates authored application sources while excluding build output from source-contract scans.
/// </summary>
internal static class TestAppProjectSources
{
    private static readonly Lazy<DirectoryInfo> _projectDirectory = new(FindProjectDirectory);

    public static DirectoryInfo ProjectDirectory => _projectDirectory.Value;

    public static IEnumerable<FileInfo> Enumerate(string searchPattern)
    {
        return ProjectDirectory
            .EnumerateFiles(searchPattern, SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file));
    }

    private static DirectoryInfo FindProjectDirectory()
    {
        for (DirectoryInfo? candidate = new(AppContext.BaseDirectory);
             candidate != null;
             candidate = candidate.Parent)
        {
            DirectoryInfo appProject = new(Path.Combine(candidate.FullName, "GenLauncherGO"));
            if (appProject.Exists)
            {
                return appProject;
            }
        }

        throw new InvalidOperationException(
            $"No GenLauncherGO directory above '{AppContext.BaseDirectory}', so its sources cannot be scanned.");
    }

    private static bool IsBuildOutput(FileInfo file)
    {
        string relativePath = Path.GetRelativePath(ProjectDirectory.FullName, file.FullName);
        string topLevelSegment = relativePath.Split(Path.DirectorySeparatorChar)[0];
        return string.Equals(topLevelSegment, "bin", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(topLevelSegment, "obj", StringComparison.OrdinalIgnoreCase);
    }
}
