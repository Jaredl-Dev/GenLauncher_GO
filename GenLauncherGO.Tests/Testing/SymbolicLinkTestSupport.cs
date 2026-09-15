using System;
using System.IO;

namespace GenLauncherGO.Tests.Testing;

internal static class SymbolicLinkTestSupport
{
    private const string RequiredEnvironmentVariable = "GENLAUNCHERGO_REQUIRE_SYMBOLIC_LINK_TESTS";

    internal const string UnsupportedReason =
        "These safety tests need a file that is itself a reparse point, which only a symbolic link provides. " +
        "A junction is a directory, so File.Exists short-circuits before production reaches its reparse check, " +
        "and every test that can use one already does. Enable Windows Developer Mode or run with symbolic-link " +
        "privileges to cover the remaining cases.";

    private static readonly Lazy<bool> _symbolicLinkSupport = new(ProbeSymbolicLinkSupport);

    internal static bool IsRequired =>
        bool.TryParse(
            Environment.GetEnvironmentVariable(RequiredEnvironmentVariable),
            out bool isRequired) &&
        isRequired;

    internal static bool IsSupported => _symbolicLinkSupport.Value;

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
        string fileTarget = Path.Combine(testRoot, "FileTarget.txt");
        string fileLink = Path.Combine(testRoot, "FileLink.txt");

        try
        {
            Directory.CreateDirectory(testRoot);
            File.WriteAllText(fileTarget, "target");
            File.CreateSymbolicLink(fileLink, fileTarget);

            return File.GetAttributes(fileLink).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
        finally
        {
            TryDeleteFileLink(fileLink);
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

    private static void TryDeleteProbeRoot(string testRoot)
    {
        try
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
