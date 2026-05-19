using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Support;

public sealed class S3ReusablePackageFileCopierTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task CopyUnchangedFilesAsync_RejectsLinkedSourceOrDestinationTreeAsync(
        bool sourceIsUnsafe,
        bool linkedOwner)
    {
        using TestDirectory testDirectory = new();
        string sourceOwner = testDirectory.CreateDirectory("SourceOwner");
        string sourcePath = testDirectory.CreateDirectory("SourceOwner/Latest");
        string destinationOwner = testDirectory.CreateDirectory("DestinationOwner");
        string destinationPath = testDirectory.CreateDirectory("DestinationOwner/Staging");
        string unsafeOwner = sourceIsUnsafe ? sourceOwner : destinationOwner;
        string unsafePath = sourceIsUnsafe ? sourcePath : destinationPath;

        if (linkedOwner)
        {
            Directory.Delete(unsafeOwner, true);
            string ownerTarget = testDirectory.CreateDirectory("LinkedOwnerTarget");
            Directory.CreateDirectory(Path.Combine(ownerTarget, Path.GetFileName(unsafePath)));
            ReparsePointTestSupport.CreateDirectoryJunction(unsafeOwner, ownerTarget);
        }
        else
        {
            string linkTarget = testDirectory.CreateDirectory("LinkedChildTarget");
            ReparsePointTestSupport.CreateDirectoryJunction(Path.Combine(unsafePath, "Linked"), linkTarget);
        }

        S3ReusablePackageFileCopier copier = CreateReusableFileCopier(new StubFileHashService());
        string expectedMessage = sourceIsUnsafe
            ? "Reusable package paths must not contain reparse points."
            : "Package staging paths must not contain reparse points.";

        Func<Task> copy = () => copier.CopyUnchangedFilesAsync(
            new OwnedContentPath(sourceOwner, sourcePath),
            new OwnedContentPath(destinationOwner, destinationPath),
            Array.Empty<RemoteFileManifestEntry>(),
            CancellationToken.None);

        await copy.Should().ThrowAsync<InvalidDataException>()
            .WithMessage(expectedMessage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CopyUnchangedFilesAsync_CancellationBeforeFileOrDirectoryWork_StopsAsync(
        bool sourceContainsFile)
    {
        using TestDirectory testDirectory = new();
        string sourceOwner = testDirectory.CreateDirectory("SourceOwner");
        string sourcePath = testDirectory.CreateDirectory("SourceOwner/Latest");
        string destinationOwner = testDirectory.CreateDirectory("DestinationOwner");
        string destinationPath = testDirectory.CreateDirectory("DestinationOwner/Staging");
        if (sourceContainsFile)
        {
            testDirectory.CreateFile("SourceOwner/Latest/unmatched.txt", "unmatched");
        }
        else
        {
            testDirectory.CreateDirectory("SourceOwner/Latest/Nested");
        }

        using CancellationTokenSource cancellationSource = new();
        await cancellationSource.CancelAsync();
        S3ReusablePackageFileCopier copier = CreateReusableFileCopier(new StubFileHashService());

        Func<Task> copy = () => copier.CopyUnchangedFilesAsync(
            new OwnedContentPath(sourceOwner, sourcePath),
            new OwnedContentPath(destinationOwner, destinationPath),
            Array.Empty<RemoteFileManifestEntry>(),
            cancellationSource.Token);

        await copy.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(false, "Reusable package paths must not contain reparse points.")]
    [InlineData(true, "Package staging paths must not contain reparse points.")]
    public async Task CopyUnchangedFilesAsync_RejectsLinkIntroducedAfterHashBeforeCopyOrTraversalAsync(
        bool linkDestination,
        string expectedMessage)
    {
        using TestDirectory testDirectory = new();
        string sourceOwner = testDirectory.CreateDirectory("SourceOwner");
        string sourcePath = testDirectory.CreateDirectory("SourceOwner/Latest");
        string sourceNestedPath = testDirectory.CreateDirectory("SourceOwner/Latest/Nested");
        string triggerPath = testDirectory.CreateFile("SourceOwner/Latest/trigger.txt", "payload");
        string destinationOwner = testDirectory.CreateDirectory("DestinationOwner");
        string destinationPath = testDirectory.CreateDirectory("DestinationOwner/Staging");
        string outsidePath = testDirectory.CreateDirectory("Outside");
        StubFileHashService hashService = new()
        {
            HashForPath = path =>
            {
                if (string.Equals(path, triggerPath, StringComparison.OrdinalIgnoreCase))
                {
                    string linkPath = sourceNestedPath;
                    if (linkDestination)
                    {
                        Directory.Delete(destinationPath, false);
                        linkPath = destinationPath;
                    }
                    else
                    {
                        Directory.Delete(sourceNestedPath, false);
                    }

                    ReparsePointTestSupport.CreateDirectoryJunction(linkPath, outsidePath);
                }

                return StubFileHashService.MatchingHash;
            }
        };
        S3ReusablePackageFileCopier copier = CreateReusableFileCopier(hashService);

        Func<Task> copy = () => copier.CopyUnchangedFilesAsync(
            new OwnedContentPath(sourceOwner, sourcePath),
            new OwnedContentPath(destinationOwner, destinationPath),
            [new RemoteFileManifestEntry("trigger.txt", StubFileHashService.MatchingHash, 7)],
            CancellationToken.None);

        await copy.Should().ThrowAsync<InvalidDataException>()
            .WithMessage(expectedMessage);
        Directory.EnumerateFileSystemEntries(outsidePath).Should().BeEmpty();
    }

    [SymbolicLinkFact]
    public async Task CopyUnchangedFilesAsync_FileReplacementDuringHash_FailsClosedAsync()
    {
        using TestDirectory testDirectory = new();
        string sourceOwner = testDirectory.CreateDirectory("SourceOwner");
        string sourcePath = testDirectory.CreateDirectory("SourceOwner/Latest");
        string sourceFilePath = testDirectory.CreateFile("SourceOwner/Latest/payload.txt", "payload");
        string destinationOwner = testDirectory.CreateDirectory("DestinationOwner");
        string destinationPath = testDirectory.CreateDirectory("DestinationOwner/Staging");
        string outsideFilePath = testDirectory.CreateFile("Outside/payload.txt", "outside");
        StubFileHashService hashService = new()
        {
            HashForPath = path =>
            {
                File.Delete(path);
                SymbolicLinkTestSupport.CreateFileLink(path, outsideFilePath);
                return StubFileHashService.MatchingHash;
            }
        };
        S3ReusablePackageFileCopier copier = CreateReusableFileCopier(hashService);

        Func<Task> copy = () => copier.CopyUnchangedFilesAsync(
            new OwnedContentPath(sourceOwner, sourcePath),
            new OwnedContentPath(destinationOwner, destinationPath),
            [new RemoteFileManifestEntry("payload.txt", StubFileHashService.MatchingHash, 7)],
            CancellationToken.None);

        await copy.Should().ThrowAsync<IOException>();
        (await File.ReadAllTextAsync(sourceFilePath)).Should().Be("payload");
        File.Exists(Path.Combine(destinationPath, "payload.txt")).Should().BeFalse();
        (await File.ReadAllTextAsync(outsideFilePath)).Should().Be("outside");
    }

    [Fact]
    public async Task CopyUnchangedFilesAsync_SameLengthRewriteDuringHash_FailsClosedAsync()
    {
        using TestDirectory testDirectory = new();
        string sourceOwner = testDirectory.CreateDirectory("SourceOwner");
        string sourcePath = testDirectory.CreateDirectory("SourceOwner/Latest");
        string sourceFilePath = testDirectory.CreateFile("SourceOwner/Latest/payload.txt", "payload");
        string destinationOwner = testDirectory.CreateDirectory("DestinationOwner");
        string destinationPath = testDirectory.CreateDirectory("DestinationOwner/Staging");
        StubFileHashService hashService = new()
        {
            HashForPath = path =>
            {
                File.WriteAllText(path, "changed");
                return StubFileHashService.MatchingHash;
            }
        };
        S3ReusablePackageFileCopier copier = CreateReusableFileCopier(hashService);

        Func<Task> copy = () => copier.CopyUnchangedFilesAsync(
            new OwnedContentPath(sourceOwner, sourcePath),
            new OwnedContentPath(destinationOwner, destinationPath),
            [new RemoteFileManifestEntry("payload.txt", StubFileHashService.MatchingHash, 7)],
            CancellationToken.None);

        await copy.Should().ThrowAsync<IOException>();
        (await File.ReadAllTextAsync(sourceFilePath)).Should().Be("payload");
        File.Exists(Path.Combine(destinationPath, "payload.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task CopyUnchangedFilesAsync_UsesRecursiveManifestPathAndSkipsMissingIndexEntryAsync()
    {
        using TestDirectory testDirectory = new();
        string sourceOwner = testDirectory.CreateDirectory("SourceOwner");
        string sourcePath = testDirectory.CreateDirectory("SourceOwner/Latest");
        testDirectory.CreateFile("SourceOwner/Latest/readme.txt", "payload");
        testDirectory.CreateFile("SourceOwner/Latest/Nested/readme.txt", "payload");
        string destinationOwner = testDirectory.CreateDirectory("DestinationOwner");
        string destinationPath = testDirectory.CreateDirectory("DestinationOwner/Staging");
        S3ReusablePackageFileCopier copier = CreateReusableFileCopier(
            new StubFileHashService { HashForPath = _ => StubFileHashService.MatchingHash });

        await copier.CopyUnchangedFilesAsync(
            new OwnedContentPath(sourceOwner, sourcePath),
            new OwnedContentPath(destinationOwner, destinationPath),
            [new RemoteFileManifestEntry("Nested/readme.txt", StubFileHashService.MatchingHash, 7)],
            CancellationToken.None);

        File.Exists(Path.Combine(destinationPath, "readme.txt")).Should().BeFalse();
        (await File.ReadAllTextAsync(Path.Combine(destinationPath, "Nested", "readme.txt")))
            .Should().Be("payload");
    }

    /// <summary>
    ///     An S3 ETag and a locally computed MD5 sum describe the same bytes in different letter case, so a case
    ///     difference must not cost the user a re-download of a file that is already correct.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CopyUnchangedFilesAsync_HashLetterCaseDiffers_ReusesFileAsync(bool manifestHashIsLowercase)
    {
        using TestDirectory testDirectory = new();
        string sourceOwner = testDirectory.CreateDirectory("SourceOwner");
        string sourcePath = testDirectory.CreateDirectory("SourceOwner/Latest");
        testDirectory.CreateFile("SourceOwner/Latest/payload.txt", "payload");
        string destinationOwner = testDirectory.CreateDirectory("DestinationOwner");
        string destinationPath = testDirectory.CreateDirectory("DestinationOwner/Staging");
        string lowercaseHash = StubFileHashService.MatchingHash.ToLowerInvariant();
        string manifestHash = manifestHashIsLowercase ? lowercaseHash : StubFileHashService.MatchingHash;
        string computedHash = manifestHashIsLowercase ? StubFileHashService.MatchingHash : lowercaseHash;
        S3ReusablePackageFileCopier copier = CreateReusableFileCopier(
            new StubFileHashService { HashForPath = _ => computedHash });

        await copier.CopyUnchangedFilesAsync(
            new OwnedContentPath(sourceOwner, sourcePath),
            new OwnedContentPath(destinationOwner, destinationPath),
            [new RemoteFileManifestEntry("payload.txt", manifestHash, 7)],
            CancellationToken.None);

        (await File.ReadAllTextAsync(Path.Combine(destinationPath, "payload.txt"))).Should().Be("payload");
    }

    /// <summary>
    ///     Bytes already staged by an interrupted transfer are the authority for that file: reuse leaves them alone
    ///     instead of failing on the existing destination or paying to hash a copy it will not write.
    /// </summary>
    [Fact]
    public async Task CopyUnchangedFilesAsync_DestinationAlreadyStaged_KeepsStagedFileAsync()
    {
        using TestDirectory testDirectory = new();
        string sourceOwner = testDirectory.CreateDirectory("SourceOwner");
        string sourcePath = testDirectory.CreateDirectory("SourceOwner/Latest");
        testDirectory.CreateFile("SourceOwner/Latest/Nested/readme.txt", "payload");
        string destinationOwner = testDirectory.CreateDirectory("DestinationOwner");
        string destinationPath = testDirectory.CreateDirectory("DestinationOwner/Staging");
        string stagedFilePath = testDirectory.CreateFile("DestinationOwner/Staging/Nested/readme.txt", "staged!");
        List<string> hashedPaths = [];
        S3ReusablePackageFileCopier copier = CreateReusableFileCopier(
            new StubFileHashService
            {
                HashForPath = path =>
                {
                    hashedPaths.Add(path);
                    return StubFileHashService.MatchingHash;
                }
            });

        Func<Task> copy = () => copier.CopyUnchangedFilesAsync(
            new OwnedContentPath(sourceOwner, sourcePath),
            new OwnedContentPath(destinationOwner, destinationPath),
            [new RemoteFileManifestEntry("Nested/readme.txt", StubFileHashService.MatchingHash, 7)],
            CancellationToken.None);

        await copy.Should().NotThrowAsync();
        (await File.ReadAllTextAsync(stagedFilePath)).Should().Be("staged!");
        hashedPaths.Should().BeEmpty();
    }

    [Theory]
    [InlineData(8, StubFileHashService.MatchingHash, StubFileHashService.MatchingHash)]
    [InlineData(7, "unreliable", "unreliable")]
    [InlineData(7, StubFileHashService.MatchingHash, StubFileHashService.MismatchedHash)]
    public async Task CopyUnchangedFilesAsync_IntegrityFailure_DoesNotReuseAsync(
        int manifestSize,
        string manifestHash,
        string computedHash)
    {
        using TestDirectory testDirectory = new();
        string sourceOwner = testDirectory.CreateDirectory("SourceOwner");
        string sourcePath = testDirectory.CreateDirectory("SourceOwner/Latest");
        testDirectory.CreateFile("SourceOwner/Latest/payload.txt", "payload");
        string destinationOwner = testDirectory.CreateDirectory("DestinationOwner");
        string destinationPath = testDirectory.CreateDirectory("DestinationOwner/Staging");
        S3ReusablePackageFileCopier copier = CreateReusableFileCopier(
            new StubFileHashService { HashForPath = _ => computedHash });

        await copier.CopyUnchangedFilesAsync(
            new OwnedContentPath(sourceOwner, sourcePath),
            new OwnedContentPath(destinationOwner, destinationPath),
            [new RemoteFileManifestEntry("payload.txt", manifestHash, (ulong)manifestSize)],
            CancellationToken.None);

        File.Exists(Path.Combine(destinationPath, "payload.txt")).Should().BeFalse();
    }

    /// <summary>
    ///     Each staging subfolder is re-validated as the walk descends into it, because the files copied at the level
    ///     above give another process a window to swap that subfolder for a link before anything is written into it.
    /// </summary>
    [Fact]
    public async Task CopyUnchangedFilesAsync_StagingSubfolderLinkedDuringCopy_RejectsBeforeDescendingAsync()
    {
        using TestDirectory testDirectory = new();
        string sourceOwner = testDirectory.CreateDirectory("SourceOwner");
        string sourcePath = testDirectory.CreateDirectory("SourceOwner/Latest");
        string triggerPath = testDirectory.CreateFile("SourceOwner/Latest/trigger.txt", "payload");
        testDirectory.CreateFile("SourceOwner/Latest/Nested/Deeper/readme.txt", "payload");
        string destinationOwner = testDirectory.CreateDirectory("DestinationOwner");
        string destinationPath = testDirectory.CreateDirectory("DestinationOwner/Staging");
        string outsidePath = testDirectory.CreateDirectory("Outside");
        StubFileHashService hashService = new()
        {
            HashForPath = path =>
            {
                if (string.Equals(path, triggerPath, StringComparison.OrdinalIgnoreCase))
                {
                    ReparsePointTestSupport.CreateDirectoryJunction(
                        Path.Combine(destinationPath, "Nested"),
                        outsidePath);
                }

                return StubFileHashService.MatchingHash;
            }
        };
        S3ReusablePackageFileCopier copier = CreateReusableFileCopier(hashService);

        Func<Task> copy = () => copier.CopyUnchangedFilesAsync(
            new OwnedContentPath(sourceOwner, sourcePath),
            new OwnedContentPath(destinationOwner, destinationPath),
            [
                new RemoteFileManifestEntry("trigger.txt", StubFileHashService.MatchingHash, 7),
                new RemoteFileManifestEntry("Nested/Deeper/readme.txt", StubFileHashService.MatchingHash, 7)
            ],
            CancellationToken.None);

        await copy.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("Package staging paths must not contain reparse points.");
        Directory.EnumerateFileSystemEntries(outsidePath).Should().BeEmpty();
    }

    private static S3ReusablePackageFileCopier CreateReusableFileCopier(IFileHashService hashService)
    {
        return new S3ReusablePackageFileCopier(hashService, NullLogger.Instance);
    }
}
