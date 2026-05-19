using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Infrastructure.Archives.Contracts;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Services;
using GenLauncherGO.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Services;

public sealed class SingleFilePackageUpdaterTests
{
    [Fact]
    public async Task UpdateAsync_ClearsStaleTemporaryFilesBeforeInstallingAsync()
    {
        using TestDirectory testDirectory = new();
        string temporaryPath = Path.Combine(testDirectory.Path, "Staging", "temp");
        string installedPath = Path.Combine(testDirectory.Path, "Mods", "installed");
        Directory.CreateDirectory(temporaryPath);
        await File.WriteAllTextAsync(Path.Combine(temporaryPath, "stale.txt"), "stale");

        SingleFilePackageUpdater updater = new(
            new WritingFileDownloader("payload"),
            new StubMetadataReader("readme.txt", 7),
            new ThrowingArchiveExtractor(),
            NullLogger<SingleFilePackageUpdater>.Instance);

        (Uri SourceUri, PackageUpdatePathSet Paths) request = CreateRequest(
            new Uri("https://example.test/readme.txt"),
            testDirectory.Path,
            temporaryPath,
            installedPath);
        await updater.UpdateAsync(
            request.SourceUri,
            request.Paths,
            null,
            CancellationToken.None);

        File.Exists(Path.Combine(installedPath, "stale.txt")).Should().BeFalse();
        File.ReadAllText(Path.Combine(installedPath, "readme.txt")).Should().Be("payload");
    }

    [Fact]
    public async Task UpdateAsync_RemovesEmptyPackageStagingParentsAfterInstallingAsync()
    {
        using TestDirectory testDirectory = new();
        string temporaryRoot = Path.Combine(testDirectory.Path, "Runtime", "Temp");
        string packagesPath = Path.Combine(temporaryRoot, "Packages");
        string temporaryPath = Path.Combine(packagesPath, "NProject Mod", "2.11");
        string installedPath = Path.Combine(testDirectory.Path, "Mods", "installed");

        SingleFilePackageUpdater updater = new(
            new WritingFileDownloader("payload"),
            new StubMetadataReader("readme.txt", 7),
            new ThrowingArchiveExtractor(),
            NullLogger<SingleFilePackageUpdater>.Instance);

        (Uri SourceUri, PackageUpdatePathSet Paths) request = CreateRequest(
            new Uri("https://example.test/readme.txt"),
            testDirectory.Path,
            temporaryPath,
            installedPath);
        await updater.UpdateAsync(
            request.SourceUri,
            request.Paths,
            null,
            CancellationToken.None);

        File.ReadAllText(Path.Combine(installedPath, "readme.txt")).Should().Be("payload");
        Directory.Exists(packagesPath).Should().BeFalse();
        Directory.Exists(temporaryRoot).Should().BeTrue();
    }

    [Theory]
    [InlineData(".zip")]
    [InlineData(".rar")]
    [InlineData(".7z")]
    public async Task UpdateAsyncExtractsArchiveDeletesDownloadedArchiveAndInstallsExtractedFilesAsync(
        string extension)
    {
        using TestDirectory testDirectory = new();
        string temporaryPath = Path.Combine(testDirectory.Path, "Staging", "temp");
        string installedPath = Path.Combine(testDirectory.Path, "Mods", "installed");
        string archiveFileName = "package" + extension;
        var archiveExtractor = new RecordingArchiveExtractor("extracted.gib", "extracted");

        SingleFilePackageUpdater updater = new(
            new WritingFileDownloader("archive"),
            new StubMetadataReader(archiveFileName, 7),
            archiveExtractor,
            NullLogger<SingleFilePackageUpdater>.Instance);

        (Uri SourceUri, PackageUpdatePathSet Paths) request = CreateRequest(
            new Uri("https://example.test/" + archiveFileName),
            testDirectory.Path,
            temporaryPath,
            installedPath);
        await updater.UpdateAsync(
            request.SourceUri,
            request.Paths,
            null,
            CancellationToken.None);

        File.Exists(Path.Combine(installedPath, archiveFileName)).Should().BeFalse();
        File.ReadAllText(Path.Combine(installedPath, "extracted.gib")).Should().Be("extracted");
        archiveExtractor.ArchiveFileName.Should().Be(archiveFileName);
        archiveExtractor.ConvertBigFilesToGib.Should().BeTrue();
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../escape.zip")]
    [InlineData(@"nested\escape.zip")]
    [InlineData("nested/escape.zip")]
    [InlineData("C:escape.zip")]
    public async Task UpdateAsyncRejectsUnsafeRemoteMetadataFileNameAsync(string fileName)
    {
        using TestDirectory testDirectory = new();
        string temporaryPath = Path.Combine(testDirectory.Path, "Staging", "temp");
        string installedPath = Path.Combine(testDirectory.Path, "Mods", "installed");
        SingleFilePackageUpdater updater = new(
            new WritingFileDownloader("payload"),
            new StubMetadataReader(fileName, 7),
            new ThrowingArchiveExtractor(),
            NullLogger<SingleFilePackageUpdater>.Instance);

        (Uri SourceUri, PackageUpdatePathSet Paths) request = CreateRequest(
            new Uri("https://example.test/package.zip"),
            testDirectory.Path,
            temporaryPath,
            installedPath);
        Func<Task> update = () => updater.UpdateAsync(
            request.SourceUri,
            request.Paths,
            null,
            CancellationToken.None);

        await update.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*safe direct file name*");
        Directory.Exists(installedPath).Should().BeFalse();
        File.Exists(Path.Combine(testDirectory.Path, "escape.zip")).Should().BeFalse();
    }

    [SymbolicLinkFact]
    public async Task UpdateAsyncRejectsLinkedInstalledRootWithoutDeletingTargetAsync()
    {
        using TestDirectory testDirectory = new();
        string temporaryPath = Path.Combine(testDirectory.Path, "Staging", "temp");
        string installedPath = Path.Combine(testDirectory.Path, "Mods", "installed");
        string outsidePath = Path.Combine(testDirectory.Path, "outside");
        Directory.CreateDirectory(outsidePath);
        Directory.CreateDirectory(Path.GetDirectoryName(installedPath)!);
        string outsideFile = Path.Combine(outsidePath, "outside.txt");
        await File.WriteAllTextAsync(outsideFile, "outside");
        SymbolicLinkTestSupport.CreateDirectoryLink(installedPath, outsidePath);

        SingleFilePackageUpdater updater = new(
            new WritingFileDownloader("payload"),
            new StubMetadataReader("readme.txt", 7),
            new ThrowingArchiveExtractor(),
            NullLogger<SingleFilePackageUpdater>.Instance);

        (Uri SourceUri, PackageUpdatePathSet Paths) request = CreateRequest(
            new Uri("https://example.test/readme.txt"),
            testDirectory.Path,
            temporaryPath,
            installedPath);
        Func<Task> update = () => updater.UpdateAsync(
            request.SourceUri,
            request.Paths,
            null,
            CancellationToken.None);

        await update.Should().ThrowAsync<IOException>();
        File.ReadAllText(outsideFile).Should().Be("outside");
    }

    private static (Uri SourceUri, PackageUpdatePathSet Paths) CreateRequest(
        Uri sourceUri,
        string ownerRoot,
        string temporaryPath,
        string installedPath)
    {
        string packageRoot = Path.Combine(
            ownerRoot,
            "Runtime",
            "Temp",
            LauncherFileSystemLayout.PackagesFolderName);
        string temporaryOwnerRoot = temporaryPath.StartsWith(
            packageRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase)
            ? packageRoot
            : Path.Combine(ownerRoot, "Staging");
        string installedOwnerRoot = Path.Combine(ownerRoot, "Mods");
        string backupRoot = Path.Combine(ownerRoot, "Runtime", "State", "PackageBackups");
        string backupPath = Path.Combine(
            backupRoot,
            Path.GetRelativePath(installedOwnerRoot, installedPath));
        return (
            sourceUri,
            new PackageUpdatePathSet(
                new OwnedContentPath(temporaryOwnerRoot, temporaryPath),
                new OwnedContentPath(installedOwnerRoot, installedPath),
                new OwnedContentPath(backupRoot, backupPath)));
    }

    private sealed class WritingFileDownloader : IResumableFileDownloader
    {
        private readonly string _contents;

        public WritingFileDownloader(string contents)
        {
            _contents = contents;
        }

        public async Task DownloadFileAsync(
            DownloadFileRequest request,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            await File.WriteAllTextAsync(request.DestinationFilePath, _contents, cancellationToken);
            progress?.Report(new DownloadProgress(request.ExpectedBytes, _contents.Length, 100));
        }
    }

    private sealed class StubMetadataReader : IDownloadFileMetadataReader
    {
        private readonly string _fileName;
        private readonly long _totalBytes;

        public StubMetadataReader(string fileName, long totalBytes)
        {
            _fileName = fileName;
            _totalBytes = totalBytes;
        }

        public Task<DownloadFileMetadata> ReadMetadataAsync(
            Uri downloadUri,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new DownloadFileMetadata(downloadUri, _fileName, _totalBytes));
        }
    }

    private sealed class ThrowingArchiveExtractor : IArchiveExtractor
    {
        public void ExtractToDirectory(
            string archiveFilePath,
            string destinationDirectory,
            bool convertBigFilesToGib = false,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Extraction should not be used for non-archive files.");
        }
    }

    private sealed class RecordingArchiveExtractor : IArchiveExtractor
    {
        private readonly string _extractedFileName;
        private readonly string _extractedContents;

        public RecordingArchiveExtractor(string extractedFileName, string extractedContents)
        {
            _extractedFileName = extractedFileName;
            _extractedContents = extractedContents;
        }

        public string? ArchiveFileName { get; private set; }

        public bool? ConvertBigFilesToGib { get; private set; }

        public void ExtractToDirectory(
            string archiveFilePath,
            string destinationDirectory,
            bool convertBigFilesToGib = false,
            CancellationToken cancellationToken = default)
        {
            ArchiveFileName = Path.GetFileName(archiveFilePath);
            ConvertBigFilesToGib = convertBigFilesToGib;
            File.WriteAllText(
                Path.Combine(destinationDirectory, _extractedFileName),
                _extractedContents);
        }
    }
}
