using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Features.Integrity;
using GenLauncherGO.Features.Startup;
using GenLauncherGO.Shared.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Features.Integrity;

public sealed class FileSystemContentIntegrityServiceTests
{
    [Fact]
    public async Task VerifyAsync_ReportsVerificationErrorWhenSnapshotCannotBeReadAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        Directory.CreateDirectory(paths.IntegrityDirectory);
        await File.WriteAllTextAsync(GetSnapshotPath(paths.IntegrityDirectory, "target"), "{", TestContext.Current.CancellationToken);
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
        await File.WriteAllTextAsync(GetSnapshotPath(paths.IntegrityDirectory, "target"), JsonSerializer.Serialize(snapshot), TestContext.Current.CancellationToken);
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
        await File.WriteAllTextAsync(filePath, "aaaa", TestContext.Current.CancellationToken);
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        await service.CaptureSnapshotAsync(paths, target, CancellationToken.None);
        await File.WriteAllTextAsync(filePath, "bbbb", TestContext.Current.CancellationToken);

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
        await File.WriteAllTextAsync(expectedPath, "expected", TestContext.Current.CancellationToken);
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        await service.CaptureSnapshotAsync(paths, target, CancellationToken.None);
        File.Delete(expectedPath);
        await File.WriteAllTextAsync(Path.Combine(content, "unexpected.txt"), "unexpected", TestContext.Current.CancellationToken);
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
        await File.WriteAllTextAsync(expectedPath, "expected", TestContext.Current.CancellationToken);
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedSingleFile);
        await service.CaptureSnapshotAsync(paths, target, CancellationToken.None);
        File.Delete(expectedPath);
        await File.WriteAllTextAsync(Path.Combine(content, "unexpected.txt"), "unexpected", TestContext.Current.CancellationToken);
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

    /// <summary>
    ///     Content the launcher does not manage has no trusted source to be compared against, so changing, adding, or
    ///     removing files inside it is the user's business and must not reach the launch review.
    /// </summary>
    [Theory]
    [InlineData(ContentSourceKind.Manual)]
    [InlineData(ContentSourceKind.UnknownLegacy)]
    public async Task VerifyAsync_UnmanagedContentThatChanged_ReportsNoIssuesAsync(object sourceKindValue)
    {
        var sourceKind = (ContentSourceKind)sourceKindValue;
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        string filePath = Path.Combine(content, "file.txt");
        await File.WriteAllTextAsync(filePath, "before", TestContext.Current.CancellationToken);
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget target = CreateTarget(content, sourceKind);
        await service.CaptureSnapshotAsync(paths, target, CancellationToken.None);
        await File.WriteAllTextAsync(filePath, "after", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(content, "added.txt"), "added", TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Path.Combine(content, "empty"));

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
        await File.WriteAllTextAsync(Path.Combine(content, "file.txt"), "content", TestContext.Current.CancellationToken);
        RecordingAtomicFileWriter atomicFileWriter = new();
        FileSystemContentIntegrityService service = CreateService(atomicFileWriter);
        ContentIntegrityTarget target = CreateTarget(content, ContentSourceKind.ManagedS3);
        using var cancellationTokenSource = new CancellationTokenSource();

        await service.CaptureSnapshotAsync(paths, target, cancellationTokenSource.Token);

        atomicFileWriter.CancellationToken.Should().Be(cancellationTokenSource.Token);
        atomicFileWriter.DestinationPath.Should().Be(GetSnapshotPath(paths.IntegrityDirectory, target.Id));
        ContentIntegritySnapshotDocument? snapshot =
            JsonSerializer.Deserialize<ContentIntegritySnapshotDocument>(atomicFileWriter.Contents!);
        snapshot.Should().NotBeNull();
        snapshot!.TargetId.Should().Be(target.Id);
        snapshot.Files.Should().ContainSingle().Which.RelativePath.Should().Be("file.txt");
    }

    [Fact]
    public async Task CaptureSnapshotIf_MatchesExpectedFileSetAsyncRejectsExtrasWithoutSnapshottingAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        await File.WriteAllTextAsync(Path.Combine(content, "active.png"), "active", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(content, "extra.png"), "extra", TestContext.Current.CancellationToken);
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
        await File.WriteAllTextAsync(Path.Combine(content, "active.png"), "active", TestContext.Current.CancellationToken);
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

    [Fact]
    public async Task VerifyAsync_RequiresMigrationWhenSourceClassificationChangesAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        await File.WriteAllTextAsync(Path.Combine(content, "file.txt"), "content", TestContext.Current.CancellationToken);
        FileSystemContentIntegrityService service = CreateService();
        ContentIntegrityTarget singleFileTarget = CreateTarget(content, ContentSourceKind.ManagedSingleFile);
        await service.CaptureSnapshotAsync(paths, singleFileTarget, CancellationToken.None);
        ContentIntegrityTarget s3Target = CreateTarget(content, ContentSourceKind.ManagedS3);

        ContentIntegrityReport report = await service.VerifyAsync(
            paths,
            new[] { s3Target },
            CancellationToken.None);

        report.Issues.Should().ContainSingle(issue =>
            issue.Kind == IntegrityIssueKind.Untracked &&
            issue.Action == IntegrityIssueAction.Repair);
    }

    [Fact]
    public async Task ApplyCleanupAsync_DeletesConfirmedManagedExtrasAndEmptyDirectoriesAsync()
    {
        using TestDirectory directory = new();
        string content = Path.Combine(directory.Path, "content");
        string nested = Path.Combine(content, "nested");
        string deeper = Path.Combine(nested, "deeper");
        Directory.CreateDirectory(deeper);
        await File.WriteAllTextAsync(Path.Combine(deeper, "unexpected.txt"), "unexpected", TestContext.Current.CancellationToken);
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
        await File.WriteAllTextAsync(unexpectedFile, "unexpected", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(nonEmpty, "keep.txt"), "keep", TestContext.Current.CancellationToken);
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

        // The safety scan is the whole point of still verifying unmanaged content: the link must be reported, and
        // reported without being followed, before deployment links this folder into the game directory.
        report.Issues.Should().ContainSingle(issue =>
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
        (await File.ReadAllTextAsync(outsideFile, TestContext.Current.CancellationToken)).Should().Be("outside");
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
    ///     Snapshot file names are a one-way hash of the target identifier, so retention has to be decided by hashing
    ///     the identifiers that are still live rather than by reading each stored document back.
    /// </summary>
    [Fact]
    public async Task PruneSnapshots_DeletesOnlySnapshotsOutsideTheRetainedSetAsync()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);
        string content = directory.CreateDirectory("content");
        await File.WriteAllTextAsync(
            Path.Combine(content, "file.txt"),
            "content",
            TestContext.Current.CancellationToken);
        FileSystemContentIntegrityService service = CreateService();
        await service.CaptureSnapshotAsync(paths, CreateTarget("retained", content), CancellationToken.None);
        await service.CaptureSnapshotAsync(paths, CreateTarget("orphaned", content), CancellationToken.None);

        int deletedCount = service.PruneSnapshots(
            paths,
            new HashSet<string>(StringComparer.Ordinal) { "retained" });

        deletedCount.Should().Be(1);
        File.Exists(GetSnapshotPath(paths.IntegrityDirectory, "retained")).Should().BeTrue();
        File.Exists(GetSnapshotPath(paths.IntegrityDirectory, "orphaned")).Should().BeFalse();
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

    private sealed class RecordingAtomicFileWriter : IAtomicFileWriter
    {
        public string? DestinationPath { get; private set; }
        public string? Contents { get; private set; }
        public CancellationToken? CancellationToken { get; private set; }

        public void WriteText(string destinationPath, string contents)
        {
            throw new NotSupportedException();
        }

        public async Task WriteAsync(
            string destinationPath,
            Func<Stream, CancellationToken, Task> writeTemporaryFileAsync,
            CancellationToken cancellationToken)
        {
            await using var stream = new MemoryStream();
            await writeTemporaryFileAsync(stream, cancellationToken);
            DestinationPath = destinationPath;
            Contents = Encoding.UTF8.GetString(stream.ToArray());
            CancellationToken = cancellationToken;
        }

        public Task<bool> WriteFileIfMissingAsync(
            string destinationPath,
            Func<string, CancellationToken, Task> writeTemporaryFileAsync,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
