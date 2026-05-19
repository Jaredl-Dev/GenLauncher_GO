using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Persistence.Services;

namespace GenLauncherGO.Tests.Infrastructure.Persistence.Services;

public sealed class AtomicFileWriterTests
{
    [Fact]
    public void WriteText_MissingDestination_CreatesParentAndUtf8FileWithoutBom()
    {
        using TestDirectory directory = new();
        string documentPath = directory.GetPath("State/settings.yaml");
        string destinationDirectory = Path.GetDirectoryName(documentPath)!;
        const string Contents = "Name: Δ";
        var writer = new AtomicFileWriter();

        writer.WriteText(documentPath, Contents);

        File.ReadAllBytes(documentPath).Should().Equal(new UTF8Encoding(false).GetBytes(Contents));
        Directory.EnumerateFileSystemEntries(destinationDirectory).Should().ContainSingle()
            .Which.Should().Be(documentPath);
    }

    [Fact]
    public void WriteText_ExistingDestination_ReplacesCompleteContents()
    {
        using TestDirectory directory = new();
        string documentPath = directory.CreateFile("state.yaml", "Name: original and longer");
        var writer = new AtomicFileWriter();

        writer.WriteText(documentPath, "Name: replacement");

        File.ReadAllText(documentPath).Should().Be("Name: replacement");
        Directory.EnumerateFileSystemEntries(directory.Path).Should().ContainSingle()
            .Which.Should().Be(documentPath);
    }

    [Fact]
    public void WriteText_CommitFailure_PreservesOriginalAndCleansTemporaryFile()
    {
        using TestDirectory directory = new();
        string documentPath = directory.CreateFile("state.yaml", "Name: original");
        var writer = new AtomicFileWriter();
        using FileStream lockedDocument = new(
            documentPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        Action act = () => writer.WriteText(documentPath, "Name: replacement");

        act.Should().Throw<IOException>();
        File.ReadAllText(documentPath).Should().Be("Name: original");
        Directory.EnumerateFileSystemEntries(directory.Path).Should().ContainSingle()
            .Which.Should().Be(documentPath);
    }

    /// <summary>
    ///     The destination directory is created when it is missing, so the path chain has to be cleared before that
    ///     happens: creating it first would plant a launcher directory inside whatever the link resolves to, and the
    ///     later refusal would not take it back.
    /// </summary>
    [Fact]
    public void WriteText_MissingDirectoryUnderLinkedParent_RejectsWithoutCreatingIt()
    {
        using TestDirectory directory = new();
        string linkPath = directory.GetPath("Linked");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, linkPath);
        string documentPath = Path.Combine(linkPath, "State", "state.yaml");
        var writer = new AtomicFileWriter();

        Action act = () => writer.WriteText(documentPath, "Name: unsafe");

        act.Should().Throw<InvalidDataException>();
        Directory.EnumerateFileSystemEntries(junction.TargetDirectory).Should().ContainSingle()
            .Which.Should().Be(junction.CanaryFilePath);
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    [Fact]
    public void WriteText_LinkedParent_RejectsWithoutTouchingTarget()
    {
        using TestDirectory directory = new();
        string linkPath = directory.GetPath("Linked");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, linkPath);
        string documentPath = Path.Combine(linkPath, "state.yaml");
        var writer = new AtomicFileWriter();

        Action act = () => writer.WriteText(documentPath, "Name: unsafe");

        act.Should().Throw<InvalidDataException>();
        File.Exists(Path.Combine(junction.TargetDirectory, "state.yaml")).Should().BeFalse();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    [Fact]
    public async Task WriteAsync_MissingDestination_WritesCompleteContentsAsync()
    {
        using TestDirectory directory = new();
        string documentPath = directory.GetPath("State/state.bin");
        string destinationDirectory = Path.GetDirectoryName(documentPath)!;
        byte[] expectedBytes = [0, 1, 2, 3, 255];
        var writer = new AtomicFileWriter();

        await writer.WriteAsync(
            documentPath,
            (stream, cancellationToken) => stream.WriteAsync(expectedBytes, cancellationToken).AsTask(),
            CancellationToken.None);

        File.ReadAllBytes(documentPath).Should().Equal(expectedBytes);
        Directory.EnumerateFileSystemEntries(destinationDirectory).Should().ContainSingle()
            .Which.Should().Be(documentPath);
    }

    /// <summary>
    ///     The staged bytes must land beside the destination and stay invisible to a reader until the commit, which is
    ///     what makes the replace atomic on the same volume.
    /// </summary>
    [Fact]
    public async Task WriteAsync_BeforeCommit_StagesOneSiblingEntryBesideTheUnchangedDestinationAsync()
    {
        using TestDirectory directory = new();
        string documentPath = directory.CreateFile("state.yaml", "Name: original");
        var writer = new AtomicFileWriter();
        List<string> stagedEntries = [];
        string observedContents = string.Empty;

        await writer.WriteAsync(
            documentPath,
            async (stream, cancellationToken) =>
            {
                stagedEntries.AddRange(Directory.EnumerateFileSystemEntries(directory.Path));
                observedContents = await File.ReadAllTextAsync(documentPath, cancellationToken);
                await stream.WriteAsync(Encoding.UTF8.GetBytes("Name: replacement"), cancellationToken);
            },
            CancellationToken.None);

        stagedEntries.Should().HaveCount(2).And.Contain(documentPath);
        observedContents.Should().Be("Name: original");
        File.ReadAllText(documentPath).Should().Be("Name: replacement");
        Directory.EnumerateFileSystemEntries(directory.Path).Should().ContainSingle()
            .Which.Should().Be(documentPath);
    }

    [Fact]
    public async Task WriteAsync_PreCanceledToken_LeavesDestinationUntouchedAsync()
    {
        using TestDirectory directory = new();
        string documentPath = directory.CreateFile("state.yaml", "Name: original");
        var writer = new AtomicFileWriter();
        bool writerCalled = false;
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Func<Task> act = () => writer.WriteAsync(
            documentPath,
            (_, _) =>
            {
                writerCalled = true;
                return Task.CompletedTask;
            },
            cancellationTokenSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        writerCalled.Should().BeFalse();
        File.ReadAllText(documentPath).Should().Be("Name: original");
        Directory.EnumerateFileSystemEntries(directory.Path).Should().ContainSingle()
            .Which.Should().Be(documentPath);
    }

    [Fact]
    public async Task WriteAsync_CanceledDuringWrite_PreservesOriginalAndCleansTemporaryFileAsync()
    {
        using TestDirectory directory = new();
        string documentPath = directory.CreateFile("state.yaml", "Name: original");
        var writer = new AtomicFileWriter();
        using var cancellationTokenSource = new CancellationTokenSource();

        Func<Task> act = () => writer.WriteAsync(
            documentPath,
            async (stream, cancellationToken) =>
            {
                byte[] replacement = Encoding.UTF8.GetBytes("Name: replacement");
                await stream.WriteAsync(replacement.AsMemory(), cancellationToken);
                await cancellationTokenSource.CancelAsync();
            },
            cancellationTokenSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.ReadAllText(documentPath).Should().Be("Name: original");
        Directory.EnumerateFileSystemEntries(directory.Path).Should().ContainSingle()
            .Which.Should().Be(documentPath);
    }

    [Fact]
    public async Task WriteAsync_WriterFailure_PreservesOriginalAndCleansTemporaryFileAsync()
    {
        using TestDirectory directory = new();
        string documentPath = directory.CreateFile("state.yaml", "Name: original");
        var writer = new AtomicFileWriter();

        Func<Task> act = () => writer.WriteAsync(
            documentPath,
            async (stream, cancellationToken) =>
            {
                byte[] replacement = Encoding.UTF8.GetBytes("Name: replacement");
                await stream.WriteAsync(replacement.AsMemory(), cancellationToken);
                throw new IOException("simulated write failure");
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
        File.ReadAllText(documentPath).Should().Be("Name: original");
        Directory.EnumerateFileSystemEntries(directory.Path).Should().ContainSingle()
            .Which.Should().Be(documentPath);
    }

    [Fact]
    public async Task WriteAsync_LinkedDestination_RejectsWithoutTouchingTargetAsync()
    {
        using TestDirectory directory = new();
        string documentPath = directory.GetPath("state.yaml");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, documentPath);
        var writer = new AtomicFileWriter();
        bool writerCalled = false;

        Func<Task> act = () => writer.WriteAsync(
            documentPath,
            (_, _) =>
            {
                writerCalled = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>();
        writerCalled.Should().BeFalse();
        Directory.EnumerateFileSystemEntries(junction.TargetDirectory).Should().ContainSingle()
            .Which.Should().Be(junction.CanaryFilePath);
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    /// <summary>
    ///     A link planted between the safety check and the commit must still be refused, because the commit is the step
    ///     that would follow it and overwrite whatever the link resolves to.
    /// </summary>
    [Fact]
    public async Task WriteAsync_DestinationLinkedDuringWrite_RejectsCommitWithoutTouchingTargetAsync()
    {
        using TestDirectory directory = new();
        string documentPath = directory.GetPath("state.yaml");
        var writer = new AtomicFileWriter();
        ProtectedJunction? junction = null;

        Func<Task> act = () => writer.WriteAsync(
            documentPath,
            async (stream, cancellationToken) =>
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes("Name: replacement"), cancellationToken);
                junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, documentPath);
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>();
        junction.Should().NotBeNull();
        Directory.EnumerateFileSystemEntries(junction!.TargetDirectory).Should().ContainSingle()
            .Which.Should().Be(junction.CanaryFilePath);
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    /// <summary>
    ///     Another writer can create the destination between the safety check and the commit, and the commit still owns
    ///     the final contents.
    /// </summary>
    [Fact]
    public async Task WriteAsync_DestinationCreatedDuringWrite_ReplacesItWithTheCommittedContentsAsync()
    {
        using TestDirectory directory = new();
        string documentPath = directory.GetPath("State/state.yaml");
        string destinationDirectory = Path.GetDirectoryName(documentPath)!;
        var writer = new AtomicFileWriter();

        await writer.WriteAsync(
            documentPath,
            async (stream, cancellationToken) =>
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes("committed document"), cancellationToken);
                await File.WriteAllTextAsync(documentPath, "racing document", cancellationToken);
            },
            CancellationToken.None);

        File.ReadAllText(documentPath).Should().Be("committed document");
        Directory.EnumerateFileSystemEntries(destinationDirectory).Should().ContainSingle()
            .Which.Should().Be(documentPath);
    }

    [Fact]
    public async Task WriteAsync_ConcurrentCreatorsLeaveOneCompleteDocumentAndNoTemporaryFilesAsync()
    {
        using TestDirectory directory = new();
        string documentPath = directory.GetPath("State/state.yaml");
        string destinationDirectory = Path.GetDirectoryName(documentPath)!;
        var writer = new AtomicFileWriter();
        TaskCompletionSource firstReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource secondReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseWriters = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task firstWrite = writer.WriteAsync(
            documentPath,
            async (stream, cancellationToken) =>
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes("first complete document"), cancellationToken);
                firstReady.TrySetResult();
                await releaseWriters.Task.WaitAsync(cancellationToken);
            },
            CancellationToken.None);
        Task secondWrite = writer.WriteAsync(
            documentPath,
            async (stream, cancellationToken) =>
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes("second complete document"), cancellationToken);
                secondReady.TrySetResult();
                await releaseWriters.Task.WaitAsync(cancellationToken);
            },
            CancellationToken.None);

        try
        {
            await Task.WhenAll(firstReady.Task, secondReady.Task).WaitAsync(TestTimeouts.Wait);
        }
        finally
        {
            releaseWriters.TrySetResult();
        }

        await Task.WhenAll(firstWrite, secondWrite).WaitAsync(TestTimeouts.Wait);
        File.ReadAllText(documentPath).Should().BeOneOf(
            "first complete document",
            "second complete document");
        Directory.EnumerateFileSystemEntries(destinationDirectory).Should().ContainSingle()
            .Which.Should().Be(documentPath);
    }
}
