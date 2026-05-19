using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Infrastructure.Integrity.Support;

namespace GenLauncherGO.Tests.Infrastructure.Integrity.Support;

public sealed class ContentIntegrityScannerTests
{
    /// <summary>
    ///     Content the user has not installed yet is absent, not broken, so the scan describes an empty target instead
    ///     of failing on the missing directory.
    /// </summary>
    [Fact]
    public async Task ScanAsync_MissingRootDirectory_ReturnsEmptyResultAsync()
    {
        using TestDirectory directory = new();
        ContentIntegrityTarget target = CreateTarget(directory.GetPath("missing"));

        ContentIntegrityScanResult scan = await ContentIntegrityScanner.ScanAsync(target, CancellationToken.None);

        scan.Files.Should().BeEmpty();
        scan.EmptyDirectories.Should().BeEmpty();
        scan.UnsafeLinks.Should().BeEmpty();
        scan.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanAsync_CancelledToken_ThrowsAsync()
    {
        using TestDirectory directory = new();
        string content = directory.CreateDirectory("content");
        directory.CreateFile("content/file.txt", "content");
        ContentIntegrityTarget target = CreateTarget(content);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> scan = () => ContentIntegrityScanner.ScanAsync(target, cancellation.Token);

        await scan.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    ///     Only a directory that actually holds nothing is an unexpected empty directory. A parent that merely contains
    ///     one would otherwise be reported alongside every leaf below it.
    /// </summary>
    [Fact]
    public async Task ScanAsync_NestedDirectoryWithEntries_ReportsOnlyTheEmptyLeafAsync()
    {
        using TestDirectory directory = new();
        string content = directory.CreateDirectory("content");
        directory.CreateFile("content/nested/file.txt", "file");
        directory.CreateDirectory("content/nested/empty");
        ContentIntegrityTarget target = CreateTarget(content);

        ContentIntegrityScanResult scan = await ContentIntegrityScanner.ScanAsync(target, CancellationToken.None);

        scan.EmptyDirectories.Should().ContainSingle().Which.Should().Be("nested/empty");
    }

    /// <summary>
    ///     A link is one unsafe entry: reporting it twice would hand the cleanup a delete it cannot repeat, and
    ///     descending into it would read and trust whatever the link points at.
    /// </summary>
    [Fact]
    public async Task ScanAsync_LinkedDirectory_ReportsUnsafeLinkOnceAsync()
    {
        using TestDirectory directory = new();
        string content = directory.CreateDirectory("content");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(
            directory,
            Path.Combine(content, "linked"));
        ContentIntegrityTarget target = CreateTarget(content);

        ContentIntegrityScanResult scan = await ContentIntegrityScanner.ScanAsync(target, CancellationToken.None);

        scan.UnsafeLinks.Should().ContainSingle().Which.Should().Be("linked");
        scan.Files.Should().BeEmpty();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    /// <summary>
    ///     A file the scan cannot read is not a file it can vouch for, so it becomes a reported error rather than an
    ///     entry that silently disappears from the scanned set.
    /// </summary>
    [Fact]
    public async Task ScanAsync_UnreadableFile_ReportsScanErrorAsync()
    {
        using TestDirectory directory = new();
        string content = directory.CreateDirectory("content");
        string lockedPath = directory.CreateFile("content/locked.bin", "locked");
        ContentIntegrityTarget target = CreateTarget(content);
        using FileStream exclusiveHandle = new(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);

        ContentIntegrityScanResult scan = await ContentIntegrityScanner.ScanAsync(target, CancellationToken.None);

        scan.Errors.Should().ContainSingle().Which.RelativePath.Should().Be("locked.bin");
        scan.Files.Should().BeEmpty();
    }

    /// <summary>
    ///     A directory the launcher cannot list is content it cannot account for. Reporting the directory as an error
    ///     is what keeps it from looking like an empty folder whose files have all gone missing.
    /// </summary>
    [Fact]
    public async Task ScanAsync_UnreadableDirectory_ReportsScanErrorAsync()
    {
        using TestDirectory directory = new();
        string content = directory.CreateDirectory("content");
        string unreadablePath = directory.CreateDirectory("content/unreadable");
        ContentIntegrityTarget target = CreateTarget(content);
        using DeniedDirectoryListing deniedListing = new(unreadablePath);

        ContentIntegrityScanResult scan = await ContentIntegrityScanner.ScanAsync(target, CancellationToken.None);

        scan.Errors.Should().ContainSingle().Which.RelativePath.Should().Be("unreadable");
        scan.EmptyDirectories.Should().BeEmpty();
    }

    private static ContentIntegrityTarget CreateTarget(string root)
    {
        return new ContentIntegrityTarget(
            "target",
            "Target",
            root,
            ContentSourceKind.ManagedS3,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>
///     Denies the current account permission to list one directory for the duration of a test, which is the only way
///     to make a directory enumeration fail on demand without elevation. The deny entry is removed on disposal so the
///     owning temporary directory can still be enumerated and deleted.
/// </summary>
internal sealed class DeniedDirectoryListing : IDisposable
{
    private readonly FileSystemAccessRule _denyRule;

    private readonly DirectoryInfo _directory;

    public DeniedDirectoryListing(string path)
    {
        using var identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User
                                  ?? throw new InvalidOperationException("The current account has no user SID.");

        _directory = new DirectoryInfo(path);
        _denyRule = new FileSystemAccessRule(user, FileSystemRights.ListDirectory, AccessControlType.Deny);
        ChangeAccess(security => security.AddAccessRule(_denyRule));
    }

    public void Dispose()
    {
        ChangeAccess(security => security.RemoveAccessRule(_denyRule));
    }

    private void ChangeAccess(Action<DirectorySecurity> change)
    {
        DirectorySecurity security = _directory.GetAccessControl();
        change(security);
        _directory.SetAccessControl(security);
    }
}
