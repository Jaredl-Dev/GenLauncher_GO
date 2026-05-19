using System;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Updating.Models;

namespace GenLauncherGO.Infrastructure.Updating.Contracts;

internal interface IResumableFileDownloader
{
    Task DownloadFileAsync(
        DownloadFileRequest request,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken);
}
