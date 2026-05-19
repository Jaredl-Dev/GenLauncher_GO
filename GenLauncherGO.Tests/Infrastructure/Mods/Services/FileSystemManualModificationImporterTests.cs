using System;
using System.IO;
using System.Threading;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Infrastructure.Archives.Contracts;
using GenLauncherGO.Infrastructure.Mods.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Mods.Services;

public sealed class FileSystemManualModificationImporterTests
{
    [Fact]
    public void Import_CopiesRegularFilesToDestination()
    {
        using var directory = new TestDirectory();
        string sourceDirectory = Path.Combine(directory.Path, "source");
        string destinationDirectory = Path.Combine(directory.Path, "destination");
        Directory.CreateDirectory(sourceDirectory);
        string sourceFilePath = Path.Combine(sourceDirectory, "readme.txt");
        File.WriteAllText(sourceFilePath, "manual content");

        FileSystemManualModificationImporter importer = CreateImporter();

        importer.Import(
            new[] { sourceFilePath },
            CreateOwnedDestination(directory.Path, destinationDirectory));

        File.ReadAllText(Path.Combine(destinationDirectory, "readme.txt"))
            .Should().Be("manual content");
        File.Exists(sourceFilePath).Should().BeTrue();
    }

    [Fact]
    public void Import_RenamesLooseBigFilesToGibFiles()
    {
        using var directory = new TestDirectory();
        string sourceDirectory = Path.Combine(directory.Path, "source");
        string destinationDirectory = Path.Combine(directory.Path, "destination");
        Directory.CreateDirectory(sourceDirectory);
        string sourceFilePath = Path.Combine(sourceDirectory, "package.big");
        File.WriteAllText(sourceFilePath, "big content");

        FileSystemManualModificationImporter importer = CreateImporter();

        importer.Import(
            new[] { sourceFilePath },
            CreateOwnedDestination(directory.Path, destinationDirectory));

        File.Exists(Path.Combine(destinationDirectory, "package.big")).Should().BeFalse();
        File.ReadAllText(Path.Combine(destinationDirectory, "package.gib"))
            .Should().Be("big content");
    }

    [Fact]
    public void Import_CopiesLooseGibFilesToDestination()
    {
        using var directory = new TestDirectory();
        string sourceDirectory = Path.Combine(directory.Path, "source");
        string destinationDirectory = Path.Combine(directory.Path, "destination");
        Directory.CreateDirectory(sourceDirectory);
        string sourceFilePath = Path.Combine(sourceDirectory, "package.gib");
        File.WriteAllText(sourceFilePath, "gib content");

        FileSystemManualModificationImporter importer = CreateImporter();

        importer.Import(
            new[] { sourceFilePath },
            CreateOwnedDestination(directory.Path, destinationDirectory));

        File.ReadAllText(Path.Combine(destinationDirectory, "package.gib"))
            .Should().Be("gib content");
        File.Exists(sourceFilePath).Should().BeTrue();
    }

    /// <summary>
    ///     Re-importing over content that is already in place must not fail the whole selection, so a file that is
    ///     already present is left exactly as it is rather than copied over.
    /// </summary>
    [Fact]
    public void Import_DoesNotFailWhenDestinationFileAlreadyExists()
    {
        using var directory = new TestDirectory();
        string sourceFilePath = directory.CreateFile("source/readme.txt", "manual content");
        string destinationDirectory = directory.CreateDirectory("destination");
        string destinationFilePath = directory.CreateFile("destination/readme.txt", "already imported");
        FileSystemManualModificationImporter importer = CreateImporter();

        importer.Import(
            new[] { sourceFilePath },
            CreateOwnedDestination(directory.Path, destinationDirectory));

        File.ReadAllText(destinationFilePath).Should().Be("already imported");
    }

    [Theory]
    [InlineData(".zip")]
    [InlineData(".rar")]
    [InlineData(".7z")]
    public void Import_ExtractsArchivesAndDeletesStagedArchive(string extension)
    {
        using var directory = new TestDirectory();
        string sourceDirectory = Path.Combine(directory.Path, "source");
        string destinationDirectory = Path.Combine(directory.Path, "destination");
        Directory.CreateDirectory(sourceDirectory);
        string archiveFileName = "package" + extension;
        string sourceFilePath = Path.Combine(sourceDirectory, archiveFileName);
        File.WriteAllText(sourceFilePath, "archive content");

        RecordingArchiveExtractor archiveExtractor = new() { ExtractHandler = WriteExtractedFile };
        FileSystemManualModificationImporter importer = CreateImporter(archiveExtractor);

        importer.Import(
            new[] { sourceFilePath },
            CreateOwnedDestination(directory.Path, destinationDirectory));

        archiveExtractor.ArchiveFilePath.Should().Be(Path.Combine(destinationDirectory, archiveFileName));
        archiveExtractor.DestinationDirectory.Should().Be(destinationDirectory);
        File.Exists(Path.Combine(destinationDirectory, archiveFileName)).Should().BeFalse();
        File.ReadAllText(Path.Combine(destinationDirectory, "extracted.txt"))
            .Should().Be("extracted content");
        File.Exists(sourceFilePath).Should().BeTrue();
    }

    /// <summary>
    ///     A cancelled import is the user's own decision, so it stops at the next file and is never reported as a
    ///     failure.
    /// </summary>
    [Fact]
    public void Import_StopsAtCancellationWithoutImportingRemainingFiles()
    {
        using var directory = new TestDirectory();
        string archiveFilePath = directory.CreateFile("source/package.zip", "archive content");
        string remainingFilePath = directory.CreateFile("source/readme.txt", "manual content");
        string destinationDirectory = Path.Combine(directory.Path, "destination");
        using CancellationTokenSource cancellation = new();
        RecordingArchiveExtractor archiveExtractor = new() { ExtractHandler = _ => cancellation.Cancel() };
        RecordingLogger<FileSystemManualModificationImporter> logger = new();
        FileSystemManualModificationImporter importer = CreateImporter(archiveExtractor, logger);

        Action act = () => importer.Import(
            new[] { archiveFilePath, remainingFilePath },
            CreateOwnedDestination(directory.Path, destinationDirectory),
            cancellation.Token);

        act.Should().Throw<OperationCanceledException>();
        File.Exists(Path.Combine(destinationDirectory, "readme.txt")).Should().BeFalse();
        logger.Entries.Should().NotContain(entry => entry.LogLevel == LogLevel.Error);
    }

    [Fact]
    public void Import_RejectsEmptySourceFileList()
    {
        using var directory = new TestDirectory();
        FileSystemManualModificationImporter importer = CreateImporter();
        string destinationDirectory = Path.Combine(directory.Path, "destination");

        Action act = () => importer.Import(
            Array.Empty<string>(),
            CreateOwnedDestination(directory.Path, destinationDirectory));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*At least one source file is required*");
    }

    /// <summary>
    ///     Import copies each selected file under its own name. A path that names no file has no name to copy under,
    ///     and would otherwise resolve onto the destination folder itself.
    /// </summary>
    [Fact]
    public void Import_RejectsSourcePathThatNamesNoFile()
    {
        using var directory = new TestDirectory();
        string sourceDirectory = directory.CreateDirectory("source");
        string destinationDirectory = Path.Combine(directory.Path, "destination");
        FileSystemManualModificationImporter importer = CreateImporter();

        Action act = () => importer.Import(
            new[] { sourceDirectory + Path.DirectorySeparatorChar },
            CreateOwnedDestination(directory.Path, destinationDirectory));

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("sourceFilePath");
    }

    [Fact]
    public void ImportRequest_RejectsDestinationOutsideOwnershipBoundaryBeforeMutation()
    {
        using var directory = new TestDirectory();
        string sourceFilePath = directory.CreateFile("source/readme.txt", "manual content");
        string ownedRoot = directory.CreateDirectory("owned");
        string outsideDestination = Path.Combine(directory.Path, "outside", "1.0");
        FileSystemManualModificationImporter importer = CreateImporter();

        Action act = () => importer.Import(
            new[] { sourceFilePath },
            new OwnedContentPath(ownedRoot, outsideDestination));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*below its owning root*");
        Directory.Exists(outsideDestination).Should().BeFalse();
    }

    [Fact]
    public void Import_RejectsReparsePointsInDestinationBeforeArchiveExtraction()
    {
        using var directory = new TestDirectory();
        string sourceFilePath = directory.CreateFile("source/package.zip", "archive content");
        string ownedRoot = directory.CreateDirectory("owned");
        string destinationDirectory = directory.CreateDirectory("owned/Mod/1.0");
        string externalTarget = directory.CreateDirectory("external");
        string externalFile = directory.CreateFile("external/target.txt", "target");
        ReparsePointTestSupport.CreateDirectoryJunction(
            Path.Combine(destinationDirectory, "linked"),
            externalTarget);
        RecordingArchiveExtractor archiveExtractor = new();
        FileSystemManualModificationImporter importer = CreateImporter(archiveExtractor);

        Action act = () => importer.Import(
            new[] { sourceFilePath },
            CreateOwnedDestination(ownedRoot, destinationDirectory));

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*reparse point*");
        archiveExtractor.ArchiveFilePath.Should().BeNull();
        File.Exists(Path.Combine(destinationDirectory, "package.zip")).Should().BeFalse();
        File.ReadAllText(externalFile).Should().Be("target");
    }

    [Fact]
    public void Import_RethrowsWhenSourceFileIsMissing()
    {
        using var directory = new TestDirectory();
        string missingSourceFilePath = Path.Combine(directory.Path, "missing.gib");
        string destinationDirectory = Path.Combine(directory.Path, "destination");
        RecordingLogger<FileSystemManualModificationImporter> logger = new();
        FileSystemManualModificationImporter importer = CreateImporter(logger: logger);

        Action act = () => importer.Import(
            new[] { missingSourceFilePath },
            CreateOwnedDestination(directory.Path, destinationDirectory));

        act.Should().Throw<FileNotFoundException>();
        logger.Entries.Should().Contain(entry =>
            entry.LogLevel == LogLevel.Error &&
            entry.Exception is FileNotFoundException);
    }

    private static void WriteExtractedFile(string destinationDirectory)
    {
        File.WriteAllText(Path.Combine(destinationDirectory, "extracted.txt"), "extracted content");
    }

    private static OwnedContentPath CreateOwnedDestination(
        string ownedRoot,
        string destinationDirectory)
    {
        return new OwnedContentPath(ownedRoot, destinationDirectory);
    }

    private static FileSystemManualModificationImporter CreateImporter(
        IArchiveExtractor? archiveExtractor = null,
        ILogger<FileSystemManualModificationImporter>? logger = null)
    {
        return new FileSystemManualModificationImporter(
            archiveExtractor ?? new RecordingArchiveExtractor(),
            logger ?? NullLogger<FileSystemManualModificationImporter>.Instance);
    }
}
