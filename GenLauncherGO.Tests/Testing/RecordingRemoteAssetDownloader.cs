using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Remote.Contracts;

namespace GenLauncherGO.Tests.Testing;

internal sealed class RecordingRemoteAssetDownloader : IRemoteAssetDownloader
{
    private readonly ConcurrentQueue<(Uri SourceUri, string DestinationFilePath)> _calls = new();

    public IReadOnlyList<(Uri SourceUri, string DestinationFilePath)> Calls => _calls.ToArray();

    public Func<Uri, string, CancellationToken, Task>? Handler { get; set; }

    public Task DownloadIfMissingAsync(
        Uri sourceUri,
        string destinationFilePath,
        CancellationToken cancellationToken)
    {
        _calls.Enqueue((sourceUri, destinationFilePath));
        return Handler?.Invoke(sourceUri, destinationFilePath, cancellationToken) ?? Task.CompletedTask;
    }
}
