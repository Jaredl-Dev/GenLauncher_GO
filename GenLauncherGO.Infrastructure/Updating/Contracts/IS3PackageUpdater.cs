using System;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Models;

namespace GenLauncherGO.Infrastructure.Updating.Contracts;

internal interface IS3PackageUpdater
{
    Task UpdateAsync(
        S3PackageUpdateRequest request,
        IProgress<PackageUpdateProgress>? progress,
        CancellationToken cancellationToken,
        PackageDownloadPauseController? pauseController = null);

    /// <summary>
    ///     Downloads and repairs selected package files directly inside an installed S3-backed package.
    /// </summary>
    Task RepairFilesAsync(
        S3PackageFileRepairRequest request,
        IProgress<PackageUpdateProgress>? progress,
        CancellationToken cancellationToken);
}
