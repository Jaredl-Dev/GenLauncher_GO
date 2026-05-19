using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Infrastructure.Common;

namespace GenLauncherGO.Infrastructure.Persistence.Services;

/// <summary>
///     Writes complete text files through a same-directory temporary file and atomic commit.
/// </summary>
internal sealed class AtomicFileWriter : IAtomicFileWriter
{
    public void WriteText(string destinationPath, string contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        (string fullDestinationPath, string temporaryPath) = PrepareWrite(destinationPath);
        try
        {
            WriteTemporaryFile(temporaryPath, contents);
            CommitTemporaryFile(temporaryPath, fullDestinationPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task WriteAsync(
        string destinationPath,
        Func<Stream, CancellationToken, Task> writeTemporaryFileAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writeTemporaryFileAsync);
        cancellationToken.ThrowIfCancellationRequested();
        (string fullDestinationPath, string temporaryPath) = PrepareWrite(destinationPath);
        try
        {
            await WriteTemporaryFileAsync(
                    temporaryPath,
                    writeTemporaryFileAsync,
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            // Once the atomic replace or move begins, it must run to completion so callers never observe
            // an ambiguous destination state.
            CommitTemporaryFile(temporaryPath, fullDestinationPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static (string DestinationPath, string TemporaryPath) PrepareWrite(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        string fullDestinationPath = LexicalPath.NormalizeFullPath(destinationPath);
        string destinationDirectory = Path.GetDirectoryName(fullDestinationPath)
                                      ?? throw new InvalidOperationException(
                                          "Atomic document paths must have a parent directory.");
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            destinationDirectory,
            "Atomic document directories");
        Directory.CreateDirectory(destinationDirectory);
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            destinationDirectory,
            "Atomic document directories");
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            fullDestinationPath,
            "Atomic document paths");

        string temporaryPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(fullDestinationPath)}.{Guid.NewGuid():N}.tmp");
        return (fullDestinationPath, temporaryPath);
    }

    private static void WriteTemporaryFile(string temporaryPath, string contents)
    {
        byte[] bytes = new UTF8Encoding(false).GetBytes(contents);
        using FileStream stream = new(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(true);
    }

    private static async Task WriteTemporaryFileAsync(
        string temporaryPath,
        Func<Stream, CancellationToken, Task> writeTemporaryFileAsync,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await writeTemporaryFileAsync(stream, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        // FlushAsync drains managed buffers with cancellation support. Flush(true) retains the existing
        // durable-to-disk guarantee before the atomic commit.
        stream.Flush(true);
    }

    private static void CommitTemporaryFile(string temporaryPath, string destinationPath)
    {
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            destinationPath,
            "Atomic document paths");
        if (File.Exists(destinationPath))
        {
            File.Replace(temporaryPath, destinationPath, null, true);
            return;
        }

        try
        {
            File.Move(temporaryPath, destinationPath);
        }
        catch (IOException) when (File.Exists(destinationPath))
        {
            File.Replace(temporaryPath, destinationPath, null, true);
        }
    }
}
