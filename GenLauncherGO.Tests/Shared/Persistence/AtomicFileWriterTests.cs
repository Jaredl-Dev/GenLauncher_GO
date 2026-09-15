using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Shared.Persistence;

namespace GenLauncherGO.Tests.Shared.Persistence;

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
            await Task.WhenAll(firstReady.Task, secondReady.Task).WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        }
        finally
        {
            releaseWriters.TrySetResult();
        }

        await Task.WhenAll(firstWrite, secondWrite).WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        File.ReadAllText(documentPath).Should().BeOneOf(
            "first complete document",
            "second complete document");
        Directory.EnumerateFileSystemEntries(destinationDirectory).Should().ContainSingle()
            .Which.Should().Be(documentPath);
    }

    [Fact]
    public async Task WriteFileIfMissingAsync_MissingDestination_PublishesCompletedFileAsync()
    {
        using TestDirectory directory = new();
        string destinationPath = directory.GetPath("Images/asset.png");
        string destinationDirectory = Path.GetDirectoryName(destinationPath)!;
        var writer = new AtomicFileWriter();

        bool committed = await writer.WriteFileIfMissingAsync(
            destinationPath,
            (temporaryPath, cancellationToken) =>
                File.WriteAllTextAsync(temporaryPath, "complete asset", cancellationToken),
            CancellationToken.None);

        committed.Should().BeTrue();
        File.ReadAllText(destinationPath).Should().Be("complete asset");
        Directory.EnumerateFileSystemEntries(destinationDirectory).Should().ContainSingle()
            .Which.Should().Be(destinationPath);
    }

    [Fact]
    public async Task WriteFileIfMissingAsync_DestinationCreatedDuringWrite_KeepsConcurrentFileAsync()
    {
        using TestDirectory directory = new();
        string destinationPath = directory.GetPath("asset.png");
        var writer = new AtomicFileWriter();

        bool committed = await writer.WriteFileIfMissingAsync(
            destinationPath,
            async (temporaryPath, cancellationToken) =>
            {
                await File.WriteAllTextAsync(temporaryPath, "staged asset", cancellationToken);
                await File.WriteAllTextAsync(destinationPath, "concurrent asset", cancellationToken);
            },
            CancellationToken.None);

        committed.Should().BeFalse();
        File.ReadAllText(destinationPath).Should().Be("concurrent asset");
        Directory.EnumerateFileSystemEntries(directory.Path).Should().ContainSingle()
            .Which.Should().Be(destinationPath);
    }
}
