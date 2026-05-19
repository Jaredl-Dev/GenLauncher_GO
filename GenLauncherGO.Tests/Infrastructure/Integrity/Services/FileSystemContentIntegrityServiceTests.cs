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
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Integrity.Services;

public sealed class FileSystemContentIntegrityServiceTests
{
    [Fact]
    public async Task VerifyAsync_ReportsVerificationErrorWhenSnapshotCannotBeReadAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        Directory.CreateDirectory(paths.IntegrityDirectory);
        await File.WriteAllTextAsync(GetSnapshotPath(paths.IntegrityDirectory, "target"), "{");
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);

        ContentIntegrityReport report = await service.VerifyAsync(
            paths,
            new[] { target },
            CancellationToken.None);

        report.Issues.Should().ContainSingle(issue =>
            issue.Kind == IntegrityIssueKind.VerificationError &&
            issue.Action == IntegrityIssueAction.Block &&
            issue.RelativePath == "." &&
            !string.IsNullOrWhiteSpace(issue.Message));
    }

    /// <summary>
    ///     A snapshot the launcher cannot prove it owns is as untrustworthy as an unreadable one: a future schema may
    ///     have changed what the fields mean, and another target's document describes different content.
    /// </summary>
    [Theory]
    [InlineData(2, "target")]
    [InlineData(1, "other-target")]
    public async Task VerifyAsync_SnapshotWithUnsupportedSchemaOrOwner_ReportsVerificationErrorAsync(
        int schemaVersion,
        string snapshotTargetId)
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        Directory.CreateDirectory(paths.IntegrityDirectory);
        ContentIntegritySnapshotDocument snapshot = new(
            schemaVersion,
            snapshotTargetId,
            ContentSourceKind.ManagedS3,
            [],
            []);
        await File.WriteAllTextAsync(
            GetSnapshotPath(paths.IntegrityDirectory, "target"),
            JsonSerializer.Serialize(snapshot));
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);

        ContentIntegrityReport report = await service.VerifyAsync(
            paths,
            new[] { target },
            CancellationToken.None);

        report.Issues.Should().ContainSingle(issue =>
            issue.Kind == IntegrityIssueKind.VerificationError &&
            issue.Action == IntegrityIssueAction.Block &&
            issue.RelativePath == ".");
    }

    [Fact]
    public async Task VerifyAsync_DetectsSameSizeSha256ModificationAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        string filePath = Path.Combine(content, "file.bin");
        await File.WriteAllTextAsync(filePath, "aaaa");
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        await service.CaptureSnapshotAsync(paths, target, CancellationToken.None);
        await File.WriteAllTextAsync(filePath, "bbbb");

        ContentIntegrityReport report = await service.VerifyAsync(
            paths,
            new[] { target },
            CancellationToken.None);

        report.Issues.Should().ContainSingle(issue =>
            issue.Kind == IntegrityIssueKind.ModifiedFile &&
            issue.Action == IntegrityIssueAction.Repair &&
            issue.RelativePath == "file.bin" &&
            issue.ExpectedSizeBytes == 4);
    }

    [Fact]
    public async Task VerifyAsync_CollectsMissingUnexpectedAndEmptyDirectoryIssuesAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        string expectedPath = Path.Combine(content, "expected.txt");
        await File.WriteAllTextAsync(expectedPath, "expected");
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        await service.CaptureSnapshotAsync(paths, target, CancellationToken.None);
        File.Delete(expectedPath);
        await File.WriteAllTextAsync(Path.Combine(content, "unexpected.txt"), "unexpected");
        Directory.CreateDirectory(Path.Combine(content, "nested", "empty"));

        ContentIntegrityReport report = await service.VerifyAsync(
            paths,
            new[] { target },
            CancellationToken.None);

        report.Issues.Should().Contain(issue =>
            issue.Kind == IntegrityIssueKind.MissingFile &&
            issue.ExpectedSizeBytes == 8);
        report.Issues.Select(issue => issue.Kind).Should().Contain(IntegrityIssueKind.UnexpectedFile);
        report.Issues.Select(issue => issue.Kind).Should().Contain(IntegrityIssueKind.EmptyDirectory);
    }

    [Fact]
    public async Task VerifyAsyncAlways_ReportsManagedEmptyDirectoriesAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = Path.Combine(directory.Path, "content");
        Directory.CreateDirectory(Path.Combine(content, "nested", "empty"));
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        await service.CaptureSnapshotAsync(paths, target, CancellationToken.None);

        ContentIntegrityReport report = await service.VerifyAsync(
            paths,
            new[] { target },
            CancellationToken.None);

        report.Issues.Should().ContainSingle(issue =>
            issue.Kind == IntegrityIssueKind.EmptyDirectory &&
            issue.Action == IntegrityIssueAction.Delete &&
            issue.RelativePath == "nested/empty");
    }

    [Fact]
    public async Task VerifyAsync_ClassifiesManagedSingleFileDifferencesAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        string expectedPath = Path.Combine(content, "expected.txt");
        await File.WriteAllTextAsync(expectedPath, "expected");
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedSingleFile);
        await service.CaptureSnapshotAsync(paths, target, CancellationToken.None);
        File.Delete(expectedPath);
        await File.WriteAllTextAsync(Path.Combine(content, "unexpected.txt"), "unexpected");
        Directory.CreateDirectory(Path.Combine(content, "empty"));

        ContentIntegrityReport report = await service.VerifyAsync(
            paths,
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
    public async Task VerifyAsync_ClassifiesUnknownLegacyDifferencesForManualTrustAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        string filePath = Path.Combine(content, "file.txt");
        await File.WriteAllTextAsync(filePath, "before");
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.UnknownLegacy);
        await service.CaptureSnapshotAsync(paths, target, CancellationToken.None);
        await File.WriteAllTextAsync(filePath, "after");
        await File.WriteAllTextAsync(Path.Combine(content, "added.txt"), "added");

        ContentIntegrityReport report = await service.VerifyAsync(
            paths,
            new[] { target },
            CancellationToken.None);

        report.Issues.Should().NotBeEmpty();
        report.Issues.Should().OnlyContain(issue => issue.Action == IntegrityIssueAction.TrustAsManual);
    }

    [Fact]
    public async Task VerifyAsync_MarksManualDifferencesForAbsorptionAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        await File.WriteAllTextAsync(Path.Combine(content, "file.txt"), "before");
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.Manual);
        await service.CaptureSnapshotAsync(paths, target, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(content, "file.txt"), "after");
        await File.WriteAllTextAsync(Path.Combine(content, "added.txt"), "added");

        ContentIntegrityReport report = await service.VerifyAsync(
            paths,
            new[] { target },
            CancellationToken.None);

        report.Issues.Should().NotBeEmpty()
            .And.OnlyContain(issue => issue.Action == IntegrityIssueAction.Absorb);
    }

    /// <summary>
    ///     Manual content is where a user's own empty folders live, so a snapshot has to record them or every later
    ///     verification would ask to absorb the same folders again.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_ManualSnapshotWithEmptyDirectory_ReportsNoIssuesAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        await File.WriteAllTextAsync(Path.Combine(content, "file.txt"), "content");
        Directory.CreateDirectory(Path.Combine(content, "empty"));
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.Manual);
        await service.CaptureSnapshotAsync(paths, target, CancellationToken.None);

        ContentIntegrityReport report = await service.VerifyAsync(
            paths,
            new[] { target },
            CancellationToken.None);

        report.HasIssues.Should().BeFalse();
    }

    [Fact]
    public async Task CaptureSnapshotAsync_AbsorbsManualDifferencesAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        string filePath = Path.Combine(content, "file.txt");
        await File.WriteAllTextAsync(filePath, "before");
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.Manual);
        await service.CaptureSnapshotAsync(paths, target, CancellationToken.None);
        await File.WriteAllTextAsync(filePath, "after");

        await service.CaptureSnapshotAsync(paths, target, CancellationToken.None);
        ContentIntegrityReport report = await service.VerifyAsync(
            paths,
            new[] { target },
            CancellationToken.None);

        report.HasIssues.Should().BeFalse();
    }

    [Fact]
    public async Task CaptureSnapshotAsync_CommitsCompleteDocumentThroughAtomicWriterAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        await File.WriteAllTextAsync(Path.Combine(content, "file.txt"), "content");
        RecordingAtomicFileWriter atomicFileWriter = new();
        FileSystemContentIntegrityService service = CreateService(atomicFileWriter);
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        using var cancellationTokenSource = new CancellationTokenSource();

        await service.CaptureSnapshotAsync(paths, target, cancellationTokenSource.Token);

        atomicFileWriter.WasWriteAsyncCalled.Should().BeTrue();
        atomicFileWriter.CancellationToken.Should().Be(cancellationTokenSource.Token);
        atomicFileWriter.DestinationPath.Should().Be(GetSnapshotPath(paths.IntegrityDirectory, target.Id));
        ContentIntegritySnapshotDocument? snapshot =
            JsonSerializer.Deserialize<ContentIntegritySnapshotDocument>(atomicFileWriter.Contents!);
        snapshot.Should().NotBeNull();
        snapshot!.TargetId.Should().Be(target.Id);
        snapshot.Files.Should().ContainSingle().Which.RelativePath.Should().Be("file.txt");
    }

    /// <summary>
    ///     A later verification compares this document against a fresh scan, so the store orders its entries
    ///     itself rather than persisting whatever order the filesystem happened to enumerate.
    /// </summary>
    [Fact]
    public async Task CaptureSnapshotAsync_OrdersEntriesIndependentlyOfEnumerationAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        await File.WriteAllTextAsync(Path.Combine(content, "zeta.txt"), "z");
        await File.WriteAllTextAsync(Path.Combine(content, "alpha.txt"), "a");
        Directory.CreateDirectory(Path.Combine(content, "zulu"));
        Directory.CreateDirectory(Path.Combine(content, "bravo"));
        RecordingAtomicFileWriter atomicFileWriter = new();
        FileSystemContentIntegrityService service = CreateService(atomicFileWriter);
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.Manual);

        await service.CaptureSnapshotAsync(paths, target, CancellationToken.None);

        ContentIntegritySnapshotDocument snapshot =
            JsonSerializer.Deserialize<ContentIntegritySnapshotDocument>(atomicFileWriter.Contents!)!;
        snapshot.Files.Select(file => file.RelativePath).Should().Equal("alpha.txt", "zeta.txt");
        snapshot.EmptyDirectories.Should().Equal("bravo", "zulu");
    }

    [Fact]
    public async Task IntegritySnapshots_GameNamespaceChange_SwitchWithoutServiceRebuildAsync()
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
    public async Task VerifyAsync_PreservesIgnoredInactiveCacheFileAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        await File.WriteAllTextAsync(Path.Combine(content, "active.png"), "active");
        await File.WriteAllTextAsync(Path.Combine(content, "inactive.png"), "inactive");
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = new(
            "target",
            "Target",
            content,
            ContentSourceKind.ManagedS3,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "inactive.png" });
        await service.CaptureSnapshotAsync(paths, target, CancellationToken.None);

        ContentIntegrityReport report = await service.VerifyAsync(
            paths,
            new[] { target },
            CancellationToken.None);

        report.HasIssues.Should().BeFalse();
    }

    [Fact]
    public async Task CaptureSnapshotIf_MatchesExpectedFileSetAsyncCapturesExistingManagedCacheWithoutMutationAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        string activePath = Path.Combine(content, "active.png");
        string inactivePath = Path.Combine(content, "inactive.png");
        await File.WriteAllTextAsync(activePath, "active");
        await File.WriteAllTextAsync(inactivePath, "inactive");
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = new(
            "target",
            "Target",
            content,
            ContentSourceKind.ManagedS3,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "inactive.png" });

        bool captured = await service.CaptureSnapshotIfMatchesExpectedFileSetAsync(
            paths,
            target,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "active.png" },
            CancellationToken.None);
        ContentIntegrityReport report = await service.VerifyAsync(
            paths,
            new[] { target },
            CancellationToken.None);

        captured.Should().BeTrue();
        report.HasIssues.Should().BeFalse();
        (await File.ReadAllTextAsync(activePath)).Should().Be("active");
        (await File.ReadAllTextAsync(inactivePath)).Should().Be("inactive");
    }

    [Fact]
    public async Task CaptureSnapshotIf_MatchesExpectedFileSetAsyncRejectsExtrasWithoutSnapshottingAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        await File.WriteAllTextAsync(Path.Combine(content, "active.png"), "active");
        await File.WriteAllTextAsync(Path.Combine(content, "extra.png"), "extra");
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);

        bool captured = await service.CaptureSnapshotIfMatchesExpectedFileSetAsync(
            paths,
            target,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "active.png" },
            CancellationToken.None);
        ContentIntegrityReport report = await service.VerifyAsync(
            paths,
            new[] { target },
            CancellationToken.None);

        captured.Should().BeFalse();
        report.Issues.Should().ContainSingle(issue =>
            issue.Kind == IntegrityIssueKind.Untracked &&
            issue.Action == IntegrityIssueAction.Repair);
    }

    /// <summary>
    ///     The expected file set describes files only, so a directory entry the manifest never mentioned still means the
    ///     content is not the package the launcher would be vouching for.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureSnapshotIf_UnexpectedDirectoryEntry_LeavesTargetUntrackedAsync(bool linkedEntry)
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        await File.WriteAllTextAsync(Path.Combine(content, "active.png"), "active");
        string unexpectedEntryPath = Path.Combine(content, "unexpected");
        if (linkedEntry)
        {
            ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, unexpectedEntryPath);
        }
        else
        {
            Directory.CreateDirectory(unexpectedEntryPath);
        }

        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);

        bool captured = await service.CaptureSnapshotIfMatchesExpectedFileSetAsync(
            paths,
            target,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "active.png" },
            CancellationToken.None);
        ContentIntegrityReport report = await service.VerifyAsync(
            paths,
            new[] { target },
            CancellationToken.None);

        captured.Should().BeFalse();
        report.Issues.Should().Contain(issue =>
            issue.Kind == IntegrityIssueKind.Untracked &&
            issue.Action == IntegrityIssueAction.Repair);
    }

    [Fact]
    public async Task VerifyAsync_ReportsIgnoredUnsafeLinkWithoutFollowingItAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(
            directory,
            Path.Combine(content, "inactive.png"));
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = new(
            "target",
            "Target",
            content,
            ContentSourceKind.ManagedS3,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "inactive.png" });

        ContentIntegrityReport report = await service.VerifyAsync(
            paths,
            new[] { target },
            CancellationToken.None);

        report.Issues.Should().Contain(issue =>
            issue.Kind == IntegrityIssueKind.UnsafeLink &&
            issue.Action == IntegrityIssueAction.Delete &&
            issue.RelativePath == "inactive.png");
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    [Theory]
    [InlineData(ContentSourceKind.ManagedS3, IntegrityIssueAction.Repair)]
    [InlineData(ContentSourceKind.ManagedSingleFile, IntegrityIssueAction.Redownload)]
    [InlineData(ContentSourceKind.Manual, IntegrityIssueAction.Absorb)]
    [InlineData(ContentSourceKind.UnknownLegacy, IntegrityIssueAction.TrustAsManual)]
    public async Task VerifyAsync_ClassifiesUntrackedContentBySourceAsync(
        ContentSourceKind sourceKind,
        IntegrityIssueAction expectedAction)
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, sourceKind);

        ContentIntegrityReport report = await service.VerifyAsync(
            paths,
            new[] { target },
            CancellationToken.None);

        report.Issues.Should().ContainSingle(issue =>
            issue.Kind == IntegrityIssueKind.Untracked &&
            issue.Action == expectedAction);
    }

    [Fact]
    public async Task VerifyAsync_RequiresMigrationWhenSourceClassificationChangesAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        await File.WriteAllTextAsync(Path.Combine(content, "file.txt"), "content");
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget managedTarget = CreateTarget(content, ContentSourceKind.ManagedS3);
        await service.CaptureSnapshotAsync(paths, managedTarget, CancellationToken.None);
        ContentIntegrityTarget manualTarget = CreateTarget(content, ContentSourceKind.Manual);

        ContentIntegrityReport report = await service.VerifyAsync(
            paths,
            new[] { manualTarget },
            CancellationToken.None);

        report.Issues.Should().ContainSingle(issue =>
            issue.Kind == IntegrityIssueKind.Untracked &&
            issue.Action == IntegrityIssueAction.Absorb);
    }

    [Fact]
    public async Task ApplyCleanupAsync_DeletesConfirmedManagedExtrasAndEmptyDirectoriesAsync()
    {
        using TestDirectory directory = new();
        string content = Path.Combine(directory.Path, "content");
        string nested = Path.Combine(content, "nested");
        string deeper = Path.Combine(nested, "deeper");
        Directory.CreateDirectory(deeper);
        await File.WriteAllTextAsync(Path.Combine(deeper, "unexpected.txt"), "unexpected");
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        ContentIntegrityReport report = new(new[]
        {
            CreateDeleteIssue(target, "nested/deeper/unexpected.txt")
        });

        await service.ApplyCleanupAsync(report, new[] { target }, CancellationToken.None);

        File.Exists(Path.Combine(deeper, "unexpected.txt")).Should().BeFalse();
        Directory.Exists(deeper).Should().BeFalse();
        Directory.Exists(nested).Should().BeFalse();
    }

    [Fact]
    public async Task ApplyCleanupAsync_DeletesConfirmedDirectoryIssueAsync()
    {
        using TestDirectory directory = new();
        string content = Path.Combine(directory.Path, "content");
        string unexpected = Path.Combine(content, "unexpected");
        Directory.CreateDirectory(unexpected);
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        ContentIntegrityReport report = new(new[]
        {
            CreateDeleteIssue(target, "unexpected", IntegrityIssueKind.EmptyDirectory)
        });

        await service.ApplyCleanupAsync(report, new[] { target }, CancellationToken.None);

        Directory.Exists(unexpected).Should().BeFalse();
    }

    [Fact]
    public async Task ApplyCleanupAsync_RejectsUnknownTargetAsync()
    {
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityReport report = new(new[]
        {
            new ContentIntegrityIssue(
                "missing",
                "Missing",
                ContentSourceKind.ManagedS3,
                IntegrityIssueKind.UnexpectedFile,
                IntegrityIssueAction.Delete,
                "unexpected.txt")
        });

        Func<Task> cleanup = () => service.ApplyCleanupAsync(
            report,
            Array.Empty<ContentIntegrityTarget>(),
            CancellationToken.None);

        await cleanup.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("The cleanup report references an unknown integrity target.");
    }

    [Fact]
    public async Task ApplyCleanupAsync_IgnoresMissingDeletedEntriesAsync()
    {
        using TestDirectory directory = new();
        string content = directory.CreateDirectory("content");
        FileSystemContentIntegrityService service = CreateService();
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
            CreateDeleteIssue(target, "missing/missing.txt")
        });

        await service.ApplyCleanupAsync(report, new[] { target }, CancellationToken.None);

        Directory.Exists(content).Should().BeTrue();
    }

    [Fact]
    public async Task ApplyCleanupAsync_SkipsEmptyDirectorySweepWhenRootIsMissingAsync()
    {
        using TestDirectory directory = new();
        string content = Path.Combine(directory.Path, "missing-content");
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        ContentIntegrityReport report = new(new[]
        {
            CreateDeleteIssue(target, "missing.txt")
        });

        await service.ApplyCleanupAsync(report, new[] { target }, CancellationToken.None);

        Directory.Exists(content).Should().BeFalse();
    }

    [Fact]
    public async Task ApplyCleanupAsync_PreservesIgnoredAndNonEmptyDirectoriesAsync()
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
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(
            content,
            ContentSourceKind.ManagedS3,
            new HashSet<string>(StringComparer.Ordinal) { @"\IGNORED/" });
        ContentIntegrityReport report = new(new[]
        {
            CreateDeleteIssue(target, "unexpected/unexpected.txt")
        });

        await service.ApplyCleanupAsync(report, new[] { target }, CancellationToken.None);

        File.Exists(unexpectedFile).Should().BeFalse();
        Directory.Exists(unexpected).Should().BeFalse();
        Directory.Exists(ignored).Should().BeTrue();
        Directory.Exists(nonEmpty).Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAsync_RejectsManualLinkWithoutFollowingItAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(
            directory,
            Path.Combine(content, "linked.txt"));
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.Manual);

        ContentIntegrityReport report = await service.VerifyAsync(
            paths,
            new[] { target },
            CancellationToken.None);

        report.Issues.Should().Contain(issue =>
            issue.Kind == IntegrityIssueKind.Untracked &&
            issue.Action == IntegrityIssueAction.Absorb);
        report.Issues.Should().Contain(issue =>
            issue.Kind == IntegrityIssueKind.UnsafeLink &&
            issue.Action == IntegrityIssueAction.Block &&
            issue.RelativePath == "linked.txt");
        Func<Task> capture = () => service.CaptureSnapshotAsync(paths, target, CancellationToken.None);
        await capture.Should().ThrowAsync<IOException>();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    [Fact]
    public async Task VerifyAsync_ReportsLinkedTargetRootWithoutFollowingItAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.GetPath("content");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, content);
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);

        ContentIntegrityReport report = await service.VerifyAsync(
            paths,
            new[] { target },
            CancellationToken.None);

        report.Issues.Should().Contain(issue =>
            issue.Kind == IntegrityIssueKind.UnsafeLink &&
            issue.Action == IntegrityIssueAction.Delete &&
            issue.RelativePath == ".");
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    [Fact]
    public async Task ApplyCleanupAsync_DeletesLinkedTargetRootWithoutDeletingTargetAsync()
    {
        using TestDirectory directory = new();
        string content = directory.GetPath("content");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, content);
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        ContentIntegrityReport report = new(new[]
        {
            CreateDeleteIssue(target, ".", IntegrityIssueKind.UnsafeLink)
        });

        await service.ApplyCleanupAsync(report, new[] { target }, CancellationToken.None);

        Directory.Exists(content).Should().BeFalse();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    /// <summary>
    ///     A linked target root is the one arrangement where every issue path already crosses the link, so the cleanup
    ///     has to refuse the whole report instead of sweeping directories inside somebody else's folder.
    /// </summary>
    [Fact]
    public async Task ApplyCleanupAsync_LinkedTargetRoot_RejectsWithoutTouchingTargetAsync()
    {
        using TestDirectory directory = new();
        string content = directory.GetPath("content");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, content);
        string targetEmptyDirectory = directory.CreateDirectory("ExternalTarget/empty");
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        ContentIntegrityReport report = new(new[]
        {
            CreateDeleteIssue(target, "target.txt")
        });

        Func<Task> cleanup = () => service.ApplyCleanupAsync(
            report,
            new[] { target },
            CancellationToken.None);

        await cleanup.Should().ThrowAsync<InvalidDataException>();
        Directory.Exists(content).Should().BeTrue();
        Directory.Exists(targetEmptyDirectory).Should().BeTrue();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    [Fact]
    public async Task ApplyCleanupAsync_RejectsLinkedAncestorWithoutDeletingTargetAsync()
    {
        using TestDirectory directory = new();
        string content = directory.CreateDirectory("content");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(
            directory,
            Path.Combine(content, "linked"));
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        ContentIntegrityReport report = new(new[]
        {
            CreateDeleteIssue(target, "linked/target.txt")
        });

        Func<Task> cleanup = () => service.ApplyCleanupAsync(
            report,
            new[] { target },
            CancellationToken.None);

        await cleanup.Should().ThrowAsync<InvalidDataException>();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    [Fact]
    public async Task ApplyCleanupAsync_RejectsPathTraversalAsync()
    {
        using TestDirectory directory = new();
        string content = directory.CreateDirectory("content");
        string outsideFile = directory.CreateFile("outside.txt", "outside");
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        ContentIntegrityReport report = new(new[]
        {
            CreateDeleteIssue(target, "../outside.txt")
        });

        Func<Task> cleanup = () => service.ApplyCleanupAsync(
            report,
            new[] { target },
            CancellationToken.None);

        await cleanup.Should().ThrowAsync<InvalidDataException>();
        (await File.ReadAllTextAsync(outsideFile)).Should().Be("outside");
    }

    /// <summary>
    ///     A snapshot proves what the content was when it was captured, not that nothing has been linked into it
    ///     since, so tracked content is checked for unsafe links exactly like untracked content is.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_LinkAddedAfterSnapshot_ReportsUnsafeLinkAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        await File.WriteAllTextAsync(Path.Combine(content, "file.txt"), "content");
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        await service.CaptureSnapshotAsync(paths, target, CancellationToken.None);
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(
            directory,
            Path.Combine(content, "linked"));

        ContentIntegrityReport report = await service.VerifyAsync(
            paths,
            new[] { target },
            CancellationToken.None);

        report.Issues.Should().Contain(issue =>
            issue.Kind == IntegrityIssueKind.UnsafeLink &&
            issue.Action == IntegrityIssueAction.Delete &&
            issue.RelativePath == "linked");
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    /// <summary>
    ///     A file the launcher cannot read is content it cannot vouch for, so verification blocks on it by name
    ///     instead of treating the unreadable entry as if it were simply missing.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_UnreadableFile_ReportsVerificationErrorAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        string lockedPath = directory.CreateFile("content/locked.bin", "locked");
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        await service.CaptureSnapshotAsync(paths, target, CancellationToken.None);
        using FileStream exclusiveHandle = new(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);

        ContentIntegrityReport report = await service.VerifyAsync(
            paths,
            new[] { target },
            CancellationToken.None);

        report.Issues.Should().Contain(issue =>
            issue.Kind == IntegrityIssueKind.VerificationError &&
            issue.Action == IntegrityIssueAction.Block &&
            issue.RelativePath == "locked.bin");
    }

    [Fact]
    public async Task ApplyCleanupAsync_CancelledToken_KeepsConfirmedEntryAsync()
    {
        using TestDirectory directory = new();
        string content = directory.CreateDirectory("content");
        string unexpectedPath = directory.CreateFile("content/unexpected.txt", "unexpected");
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        ContentIntegrityReport report = new(new[] { CreateDeleteIssue(target, "unexpected.txt") });
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> cleanup = () => service.ApplyCleanupAsync(
            report,
            new[] { target },
            cancellation.Token);

        await cleanup.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(unexpectedPath).Should().BeTrue();
    }

    /// <summary>
    ///     The empty-directory sweep is the one part of cleanup that deletes entries no issue named, so it runs only
    ///     inside the targets the user actually confirmed deletions for.
    /// </summary>
    [Fact]
    public async Task ApplyCleanupAsync_TargetWithoutDeleteIssues_KeepsEmptyDirectoriesAsync()
    {
        using TestDirectory directory = new();
        string cleanedContent = directory.CreateDirectory("cleaned");
        string untouchedContent = directory.CreateDirectory("untouched");
        directory.CreateFile("cleaned/nested/unexpected.txt", "unexpected");
        string untouchedEmptyDirectory = directory.CreateDirectory("untouched/empty");
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget cleanedTarget = CreateTarget("cleaned", cleanedContent);
        ContentIntegrityTarget untouchedTarget = CreateTarget("untouched", untouchedContent);
        ContentIntegrityReport report = new(new[]
        {
            CreateDeleteIssue(cleanedTarget, "nested/unexpected.txt")
        });

        await service.ApplyCleanupAsync(
            report,
            new[] { cleanedTarget, untouchedTarget },
            CancellationToken.None);

        Directory.Exists(Path.Combine(cleanedContent, "nested")).Should().BeFalse();
        Directory.Exists(untouchedEmptyDirectory).Should().BeTrue();
    }

    [Fact]
    public async Task ApplyCleanupAsync_MultipleTargets_SweepsEveryTargetWithDeletionsAsync()
    {
        using TestDirectory directory = new();
        string firstContent = directory.CreateDirectory("first");
        string secondContent = directory.CreateDirectory("second");
        directory.CreateFile("first/nested/unexpected.txt", "unexpected");
        directory.CreateFile("second/nested/unexpected.txt", "unexpected");
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget firstTarget = CreateTarget("first", firstContent);
        ContentIntegrityTarget secondTarget = CreateTarget("second", secondContent);
        ContentIntegrityReport report = new(new[]
        {
            CreateDeleteIssue(firstTarget, "nested/unexpected.txt"),
            CreateDeleteIssue(secondTarget, "nested/unexpected.txt")
        });

        await service.ApplyCleanupAsync(
            report,
            new[] { firstTarget, secondTarget },
            CancellationToken.None);

        Directory.Exists(Path.Combine(firstContent, "nested")).Should().BeFalse();
        Directory.Exists(Path.Combine(secondContent, "nested")).Should().BeFalse();
    }

    private static LauncherPaths CreatePaths(TestDirectory directory)
    {
        return TestLauncherPaths.Create(Path.Combine(directory.Path, "Game"));
    }

    private static FileSystemContentIntegrityService CreateService(
        IAtomicFileWriter? atomicFileWriter = null)
    {
        return new FileSystemContentIntegrityService(
            atomicFileWriter ?? new AtomicFileWriter(),
            NullLogger<FileSystemContentIntegrityService>.Instance);
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

    private static ContentIntegrityTarget CreateTarget(string id, string root)
    {
        return new ContentIntegrityTarget(
            id,
            id,
            root,
            ContentSourceKind.ManagedS3,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static ContentIntegrityIssue CreateDeleteIssue(
        ContentIntegrityTarget target,
        string relativePath,
        IntegrityIssueKind kind = IntegrityIssueKind.UnexpectedFile)
    {
        return new ContentIntegrityIssue(
            target.Id,
            target.DisplayName,
            target.SourceKind,
            kind,
            IntegrityIssueAction.Delete,
            relativePath);
    }

    private static string GetSnapshotPath(string snapshotDirectory, string targetId)
    {
        byte[] identifierHash = SHA256.HashData(Encoding.UTF8.GetBytes(targetId));
        return Path.Combine(snapshotDirectory, Convert.ToHexString(identifierHash) + ".json");
    }
}
