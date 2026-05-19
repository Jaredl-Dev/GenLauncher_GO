using System;
using System.IO;
using System.Linq;
using GenLauncherGO.Infrastructure.Common;

namespace GenLauncherGO.Tests.Infrastructure.Common;

/// <summary>
///     The launcher decides whether two paths name the same game folder, and whether a recorded deployment still
///     describes the folder in front of it, from these two answers. Both have to see through the aliases a path can be
///     spelled with instead of comparing the spelling itself.
/// </summary>
public sealed class PhysicalDirectoryPathTests
{
    [Fact]
    public void ResolveExisting_JunctionPath_ReturnsTheCanonicalTargetPath()
    {
        using TestDirectory directory = new();
        string targetDirectory = directory.CreateDirectory("Target");
        string junctionPath = directory.GetPath("Link");
        ReparsePointTestSupport.CreateDirectoryJunction(junctionPath, targetDirectory);

        string result = PhysicalDirectoryPath.ResolveExisting(junctionPath);

        result.Should().Be(PhysicalDirectoryPath.ResolveExisting(targetDirectory));
        Path.GetFileName(result).Should().Be("Target");
    }

    /// <summary>
    ///     The canonical path is compared against ordinary normalized paths and stored in the deployment manifest, so a
    ///     device prefix or a trailing separator would silently break every one of those comparisons.
    /// </summary>
    [Fact]
    public void ResolveExisting_ReturnsAPlainPathWithoutADevicePrefixOrTrailingSeparator()
    {
        using TestDirectory directory = new();
        string targetDirectory = directory.CreateDirectory("Target");

        string result = PhysicalDirectoryPath.ResolveExisting(targetDirectory);

        result.Should().NotStartWith(@"\\?\");
        Path.EndsInDirectorySeparator(result).Should().BeFalse();
        Directory.Exists(result).Should().BeTrue();
    }

    /// <summary>
    ///     A caller may already hold the extended-length spelling of a path, which must not be prefixed a second time.
    /// </summary>
    [Fact]
    public void ResolveExisting_ExtendedLengthSpelling_ReturnsTheSameCanonicalPath()
    {
        using TestDirectory directory = new();
        string targetDirectory = directory.CreateDirectory("Target");

        string result = PhysicalDirectoryPath.ResolveExisting(@"\\?\" + Path.GetFullPath(targetDirectory));

        result.Should().Be(PhysicalDirectoryPath.ResolveExisting(targetDirectory));
    }

    /// <summary>
    ///     Game and launcher folders nest deeply enough to outgrow the first buffer the resolver asks Windows to fill,
    ///     and a truncated canonical path would name a different directory than the one that was opened.
    /// </summary>
    [Fact]
    public void ResolveExisting_PathLongerThanTheInitialBuffer_ReturnsTheCompletePath()
    {
        using TestDirectory directory = new();
        string[] segments = [.. Enumerable.Repeat(new string('d', 80), 7)];
        string deepDirectory = directory.CreateDirectory(string.Join('/', segments));

        string result = PhysicalDirectoryPath.ResolveExisting(deepDirectory);

        result.Should().Be(Path.Combine(
            PhysicalDirectoryPath.ResolveExisting(directory.Path),
            Path.Combine(segments)));
        result.Length.Should().BeGreaterThan(512);
    }

    [Fact]
    public void ResolveExisting_MissingDirectory_Throws()
    {
        using TestDirectory directory = new();
        string missingPath = directory.GetPath("Missing");

        Action act = () => PhysicalDirectoryPath.ResolveExisting(missingPath);

        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void ResolveExisting_FilePath_Throws()
    {
        using TestDirectory directory = new();
        string filePath = directory.CreateFile("game.exe", "binary");

        Action act = () => PhysicalDirectoryPath.ResolveExisting(filePath);

        act.Should().Throw<DirectoryNotFoundException>();
    }

    /// <summary>
    ///     Identity is what tells the launcher a recorded game folder is still the same folder after it was reached by
    ///     another name, so an alias has to answer with the identity of what it resolves to.
    /// </summary>
    [Fact]
    public void GetIdentity_JunctionPath_ReturnsTheTargetIdentity()
    {
        using TestDirectory directory = new();
        string targetDirectory = directory.CreateDirectory("Target");
        string junctionPath = directory.GetPath("Link");
        ReparsePointTestSupport.CreateDirectoryJunction(junctionPath, targetDirectory);

        PhysicalFileSystemIdentity result = PhysicalDirectoryPath.GetIdentity(junctionPath);

        result.Should().Be(PhysicalDirectoryPath.GetIdentity(targetDirectory));
    }

    /// <summary>
    ///     Two folders on one volume share a volume serial number and must still be told apart, which is the whole
    ///     reason the file index is part of the identity.
    /// </summary>
    [Fact]
    public void GetIdentity_DifferentDirectories_ReturnsDifferentIdentities()
    {
        using TestDirectory directory = new();
        string firstDirectory = directory.CreateDirectory("First");
        string secondDirectory = directory.CreateDirectory("Second");

        PhysicalFileSystemIdentity result = PhysicalDirectoryPath.GetIdentity(firstDirectory);

        PhysicalFileSystemIdentity secondIdentity = PhysicalDirectoryPath.GetIdentity(secondDirectory);
        result.Should().NotBe(secondIdentity);
        result.VolumeSerialNumber.Should().Be(secondIdentity.VolumeSerialNumber);
    }

    [Fact]
    public void GetIdentity_MissingDirectory_Throws()
    {
        using TestDirectory directory = new();
        string missingPath = directory.GetPath("Missing");

        Action act = () => PhysicalDirectoryPath.GetIdentity(missingPath);

        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void GetFileIdentity_JunctionPath_ReturnsTheTargetFileIdentity()
    {
        using TestDirectory directory = new();
        string junctionPath = directory.GetPath("Link");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(
            directory,
            junctionPath);

        PhysicalFileSystemIdentity result = PhysicalDirectoryPath.GetFileIdentity(
            Path.Combine(junctionPath, "target.txt"));

        result.Should().Be(PhysicalDirectoryPath.GetFileIdentity(junction.CanaryFilePath));
    }

    /// <summary>
    ///     Deployed files are identified while the game or another writer still holds them open, so reading an identity
    ///     must never contend for the file.
    /// </summary>
    [Fact]
    public void GetFileIdentity_FileHeldOpen_ReturnsTheSameIdentity()
    {
        using TestDirectory directory = new();
        string filePath = directory.CreateFile("state.yaml", "state");
        PhysicalFileSystemIdentity closedIdentity = PhysicalDirectoryPath.GetFileIdentity(filePath);
        using FileStream heldOpen = new(filePath, FileMode.Open, FileAccess.Write, FileShare.None);

        PhysicalFileSystemIdentity result = PhysicalDirectoryPath.GetFileIdentity(filePath);

        result.Should().Be(closedIdentity);
    }

    [Fact]
    public void GetFileIdentity_DifferentFiles_ReturnsDifferentIdentities()
    {
        using TestDirectory directory = new();
        string firstPath = directory.CreateFile("first.txt", "first");
        string secondPath = directory.CreateFile("second.txt", "second");

        PhysicalFileSystemIdentity result = PhysicalDirectoryPath.GetFileIdentity(firstPath);

        result.Should().NotBe(PhysicalDirectoryPath.GetFileIdentity(secondPath));
    }

    [Fact]
    public void GetFileIdentity_MissingFile_Throws()
    {
        using TestDirectory directory = new();
        string missingPath = directory.GetPath("missing.txt");

        Action act = () => PhysicalDirectoryPath.GetFileIdentity(missingPath);

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void GetFileIdentity_DirectoryPath_Throws()
    {
        using TestDirectory directory = new();
        string directoryPath = directory.CreateDirectory("Content");

        Action act = () => PhysicalDirectoryPath.GetFileIdentity(directoryPath);

        act.Should().Throw<FileNotFoundException>();
    }
}
