using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Infrastructure.Archives.Contracts;
using GenLauncherGO.Infrastructure.Mods.Services;
using GenLauncherGO.Tests.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Mods.Services;

public sealed class FileSystemManualModificationImporterTests
{
    [Fact]
    public void ImportCopiesRegularFilesToDestination()
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
    public void ImportRenamesLooseBigFilesToGibFiles()
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
    public void ImportCopiesLooseGibFilesToDestination()
    {
        using var directory = new TestDirectory();
        string sourceDirectory = Path.Combine(directory.Path, "source");
        string destinationDirectory = Path.Combine(directory.Path, "destination");
        Directory.CreateDirectory(sourceDirectory);
        string sourceFilePath = Path.Combine(sourceDirectory, "package.gib");
        File.WriteAllText(sourceFilePath, "gib content");
        RecordingLogger<FileSystemManualModificationImporter> logger = new();

        FileSystemManualModificationImporter importer = CreateImporter(logger: logger);

        importer.Import(
            new[] { sourceFilePath },
            CreateOwnedDestination(directory.Path, destinationDirectory));

        File.ReadAllText(Path.Combine(destinationDirectory, "package.gib"))
            .Should().Be("gib content");
        File.Exists(sourceFilePath).Should().BeTrue();
        logger.Entries.Should().Contain(entry =>
            entry.LogLevel == LogLevel.Information &&
            entry.Message.Contains("Imported 1 manual content file(s)", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(".zip")]
    [InlineData(".rar")]
    [InlineData(".7z")]
    public void ImportExtractsArchivesAndDeletesStagedArchive(string extension)
    {
        using var directory = new TestDirectory();
        string sourceDirectory = Path.Combine(directory.Path, "source");
        string destinationDirectory = Path.Combine(directory.Path, "destination");
        Directory.CreateDirectory(sourceDirectory);
        string archiveFileName = "package" + extension;
        string sourceFilePath = Path.Combine(sourceDirectory, archiveFileName);
        File.WriteAllText(sourceFilePath, "archive content");

        RecordingArchiveExtractor archiveExtractor = new();
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

    [Fact]
    public void ImportRejectsEmptySourceFileList()
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

    [Fact]
    public void ImportRequestRejectsDestinationOutsideOwnershipBoundaryBeforeMutation()
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

    [SymbolicLinkFact]
    public void ImportRejectsReparsePointsInDestinationBeforeArchiveExtraction()
    {
        using var directory = new TestDirectory();
        string sourceFilePath = directory.CreateFile("source/package.zip", "archive content");
        string ownedRoot = directory.CreateDirectory("owned");
        string destinationDirectory = directory.CreateDirectory("owned/Mod/1.0");
        string externalTarget = directory.CreateDirectory("external");
        string externalFile = directory.CreateFile("external/target.txt", "target");
        SymbolicLinkTestSupport.CreateDirectoryLink(
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
    public void ImportLogsFailuresBeforeRethrowing()
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
            entry.Exception is FileNotFoundException &&
            entry.Message.Contains("Failed to import manual content", StringComparison.Ordinal));
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

    private sealed class RecordingArchiveExtractor : IArchiveExtractor
    {
        public string? ArchiveFilePath { get; private set; }

        public string? DestinationDirectory { get; private set; }

        public void ExtractToDirectory(
            string archiveFilePath,
            string destinationDirectory,
            bool convertBigFilesToGib = false,
            CancellationToken cancellationToken = default)
        {
            ArchiveFilePath = archiveFilePath;
            DestinationDirectory = destinationDirectory;
            File.WriteAllText(Path.Combine(destinationDirectory, "extracted.txt"), "extracted content");
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(
        LogLevel LogLevel,
        string Message,
        Exception? Exception);

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
