using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenLauncherGO.Features.Updating;

internal interface IResumableFileDownloader
{
    Task DownloadFileAsync(
        DownloadFileRequest request,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken);
}
