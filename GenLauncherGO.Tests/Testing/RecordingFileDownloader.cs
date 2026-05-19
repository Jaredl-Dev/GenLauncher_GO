using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Models;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Records download requests and, unless a handler takes over, writes a file of the requested length so the
///     caller's own file handling runs against real bytes.
/// </summary>
internal sealed class RecordingFileDownloader : IResumableFileDownloader
{
    private const byte FillerByte = (byte)'x';

    public ConcurrentQueue<DownloadFileRequest> Requests { get; } = new();

    public Func<DownloadFileRequest, CancellationToken, Task>? Handler { get; init; }

    public Func<DownloadFileRequest, IProgress<DownloadProgress>?, CancellationToken, Task>? ProgressHandler
    {
        get;
        init;
    }

    public async Task DownloadFileAsync(
        DownloadFileRequest request,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        if (ProgressHandler is not null)
        {
            await ProgressHandler(request, progress, cancellationToken);
            return;
        }

        if (Handler is not null)
        {
            await Handler(request, cancellationToken);
            return;
        }

        byte[] payload = new byte[checked((int)request.ExpectedBytes.GetValueOrDefault())];
        Array.Fill(payload, FillerByte);
        Directory.CreateDirectory(Path.GetDirectoryName(request.DestinationFilePath)!);
        await File.WriteAllBytesAsync(request.DestinationFilePath, payload, cancellationToken);
    }
}
