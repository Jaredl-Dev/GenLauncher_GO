using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Integrity.Services;
using GenLauncherGO.Infrastructure.Integrity.Support;
using GenLauncherGO.Infrastructure.Persistence.Services;
using GenLauncherGO.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Integrity.Services;

public sealed class FileSystemContentIntegrityServiceTests
{
    [Fact]
    public async Task VerifyAsyncReportsVerificationErrorWhenSnapshotCannotBeReadAsync()
    {
        using TestDirectory directory = new();
        string content = Path.Combine(directory.Path, "content");
        Directory.CreateDirectory(content);
        string snapshotDirectory = CreatePaths(directory.Path).IntegrityDirectory;
        Directory.CreateDirectory(snapshotDirectory);
        await File.WriteAllTextAsync(GetSnapshotPath(snapshotDirectory, "target"), "{");
        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);

        ContentIntegrityReport report = await service.VerifyAsync(
            CreatePaths(directory.Path),
            new[] { target },
            CancellationToken.None);

        report.Issues.Should().ContainSingle(issue =>
            issue.Kind == IntegrityIssueKind.VerificationError &&
            issue.Action == IntegrityIssueAction.Block &&
            issue.RelativePath == "." &&
            !String.IsNullOrWhiteSpace(issue.Message));
    }

    [Fact]
    public async Task VerifyAsyncDetectsSameSizeSha256ModificationAsync()
    {
        using TestDirectory directory = new();
        string content = Path.Combine(directory.Path, "content");
        Directory.CreateDirectory(content);
        string filePath = Path.Combine(content, "file.bin");
        await File.WriteAllTextAsync(filePath, "aaaa");
        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        await service.CaptureSnapshotAsync(CreatePaths(directory.Path), target, CancellationToken.None);
        await File.WriteAllTextAsync(filePath, "bbbb");

        ContentIntegrityReport report = await service.VerifyAsync(
            CreatePaths(directory.Path),
            new[] { target },
            CancellationToken.None);

        report.Issues.Should().ContainSingle(issue =>
            issue.Kind == IntegrityIssueKind.ModifiedFile &&
            issue.Action == IntegrityIssueAction.Repair &&
            issue.RelativePath == "file.bin" &&
            issue.ExpectedSizeBytes == 4);
    }

    [Fact]
    public async Task VerifyAsyncCollectsMissingUnexpectedAndEmptyDirectoryIssuesAsync()
    {
        using TestDirectory directory = new();
        string content = Path.Combine(directory.Path, "content");
        Directory.CreateDirectory(content);
        string expectedPath = Path.Combine(content, "expected.txt");
        await File.WriteAllTextAsync(expectedPath, "expected");
        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        await service.CaptureSnapshotAsync(CreatePaths(directory.Path), target, CancellationToken.None);
        File.Delete(expectedPath);
        await File.WriteAllTextAsync(Path.Combine(content, "unexpected.txt"), "unexpected");
        Directory.CreateDirectory(Path.Combine(content, "nested", "empty"));

        ContentIntegrityReport report = await service.VerifyAsync(
            CreatePaths(directory.Path),
            new[] { target },
            CancellationToken.None);

        report.Issues.Should().Contain(issue =>
            issue.Kind == IntegrityIssueKind.MissingFile &&
            issue.ExpectedSizeBytes == 8);
        report.Issues.Select(issue => issue.Kind).Should().Contain(IntegrityIssueKind.UnexpectedFile);
        report.Issues.Select(issue => issue.Kind).Should().Contain(IntegrityIssueKind.EmptyDirectory);
    }

    [Fact]
    public async Task VerifyAsyncAlwaysReportsManagedEmptyDirectoriesAsync()
    {
        using TestDirectory directory = new();
        string content = Path.Combine(directory.Path, "content");
        Directory.CreateDirectory(Path.Combine(content, "nested", "empty"));
        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        await service.CaptureSnapshotAsync(CreatePaths(directory.Path), target, CancellationToken.None);

        ContentIntegrityReport report = await service.VerifyAsync(
            CreatePaths(directory.Path),
            new[] { target },
            CancellationToken.None);

        report.Issues.Should().ContainSingle(issue =>
            issue.Kind == IntegrityIssueKind.EmptyDirectory &&
            issue.Action == IntegrityIssueAction.Delete &&
            issue.RelativePath == "nested/empty");
    }

    [Fact]
    public async Task VerifyAsyncClassifiesManagedSingleFileDifferencesAsync()
    {
        using TestDirectory directory = new();
        string content = Path.Combine(directory.Path, "content");
        Directory.CreateDirectory(content);
        string expectedPath = Path.Combine(content, "expected.txt");
        await File.WriteAllTextAsync(expectedPath, "expected");
        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedSingleFile);
        await service.CaptureSnapshotAsync(CreatePaths(directory.Path), target, CancellationToken.None);
        File.Delete(expectedPath);
        await File.WriteAllTextAsync(Path.Combine(content, "unexpected.txt"), "unexpected");
        Directory.CreateDirectory(Path.Combine(content, "empty"));

        ContentIntegrityReport report = await service.VerifyAsync(
            CreatePaths(directory.Path),
            new[] { target },
            CancellationToken.None);

        report.Issues.Should().Contain(issue =>
            issue.Kind == IntegrityIssueKind.MissingFile &&
            issue.Action == IntegrityIssueAction.Redownload &&
            issue.RelativePath == "expected.txt");
        report.Issues.Should().Contain(issue =>
            issue.Kind == IntegrityIssueKind.UnexpectedFile &&
            issue.Action == IntegrityIssueAction.Delete &&
            issue.RelativePath == "unexpected.txt");
        report.Issues.Should().Contain(issue =>
            issue.Kind == IntegrityIssueKind.EmptyDirectory &&
            issue.Action == IntegrityIssueAction.Delete &&
            issue.RelativePath == "empty");
    }

    [Fact]
    public async Task VerifyAsyncClassifiesUnknownLegacyDifferencesForManualTrustAsync()
    {
        using TestDirectory directory = new();
        string content = Path.Combine(directory.Path, "content");
        Directory.CreateDirectory(content);
        string filePath = Path.Combine(content, "file.txt");
        await File.WriteAllTextAsync(filePath, "before");
        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.UnknownLegacy);
        await service.CaptureSnapshotAsync(CreatePaths(directory.Path), target, CancellationToken.None);
        await File.WriteAllTextAsync(filePath, "after");
        await File.WriteAllTextAsync(Path.Combine(content, "added.txt"), "added");

        ContentIntegrityReport report = await service.VerifyAsync(
            CreatePaths(directory.Path),
            new[] { target },
            CancellationToken.None);

        report.Issues.Should().NotBeEmpty();
        report.Issues.Should().OnlyContain(issue => issue.Action == IntegrityIssueAction.TrustAsManual);
    }

    [Fact]
    public async Task VerifyAsyncMarksManualDifferencesForAbsorptionAsync()
    {
        using TestDirectory directory = new();
        string content = Path.Combine(directory.Path, "content");
        Directory.CreateDirectory(content);
        await File.WriteAllTextAsync(Path.Combine(content, "file.txt"), "before");
        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.Manual);
        await service.CaptureSnapshotAsync(CreatePaths(directory.Path), target, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(content, "file.txt"), "after");
        await File.WriteAllTextAsync(Path.Combine(content, "added.txt"), "added");

        ContentIntegrityReport report = await service.VerifyAsync(
            CreatePaths(directory.Path),
            new[] { target },
            CancellationToken.None);

        report.Issues.Should().NotBeEmpty()
            .And.OnlyContain(issue => issue.Action == IntegrityIssueAction.Absorb);
    }

    [Fact]
    public async Task CaptureSnapshotAsyncAbsorbsManualDifferencesAsync()
    {
        using TestDirectory directory = new();
        string content = Path.Combine(directory.Path, "content");
        Directory.CreateDirectory(content);
        string filePath = Path.Combine(content, "file.txt");
        await File.WriteAllTextAsync(filePath, "before");
        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.Manual);
        await service.CaptureSnapshotAsync(CreatePaths(directory.Path), target, CancellationToken.None);
        await File.WriteAllTextAsync(filePath, "after");

        await service.CaptureSnapshotAsync(CreatePaths(directory.Path), target, CancellationToken.None);
        ContentIntegrityReport report = await service.VerifyAsync(
            CreatePaths(directory.Path),
            new[] { target },
            CancellationToken.None);

        report.HasIssues.Should().BeFalse();
    }

    [Fact]
    public async Task CaptureSnapshotAsyncCommitsCompleteDocumentThroughAtomicWriterAsync()
    {
        using TestDirectory directory = new();
        string content = directory.CreateDirectory("content");
        await File.WriteAllTextAsync(Path.Combine(content, "file.txt"), "content");
        RecordingAtomicFileWriter atomicFileWriter = new();
        FileSystemContentIntegrityService service = CreateService(directory.Path, atomicFileWriter);
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        using var cancellationTokenSource = new CancellationTokenSource();

        await service.CaptureSnapshotAsync(
            CreatePaths(directory.Path),
            target,
            cancellationTokenSource.Token);

        atomicFileWriter.WasWriteAsyncCalled.Should().BeTrue();
        atomicFileWriter.CancellationToken.Should().Be(cancellationTokenSource.Token);
        atomicFileWriter.DestinationPath.Should().Be(GetSnapshotPath(
            CreatePaths(directory.Path).IntegrityDirectory,
            target.Id));
        ContentIntegritySnapshotDocument? snapshot =
            JsonSerializer.Deserialize<ContentIntegritySnapshotDocument>(atomicFileWriter.Contents!);
        snapshot.Should().NotBeNull();
        snapshot!.TargetId.Should().Be(target.Id);
        snapshot.Files.Should().ContainSingle().Which.RelativePath.Should().Be("file.txt");
    }

    [Fact]
    public async Task IntegritySnapshotsSwitchGameNamespaceWithoutRebuildingServiceAsync()
    {
        using TestDirectory directory = new();
        string executableDirectory = directory.CreateDirectory("Launcher");
        var storagePaths = new LauncherStoragePaths(executableDirectory);
        LauncherPaths generalsPaths = storagePaths.CreateGamePaths(
            SupportedGame.Generals,
            directory.CreateDirectory("GeneralsGame"));
        LauncherPaths zeroHourPaths = storagePaths.CreateGamePaths(
            SupportedGame.ZeroHour,
            directory.CreateDirectory("ZeroHourGame"));
        var service = new FileSystemContentIntegrityService(
            new AtomicFileWriter(),
            NullLogger<FileSystemContentIntegrityService>.Instance);
        string content = directory.CreateDirectory("content");
        await File.WriteAllTextAsync(Path.Combine(content, "file.txt"), "content");
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);

        await service.CaptureSnapshotAsync(generalsPaths, target, CancellationToken.None);
        ContentIntegrityReport zeroHourReport = await service.VerifyAsync(
            zeroHourPaths,
            new[] { target },
            CancellationToken.None);

        zeroHourReport.Issues.Should().ContainSingle(issue =>
            issue.Kind == IntegrityIssueKind.Untracked);
        File.Exists(GetSnapshotPath(generalsPaths.IntegrityDirectory, target.Id)).Should().BeTrue();
        File.Exists(GetSnapshotPath(zeroHourPaths.IntegrityDirectory, target.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsyncPreservesIgnoredInactiveCacheFileAsync()
    {
        using TestDirectory directory = new();
        string content = Path.Combine(directory.Path, "content");
        Directory.CreateDirectory(content);
        await File.WriteAllTextAsync(Path.Combine(content, "active.png"), "active");
        await File.WriteAllTextAsync(Path.Combine(content, "inactive.png"), "inactive");
        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityTarget target = new(
            "target",
            "Target",
            content,
            ContentSourceKind.ManagedS3,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "inactive.png" });
        await service.CaptureSnapshotAsync(CreatePaths(directory.Path), target, CancellationToken.None);

        ContentIntegrityReport report = await service.VerifyAsync(
            CreatePaths(directory.Path),
            new[] { target },
            CancellationToken.None);

        report.HasIssues.Should().BeFalse();
    }

    [Fact]
    public async Task CaptureSnapshotIfMatchesExpectedFileSetAsyncCapturesExistingManagedCacheWithoutMutationAsync()
    {
        using TestDirectory directory = new();
        string content = Path.Combine(directory.Path, "content");
        Directory.CreateDirectory(content);
        string activePath = Path.Combine(content, "active.png");
        string inactivePath = Path.Combine(content, "inactive.png");
        await File.WriteAllTextAsync(activePath, "active");
        await File.WriteAllTextAsync(inactivePath, "inactive");
        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityTarget target = new(
            "target",
            "Target",
            content,
            ContentSourceKind.ManagedS3,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "inactive.png" });

        bool captured = await service.CaptureSnapshotIfMatchesExpectedFileSetAsync(
            CreatePaths(directory.Path),
            target,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "active.png" },
            CancellationToken.None);
        ContentIntegrityReport report = await service.VerifyAsync(
            CreatePaths(directory.Path),
            new[] { target },
            CancellationToken.None);

        captured.Should().BeTrue();
        report.HasIssues.Should().BeFalse();
        File.ReadAllText(activePath).Should().Be("active");
        File.ReadAllText(inactivePath).Should().Be("inactive");
    }

    [Fact]
    public async Task CaptureSnapshotIfMatchesExpectedFileSetAsyncRejectsExtrasWithoutSnapshottingAsync()
    {
        using TestDirectory directory = new();
        string content = Path.Combine(directory.Path, "content");
        Directory.CreateDirectory(content);
        await File.WriteAllTextAsync(Path.Combine(content, "active.png"), "active");
        await File.WriteAllTextAsync(Path.Combine(content, "extra.png"), "extra");
        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);

        bool captured = await service.CaptureSnapshotIfMatchesExpectedFileSetAsync(
            CreatePaths(directory.Path),
            target,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "active.png" },
            CancellationToken.None);
        ContentIntegrityReport report = await service.VerifyAsync(
            CreatePaths(directory.Path),
            new[] { target },
            CancellationToken.None);

        captured.Should().BeFalse();
        report.Issues.Should().ContainSingle(issue =>
            issue.Kind == IntegrityIssueKind.Untracked &&
            issue.Action == IntegrityIssueAction.Repair);
    }

    [SymbolicLinkFact]
    public async Task VerifyAsyncReportsIgnoredUnsafeLinkWithoutFollowingItAsync()
    {
        using TestDirectory directory = new();
        string outsidePath = Path.Combine(directory.Path, "outside.txt");
        await File.WriteAllTextAsync(outsidePath, "outside");
        string content = Path.Combine(directory.Path, "content");
        Directory.CreateDirectory(content);
        string linkPath = Path.Combine(content, "inactive.png");
        SymbolicLinkTestSupport.CreateFileLink(linkPath, outsidePath);

        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityTarget target = new(
            "target",
            "Target",
            content,
            ContentSourceKind.ManagedS3,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "inactive.png" });

        ContentIntegrityReport report = await service.VerifyAsync(
            CreatePaths(directory.Path),
            new[] { target },
            CancellationToken.None);

        report.Issues.Should().Contain(issue =>
            issue.Kind == IntegrityIssueKind.UnsafeLink &&
            issue.Action == IntegrityIssueAction.Delete &&
            issue.RelativePath == "inactive.png");
        File.ReadAllText(outsidePath).Should().Be("outside");
    }

    [Theory]
    [InlineData(ContentSourceKind.ManagedS3, IntegrityIssueAction.Repair)]
    [InlineData(ContentSourceKind.ManagedSingleFile, IntegrityIssueAction.Redownload)]
    [InlineData(ContentSourceKind.Manual, IntegrityIssueAction.Absorb)]
    [InlineData(ContentSourceKind.UnknownLegacy, IntegrityIssueAction.TrustAsManual)]
    public async Task VerifyAsyncClassifiesUntrackedContentBySourceAsync(
        ContentSourceKind sourceKind,
        IntegrityIssueAction expectedAction)
    {
        using TestDirectory directory = new();
        string content = Path.Combine(directory.Path, "content");
        Directory.CreateDirectory(content);
        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityTarget target = CreateTarget(content, sourceKind);

        ContentIntegrityReport report = await service.VerifyAsync(
            CreatePaths(directory.Path),
            new[] { target },
            CancellationToken.None);

        report.Issues.Should().ContainSingle(issue =>
            issue.Kind == IntegrityIssueKind.Untracked &&
            issue.Action == expectedAction);
    }

    [Fact]
    public async Task VerifyAsyncRequiresMigrationWhenSourceClassificationChangesAsync()
    {
        using TestDirectory directory = new();
        string content = Path.Combine(directory.Path, "content");
        Directory.CreateDirectory(content);
        await File.WriteAllTextAsync(Path.Combine(content, "file.txt"), "content");
        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityTarget managedTarget = CreateTarget(content, ContentSourceKind.ManagedS3);
        await service.CaptureSnapshotAsync(
            CreatePaths(directory.Path),
            managedTarget,
            CancellationToken.None);
        ContentIntegrityTarget manualTarget = CreateTarget(content, ContentSourceKind.Manual);

        ContentIntegrityReport report = await service.VerifyAsync(
            CreatePaths(directory.Path),
            new[] { manualTarget },
            CancellationToken.None);

        report.Issues.Should().ContainSingle(issue =>
            issue.Kind == IntegrityIssueKind.Untracked &&
            issue.Action == IntegrityIssueAction.Absorb);
    }

    [Fact]
    public async Task ApplyCleanupAsyncDeletesConfirmedManagedExtrasAndEmptyDirectoriesAsync()
    {
        using TestDirectory directory = new();
        string content = Path.Combine(directory.Path, "content");
        string nested = Path.Combine(content, "nested");
        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(Path.Combine(nested, "unexpected.txt"), "unexpected");
        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        ContentIntegrityReport report = new(new[]
        {
            new ContentIntegrityIssue(
                target.Id,
                target.DisplayName,
                target.SourceKind,
                IntegrityIssueKind.UnexpectedFile,
                IntegrityIssueAction.Delete,
                "nested/unexpected.txt"),
        });

        await service.ApplyCleanupAsync(report, new[] { target }, CancellationToken.None);

        File.Exists(Path.Combine(nested, "unexpected.txt")).Should().BeFalse();
        Directory.Exists(nested).Should().BeFalse();
    }

    [Fact]
    public async Task ApplyCleanupAsyncDeletesConfirmedDirectoryIssueAsync()
    {
        using TestDirectory directory = new();
        string content = Path.Combine(directory.Path, "content");
        string unexpected = Path.Combine(content, "unexpected");
        Directory.CreateDirectory(unexpected);
        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        ContentIntegrityReport report = new(new[]
        {
            new ContentIntegrityIssue(
                target.Id,
                target.DisplayName,
                target.SourceKind,
                IntegrityIssueKind.EmptyDirectory,
                IntegrityIssueAction.Delete,
                "unexpected"),
        });

        await service.ApplyCleanupAsync(report, new[] { target }, CancellationToken.None);

        Directory.Exists(unexpected).Should().BeFalse();
    }

    [Fact]
    public async Task ApplyCleanupAsyncRejectsUnknownTargetAsync()
    {
        using TestDirectory directory = new();
        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityReport report = new(new[]
        {
            new ContentIntegrityIssue(
                "missing",
                "Missing",
                ContentSourceKind.ManagedS3,
                IntegrityIssueKind.UnexpectedFile,
                IntegrityIssueAction.Delete,
                "unexpected.txt"),
        });

        Func<Task> cleanup = () => service.ApplyCleanupAsync(
            report,
            Array.Empty<ContentIntegrityTarget>(),
            CancellationToken.None);

        await cleanup.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("The cleanup report references an unknown integrity target.");
    }

    [Fact]
    public async Task ApplyCleanupAsyncIgnoresMissingDeletedEntriesAsync()
    {
        using TestDirectory directory = new();
        string content = Path.Combine(directory.Path, "content");
        Directory.CreateDirectory(content);
        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        ContentIntegrityReport report = new(new[]
        {
            new ContentIntegrityIssue(
                target.Id,
                target.DisplayName,
                target.SourceKind,
                IntegrityIssueKind.UnexpectedFile,
                IntegrityIssueAction.Delete,
                "missing.txt"),
            new ContentIntegrityIssue(
                target.Id,
                target.DisplayName,
                target.SourceKind,
                IntegrityIssueKind.UnexpectedFile,
                IntegrityIssueAction.Delete,
                "missing/missing.txt"),
        });

        await service.ApplyCleanupAsync(report, new[] { target }, CancellationToken.None);

        Directory.Exists(content).Should().BeTrue();
    }

    [Fact]
    public async Task ApplyCleanupAsyncSkipsEmptyDirectorySweepWhenRootIsMissingAsync()
    {
        using TestDirectory directory = new();
        string content = Path.Combine(directory.Path, "missing-content");
        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        ContentIntegrityReport report = new(new[]
        {
            new ContentIntegrityIssue(
                target.Id,
                target.DisplayName,
                target.SourceKind,
                IntegrityIssueKind.UnexpectedFile,
                IntegrityIssueAction.Delete,
                "missing.txt"),
        });

        await service.ApplyCleanupAsync(report, new[] { target }, CancellationToken.None);

        Directory.Exists(content).Should().BeFalse();
    }

    [Fact]
    public async Task ApplyCleanupAsyncPreservesIgnoredAndNonEmptyDirectoriesAsync()
    {
        using TestDirectory directory = new();
        string content = Path.Combine(directory.Path, "content");
        string unexpected = Path.Combine(content, "unexpected");
        string ignored = Path.Combine(content, "ignored");
        string nonEmpty = Path.Combine(content, "non-empty");
        Directory.CreateDirectory(unexpected);
        Directory.CreateDirectory(ignored);
        Directory.CreateDirectory(nonEmpty);
        string unexpectedFile = Path.Combine(unexpected, "unexpected.txt");
        await File.WriteAllTextAsync(unexpectedFile, "unexpected");
        await File.WriteAllTextAsync(Path.Combine(nonEmpty, "keep.txt"), "keep");
        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityTarget target = CreateTarget(
            content,
            ContentSourceKind.ManagedS3,
            new HashSet<string>(StringComparer.Ordinal) { @"\IGNORED/" });
        ContentIntegrityReport report = new(new[]
        {
            new ContentIntegrityIssue(
                target.Id,
                target.DisplayName,
                target.SourceKind,
                IntegrityIssueKind.UnexpectedFile,
                IntegrityIssueAction.Delete,
                "unexpected/unexpected.txt"),
        });

        await service.ApplyCleanupAsync(report, new[] { target }, CancellationToken.None);

        File.Exists(unexpectedFile).Should().BeFalse();
        Directory.Exists(unexpected).Should().BeFalse();
        Directory.Exists(ignored).Should().BeTrue();
        Directory.Exists(nonEmpty).Should().BeTrue();
    }

    [SymbolicLinkFact]
    public async Task VerifyAsyncRejectsManualLinkWithoutFollowingItAsync()
    {
        using TestDirectory directory = new();
        string outsidePath = Path.Combine(directory.Path, "outside.txt");
        await File.WriteAllTextAsync(outsidePath, "outside");
        string content = Path.Combine(directory.Path, "content");
        Directory.CreateDirectory(content);
        string linkPath = Path.Combine(content, "linked.txt");
        SymbolicLinkTestSupport.CreateFileLink(linkPath, outsidePath);

        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.Manual);

        ContentIntegrityReport report = await service.VerifyAsync(
            CreatePaths(directory.Path),
            new[] { target },
            CancellationToken.None);

        report.Issues.Should().Contain(issue =>
            issue.Kind == IntegrityIssueKind.Untracked &&
            issue.Action == IntegrityIssueAction.Absorb);
        report.Issues.Should().Contain(issue =>
            issue.Kind == IntegrityIssueKind.UnsafeLink &&
            issue.Action == IntegrityIssueAction.Block &&
            issue.RelativePath == "linked.txt");
        Func<Task> capture = () => service.CaptureSnapshotAsync(
            CreatePaths(directory.Path),
            target,
            CancellationToken.None);
        await capture.Should().ThrowAsync<IOException>();
    }

    [SymbolicLinkFact]
    public async Task VerifyAsyncReportsLinkedTargetRootWithoutFollowingItAsync()
    {
        using TestDirectory directory = new();
        string outside = Path.Combine(directory.Path, "outside");
        Directory.CreateDirectory(outside);
        string outsideFile = Path.Combine(outside, "outside.txt");
        await File.WriteAllTextAsync(outsideFile, "outside");
        string content = Path.Combine(directory.Path, "content");
        SymbolicLinkTestSupport.CreateDirectoryLink(content, outside);

        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);

        ContentIntegrityReport report = await service.VerifyAsync(
            CreatePaths(directory.Path),
            new[] { target },
            CancellationToken.None);

        report.Issues.Should().Contain(issue =>
            issue.Kind == IntegrityIssueKind.UnsafeLink &&
            issue.Action == IntegrityIssueAction.Delete &&
            issue.RelativePath == ".");
        File.ReadAllText(outsideFile).Should().Be("outside");
    }

    [SymbolicLinkFact]
    public async Task ApplyCleanupAsyncDeletesLinkedTargetRootWithoutDeletingTargetAsync()
    {
        using TestDirectory directory = new();
        string outside = Path.Combine(directory.Path, "outside");
        Directory.CreateDirectory(outside);
        string outsideFile = Path.Combine(outside, "outside.txt");
        await File.WriteAllTextAsync(outsideFile, "outside");
        string content = Path.Combine(directory.Path, "content");
        SymbolicLinkTestSupport.CreateDirectoryLink(content, outside);

        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        ContentIntegrityReport report = new(new[]
        {
            new ContentIntegrityIssue(
                target.Id,
                target.DisplayName,
                target.SourceKind,
                IntegrityIssueKind.UnsafeLink,
                IntegrityIssueAction.Delete,
                "."),
        });

        await service.ApplyCleanupAsync(report, new[] { target }, CancellationToken.None);

        Directory.Exists(content).Should().BeFalse();
        File.ReadAllText(outsideFile).Should().Be("outside");
    }

    [SymbolicLinkFact]
    public async Task ApplyCleanupAsyncRejectsLinkedAncestorWithoutDeletingTargetAsync()
    {
        using TestDirectory directory = new();
        string outside = Path.Combine(directory.Path, "outside");
        Directory.CreateDirectory(outside);
        string outsideFile = Path.Combine(outside, "outside.txt");
        await File.WriteAllTextAsync(outsideFile, "outside");
        string content = Path.Combine(directory.Path, "content");
        Directory.CreateDirectory(content);
        SymbolicLinkTestSupport.CreateDirectoryLink(Path.Combine(content, "linked"), outside);

        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        ContentIntegrityReport report = new(new[]
        {
            new ContentIntegrityIssue(
                target.Id,
                target.DisplayName,
                target.SourceKind,
                IntegrityIssueKind.UnexpectedFile,
                IntegrityIssueAction.Delete,
                "linked/outside.txt"),
        });

        Func<Task> cleanup = () => service.ApplyCleanupAsync(
            report,
            new[] { target },
            CancellationToken.None);

        await cleanup.Should().ThrowAsync<InvalidDataException>();
        File.ReadAllText(outsideFile).Should().Be("outside");
    }

    [Fact]
    public async Task ApplyCleanupAsyncRejectsPathTraversalAsync()
    {
        using TestDirectory directory = new();
        string content = Path.Combine(directory.Path, "content");
        Directory.CreateDirectory(content);
        string outsideFile = Path.Combine(directory.Path, "outside.txt");
        await File.WriteAllTextAsync(outsideFile, "outside");
        FileSystemContentIntegrityService service = CreateService(directory.Path);
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        ContentIntegrityReport report = new(new[]
        {
            new ContentIntegrityIssue(
                target.Id,
                target.DisplayName,
                target.SourceKind,
                IntegrityIssueKind.UnexpectedFile,
                IntegrityIssueAction.Delete,
                "../outside.txt"),
        });

        Func<Task> cleanup = () => service.ApplyCleanupAsync(
            report,
            new[] { target },
            CancellationToken.None);

        await cleanup.Should().ThrowAsync<InvalidDataException>();
        File.ReadAllText(outsideFile).Should().Be("outside");
    }

    private static FileSystemContentIntegrityService CreateService(
        string root,
        IAtomicFileWriter? atomicFileWriter = null)
    {
        return new FileSystemContentIntegrityService(
            atomicFileWriter ?? new AtomicFileWriter(),
            NullLogger<FileSystemContentIntegrityService>.Instance);
    }

    private static LauncherPaths CreatePaths(string root)
    {
        return TestLauncherPaths.Create(Path.Combine(root, "Game"));
    }

    private static ContentIntegrityTarget CreateTarget(
        string root,
        ContentSourceKind sourceKind,
        IReadOnlySet<string>? ignoredRelativePaths = null)
    {
        return new ContentIntegrityTarget(
            "target",
            "Target",
            root,
            sourceKind,
            ignoredRelativePaths ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static string GetSnapshotPath(string snapshotDirectory, string targetId)
    {
        byte[] identifierHash = SHA256.HashData(Encoding.UTF8.GetBytes(targetId));
        return Path.Combine(snapshotDirectory, Convert.ToHexString(identifierHash) + ".json");
    }
}
