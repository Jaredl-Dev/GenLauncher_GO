using System;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Models;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Answers download metadata requests and counts them, which is how a test sees whether a size lookup was
///     avoided, retried, or resolved from cache rather than asked for again.
/// </summary>
internal sealed class StubDownloadFileMetadataReader : IDownloadFileMetadataReader
{
    private readonly Func<Uri, CancellationToken, Task<DownloadFileMetadata>>? _handler;

    public StubDownloadFileMetadataReader(
        Func<Uri, CancellationToken, Task<DownloadFileMetadata>>? handler = null)
    {
        _handler = handler;
    }

    /// <summary>
    ///     Answers every request with the same metadata, for a test whose subject is what happens after the lookup
    ///     rather than the lookup itself.
    /// </summary>
    public StubDownloadFileMetadataReader(string fileName, long? totalBytes)
        : this((downloadUri, _) => Task.FromResult(
            new DownloadFileMetadata(downloadUri, fileName, totalBytes)))
    {
    }

    public int RequestCount { get; private set; }

    public Task<DownloadFileMetadata> ReadMetadataAsync(Uri downloadUri, CancellationToken cancellationToken)
    {
        RequestCount++;
        return _handler?.Invoke(downloadUri, cancellationToken) ??
               Task.FromException<DownloadFileMetadata>(
                   new InvalidOperationException($"No metadata result was configured for '{downloadUri}'."));
    }
}
