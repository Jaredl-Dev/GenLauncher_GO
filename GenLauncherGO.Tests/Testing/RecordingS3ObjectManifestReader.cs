using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Models;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Returns enqueued manifests in order and records what was asked for.
/// </summary>
/// <remarks>
///     Once the queue drains, the last manifest is returned again: listing the same prefix twice has to produce the
///     same objects, so a caller that re-reads must not silently see an empty bucket.
/// </remarks>
internal sealed class RecordingS3ObjectManifestReader : IS3ObjectManifestReader
{
    private readonly Queue<IReadOnlyList<RemoteFileManifestEntry>> _manifests = new();

    private IReadOnlyList<RemoteFileManifestEntry> _lastManifest = Array.Empty<RemoteFileManifestEntry>();

    public List<S3ObjectManifestRequest> Requests { get; } = [];

    public void Enqueue(params RemoteFileManifestEntry[] files)
    {
        ArgumentNullException.ThrowIfNull(files);

        _manifests.Enqueue(files);
    }

    public Task<IReadOnlyList<RemoteFileManifestEntry>> ReadManifestAsync(
        S3ObjectManifestRequest request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (_manifests.TryDequeue(out IReadOnlyList<RemoteFileManifestEntry>? manifest))
        {
            _lastManifest = manifest;
        }

        return Task.FromResult(_lastManifest);
    }
}
