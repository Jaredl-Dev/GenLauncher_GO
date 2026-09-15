using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using GenLauncherGO.Shared.Archives;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;

namespace GenLauncherGO.Tests.Shared.Archives;

public sealed class ArchiveExtractorTests
{
    [Fact]
    public void ExtractToDirectory_PreservesBigFilesByDefault()
    {
        using TestDirectory directory = new();
        string archivePath = directory.GetPath("mod.zip");
        string destinationDirectory = directory.GetPath("extract");
        CreateZipArchive(archivePath, "Data/test.big", "test data");

        var extractor = new ArchiveExtractor();

        extractor.ExtractToDirectory(archivePath, destinationDirectory, cancellationToken: TestContext.Current.CancellationToken);

        File.Exists(Path.Combine(destinationDirectory, "Data", "test.big")).Should().BeTrue();
        File.Exists(Path.Combine(destinationDirectory, "Data", "test.gib")).Should().BeFalse();
    }

    [Fact]
    public void ExtractToDirectory_ConvertsBigFilesWhenRequested()
    {
        using TestDirectory directory = new();
        string archivePath = directory.GetPath("mod.zip");
        string destinationDirectory = directory.GetPath("extract");
        CreateZipArchive(archivePath, "Data/test.big", "test data");

        var extractor = new ArchiveExtractor();

        extractor.ExtractToDirectory(archivePath, destinationDirectory, true, TestContext.Current.CancellationToken);

        File.Exists(Path.Combine(destinationDirectory, "Data", "test.gib")).Should().BeTrue();
        File.Exists(Path.Combine(destinationDirectory, "Data", "test.big")).Should().BeFalse();
    }

    /// <summary>
    ///     Archive entry keys are attacker-controlled, so traversal through either separator and a rooted key all have
    ///     to be refused before anything is written.
    /// </summary>
    [Theory]
    [InlineData("../escape.txt", @"..\escape.txt")]
    [InlineData(@"..\escape.txt", @"..\escape.txt")]
    [InlineData(@"C:\escape.txt", @"C:\escape.txt")]
    public void ExtractToDirectory_RejectsEntriesOutsideDestinationDirectory(
        string entryKey,
        string escapeTargetPath)
    {
        using TestDirectory directory = new();
        string archivePath = directory.GetPath("mod.zip");
        string destinationDirectory = directory.GetPath("extract");
        string escapedFilePath = Path.GetFullPath(Path.Combine(destinationDirectory, escapeTargetPath));
        CreateZipArchive(archivePath, entryKey, "escaped");

        var extractor = new ArchiveExtractor();

        Action act = () => extractor.ExtractToDirectory(archivePath, destinationDirectory);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*outside the destination folder*");
        File.Exists(escapedFilePath).Should().BeFalse();
    }

    [Fact]
    public void ExtractToDirectory_ExtractsSevenZipArchiveAndConvertsBigFiles()
    {
        using TestDirectory directory = new();
        string archivePath = directory.GetPath("mod.7z");
        string destinationDirectory = directory.GetPath("extract");
        CreateSevenZipArchive(archivePath, "Data/test.big", "test data");

        var extractor = new ArchiveExtractor();

        extractor.ExtractToDirectory(archivePath, destinationDirectory, true, TestContext.Current.CancellationToken);

        File.ReadAllText(Path.Combine(destinationDirectory, "Data", "test.gib"))
            .Should().Be("test data");
        File.Exists(Path.Combine(destinationDirectory, "Data", "test.big")).Should().BeFalse();
    }

    [Fact]
    public void ExtractToDirectory_RejectsLinkedDestinationTreeWithoutWritingThroughIt()
    {
        using TestDirectory directory = new();
        string archivePath = directory.GetPath("mod.zip");
        string destinationDirectory = directory.CreateDirectory("extract");
        string linkedDirectory = Path.Combine(destinationDirectory, "linked");
        CreateZipArchive(archivePath, "linked/escape.txt", "escaped");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(
            directory,
            linkedDirectory);
        var extractor = new ArchiveExtractor();

        Action act = () => extractor.ExtractToDirectory(archivePath, destinationDirectory);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*reparse point*");
        File.Exists(Path.Combine(junction.TargetDirectory, "escape.txt")).Should().BeFalse();
        Directory.Exists(linkedDirectory).Should().BeTrue();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    [Fact]
    public void ExtractToDirectory_PreCanceledTokenDoesNotWriteArchiveEntries()
    {
        using TestDirectory directory = new();
        string archivePath = directory.GetPath("mod.zip");
        string destinationDirectory = directory.GetPath("extract");
        CreateZipArchive(archivePath, "readme.txt", "payload");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        var extractor = new ArchiveExtractor();

        Action act = () => extractor.ExtractToDirectory(
            archivePath,
            destinationDirectory,
            cancellationToken: cancellation.Token);

        act.Should().Throw<OperationCanceledException>();
        File.Exists(Path.Combine(destinationDirectory, "readme.txt")).Should().BeFalse();
    }

    private static void CreateZipArchive(string archivePath, string entryName, string contents)
    {
        using ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry(entryName);
        using Stream entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream);
        writer.Write(contents);
    }

    private static void CreateSevenZipArchive(string archivePath, string entryName, string contents)
    {
        using IWriter writer = WriterFactory.OpenWriter(
            archivePath,
            ArchiveType.SevenZip,
            new SevenZipWriterOptions());
        using MemoryStream contentsStream = new(Encoding.UTF8.GetBytes(contents));
        writer.Write(entryName, contentsStream, null);
    }
}
