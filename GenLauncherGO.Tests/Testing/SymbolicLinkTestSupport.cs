using System;
using System.IO;

namespace GenLauncherGO.Tests.Testing;

internal static class SymbolicLinkTestSupport
{
    private const string RequiredEnvironmentVariable = "GENLAUNCHERGO_REQUIRE_SYMBOLIC_LINK_TESTS";
    private static readonly Lazy<bool> _symbolicLinkSupport = new(ProbeSymbolicLinkSupport);

    internal const string UnsupportedReason =
        "These safety tests require Windows file and directory symbolic-link support. " +
        "Enable Windows Developer Mode or run the tests with symbolic-link privileges.";

    internal static bool IsRequired =>
        Boolean.TryParse(
            Environment.GetEnvironmentVariable(RequiredEnvironmentVariable),
            out bool isRequired) &&
        isRequired;

    internal static bool IsSupported => _symbolicLinkSupport.Value;

    public static void CreateDirectoryLink(string linkPath, string targetPath)
    {
        CreateLink(
            () => Directory.CreateSymbolicLink(linkPath, targetPath),
            "directory");
    }

    public static void CreateFileLink(string linkPath, string targetPath)
    {
        CreateLink(
            () => File.CreateSymbolicLink(linkPath, targetPath),
            "file");
    }

    private static void CreateLink(Action createLink, string linkKind)
    {
        try
        {
            createLink();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            throw new InvalidOperationException(
                $"Could not create the {linkKind} symbolic link required by this safety test. " +
                "CI requires symbolic-link tests to execute.",
                exception);
        }
    }

    private static bool ProbeSymbolicLinkSupport()
    {
        string testRoot = Path.Combine(
            Path.GetTempPath(),
            "GenLauncherGO.Tests",
            $"SymbolicLinkProbe-{Guid.NewGuid():N}");
        string directoryTarget = Path.Combine(testRoot, "DirectoryTarget");
        string directoryLink = Path.Combine(testRoot, "DirectoryLink");
        string fileTarget = Path.Combine(testRoot, "FileTarget.txt");
        string fileLink = Path.Combine(testRoot, "FileLink.txt");

        try
        {
            Directory.CreateDirectory(directoryTarget);
            File.WriteAllText(fileTarget, "target");
            Directory.CreateSymbolicLink(directoryLink, directoryTarget);
            File.CreateSymbolicLink(fileLink, fileTarget);

            return File.GetAttributes(directoryLink).HasFlag(FileAttributes.ReparsePoint) &&
                File.GetAttributes(fileLink).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
        finally
        {
            TryDeleteFileLink(fileLink);
            TryDeleteDirectoryLink(directoryLink);
            TryDeleteProbeRoot(testRoot);
        }
    }

    private static void TryDeleteFileLink(string fileLink)
    {
        try
        {
            File.Delete(fileLink);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectoryLink(string directoryLink)
    {
        try
        {
            if (Directory.Exists(directoryLink))
            {
                Directory.Delete(directoryLink);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteProbeRoot(string testRoot)
    {
        try
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
