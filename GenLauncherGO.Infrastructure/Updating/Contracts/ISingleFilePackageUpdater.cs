using System;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Models;

namespace GenLauncherGO.Infrastructure.Updating.Contracts;

internal interface ISingleFilePackageUpdater
{
    Task UpdateAsync(
        Uri sourceUri,
        PackageUpdatePathSet paths,
        IProgress<PackageUpdateProgress>? progress,
        CancellationToken cancellationToken,
        PackageDownloadPauseController? pauseController = null);
}
