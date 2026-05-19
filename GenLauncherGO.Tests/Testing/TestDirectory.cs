using System;
using System.IO;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
/// Owns a temporary directory for a test and deletes it during disposal.
/// </summary>
internal sealed class TestDirectory : IDisposable
{
    private const string TestRootFolderName = "GenLauncherGO.Tests";

    public TestDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            TestRootFolderName,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string GetPath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (System.IO.Path.IsPathFullyQualified(relativePath))
        {
            throw new ArgumentException("The test path must be relative.", nameof(relativePath));
        }

        string fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(Path, relativePath));
        string resolvedRelativePath = System.IO.Path.GetRelativePath(Path, fullPath);
        string parentPrefix = $"..{System.IO.Path.DirectorySeparatorChar}";

        if (resolvedRelativePath.Equals("..", StringComparison.Ordinal) ||
            resolvedRelativePath.StartsWith(parentPrefix, StringComparison.Ordinal) ||
            System.IO.Path.IsPathFullyQualified(resolvedRelativePath))
        {
            throw new ArgumentException("The test path must stay inside the owned directory.", nameof(relativePath));
        }

        return fullPath;
    }

    public string CreateDirectory(string relativePath)
    {
        string directoryPath = GetPath(relativePath);
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    public string CreateFile(string relativePath, string contents = "")
    {
        ArgumentNullException.ThrowIfNull(contents);

        string filePath = GetPath(relativePath);
        string? parentDirectory = System.IO.Path.GetDirectoryName(filePath);
        if (parentDirectory != null)
        {
            Directory.CreateDirectory(parentDirectory);
        }

        File.WriteAllText(filePath, contents);
        return filePath;
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
