using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenLauncherGO.Features.Updating;

internal interface ISingleFilePackageUpdater
{
    Task UpdateAsync(
        DownloadFileMetadata metadata,
        PackageUpdatePathSet paths,
        IProgress<PackageUpdateProgress>? progress,
        CancellationToken cancellationToken,
        PackageDownloadPauseController? pauseController = null);
}
