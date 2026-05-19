using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GenLauncherGO.Infrastructure.Persistence.Services;

/// <summary>
///     Commits complete text documents atomically within their destination directory.
/// </summary>
internal interface IAtomicFileWriter
{
    /// <summary>
    ///     Writes and durably flushes a temporary file before atomically committing it to the destination path.
    /// </summary>
    void WriteText(string destinationPath, string contents);

    /// <summary>
    ///     Writes and durably flushes a temporary file asynchronously before atomically committing it to the destination path.
    /// </summary>
    /// <param name="destinationPath">The final document path.</param>
    /// <param name="writeTemporaryFileAsync">
    ///     The operation that writes the complete document to the temporary stream and leaves the stream open.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token that cancels temporary-file writing and flushing. The final atomic commit is not cancellable once started.
    /// </param>
    Task WriteAsync(
        string destinationPath,
        Func<Stream, CancellationToken, Task> writeTemporaryFileAsync,
        CancellationToken cancellationToken);
}
