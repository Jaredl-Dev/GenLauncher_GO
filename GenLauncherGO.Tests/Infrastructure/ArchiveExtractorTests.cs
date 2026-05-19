using System;
using System.IO;
using System.IO.Compression;
using GenLauncherGO.Infrastructure.Archives;
using GenLauncherGO.Tests.Testing;

namespace GenLauncherGO.Tests.Infrastructure;

public sealed class ArchiveExtractorTests
{
    [Fact]
    public void ExtractToDirectoryPreservesBigFilesByDefault()
    {
        using TestDirectory directory = new();
        string archivePath = directory.GetPath("mod.zip");
        string destinationDirectory = directory.GetPath("extract");
        CreateZipArchive(archivePath, "Data/test.big", "test data");

        var extractor = new ArchiveExtractor();

        extractor.ExtractToDirectory(archivePath, destinationDirectory);

        File.Exists(Path.Combine(destinationDirectory, "Data", "test.big")).Should().BeTrue();
        File.Exists(Path.Combine(destinationDirectory, "Data", "test.gib")).Should().BeFalse();
    }

    [Fact]
    public void ExtractToDirectoryConvertsBigFilesWhenRequested()
    {
        using TestDirectory directory = new();
        string archivePath = directory.GetPath("mod.zip");
        string destinationDirectory = directory.GetPath("extract");
        CreateZipArchive(archivePath, "Data/test.big", "test data");

        var extractor = new ArchiveExtractor();

        extractor.ExtractToDirectory(
            archivePath,
            destinationDirectory,
            convertBigFilesToGib: true);

        File.Exists(Path.Combine(destinationDirectory, "Data", "test.gib")).Should().BeTrue();
        File.Exists(Path.Combine(destinationDirectory, "Data", "test.big")).Should().BeFalse();
    }

    [Fact]
    public void ExtractToDirectoryRejectsEntriesOutsideDestinationDirectory()
    {
        using TestDirectory directory = new();
        string archivePath = directory.GetPath("mod.zip");
        string destinationDirectory = directory.GetPath("extract");
        string escapedFilePath = directory.GetPath("escape.txt");
        CreateZipArchive(archivePath, "../escape.txt", "escaped");

        var extractor = new ArchiveExtractor();

        Action act = () => extractor.ExtractToDirectory(archivePath, destinationDirectory);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*outside the destination folder*");
        File.Exists(escapedFilePath).Should().BeFalse();
    }

    private static void CreateZipArchive(string archivePath, string entryName, string contents)
    {
        using ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry(entryName);
        using Stream entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream);
        writer.Write(contents);
    }
}
