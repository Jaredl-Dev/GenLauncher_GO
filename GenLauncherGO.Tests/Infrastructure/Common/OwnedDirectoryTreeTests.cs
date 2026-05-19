using System;
using System.Diagnostics;
using System.IO;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Tests.Testing;

namespace GenLauncherGO.Tests.Infrastructure.Common;

public sealed class OwnedDirectoryTreeTests
{
    [Fact]
    public void DeleteIfExistsDeletesNestedDirectoryLinkWithoutTouchingTarget()
    {
        using TestDirectory directory = new();
        string ownedRoot = Path.Combine(directory.Path, "Owned");
        string contentPath = Path.Combine(ownedRoot, "Content");
        string targetPath = Path.Combine(directory.Path, "ExternalTarget");
        string linkPath = Path.Combine(contentPath, "Linked");
        Directory.CreateDirectory(contentPath);
        Directory.CreateDirectory(targetPath);
        File.WriteAllText(Path.Combine(contentPath, "owned.txt"), "owned");
        File.WriteAllText(Path.Combine(targetPath, "target.txt"), "target");
        CreateDirectoryJunction(linkPath, targetPath);

        bool deleted = OwnedDirectoryTree.DeleteIfExists(
            new OwnedContentPath(ownedRoot, contentPath));

        deleted.Should().BeTrue();
        Directory.Exists(contentPath).Should().BeFalse();
        File.ReadAllText(Path.Combine(targetPath, "target.txt")).Should().Be("target");
    }

    [Fact]
    public void DeleteIfExistsDeletesLinkedLeafWithoutTouchingTarget()
    {
        using TestDirectory directory = new();
        string ownedRoot = Path.Combine(directory.Path, "Owned");
        string targetPath = Path.Combine(directory.Path, "ExternalTarget");
        string linkPath = Path.Combine(ownedRoot, "Version");
        Directory.CreateDirectory(ownedRoot);
        Directory.CreateDirectory(targetPath);
        File.WriteAllText(Path.Combine(targetPath, "target.txt"), "target");
        CreateDirectoryJunction(linkPath, targetPath);

        bool deleted = OwnedDirectoryTree.DeleteIfExists(
            new OwnedContentPath(ownedRoot, linkPath));

        deleted.Should().BeTrue();
        Directory.Exists(linkPath).Should().BeFalse();
        File.ReadAllText(Path.Combine(targetPath, "target.txt")).Should().Be("target");
    }

    [Fact]
    public void DeleteIfExistsRejectsLinkedAncestorWithoutTouchingTarget()
    {
        using TestDirectory directory = new();
        string ownedRoot = Path.Combine(directory.Path, "Owned");
        string targetPath = Path.Combine(directory.Path, "ExternalTarget");
        string linkedAncestor = Path.Combine(ownedRoot, "Linked");
        string targetChild = Path.Combine(targetPath, "Child");
        string candidatePath = Path.Combine(linkedAncestor, "Child");
        Directory.CreateDirectory(ownedRoot);
        Directory.CreateDirectory(targetChild);
        File.WriteAllText(Path.Combine(targetChild, "target.txt"), "target");
        CreateDirectoryJunction(linkedAncestor, targetPath);

        Action act = () => OwnedDirectoryTree.DeleteIfExists(
            new OwnedContentPath(ownedRoot, candidatePath));

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*reparse point*");
        File.ReadAllText(Path.Combine(targetChild, "target.txt")).Should().Be("target");

        Directory.Delete(linkedAncestor, recursive: false);
    }

    private static void CreateDirectoryJunction(string linkPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
            Arguments = $"/d /c mklink /J \"{linkPath}\" \"{targetPath}\"",
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using Process process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException("Could not start the junction creation process.");
        process.WaitForExit();

        process.ExitCode.Should().Be(
            0,
            $"junction creation should succeed. Output: {process.StandardOutput.ReadToEnd()} {process.StandardError.ReadToEnd()}");
    }
}
