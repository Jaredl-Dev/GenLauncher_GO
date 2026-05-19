using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Models;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Records both S3 package operations, which are separate contracts a caller can confuse: a full update replaces
///     the installed folder, a repair rewrites named files in place.
/// </summary>
internal sealed class RecordingS3PackageUpdater : IS3PackageUpdater
{
    public List<S3PackageUpdateRequest> UpdateRequests { get; } = [];

    public List<S3PackageFileRepairRequest> RepairRequests { get; } = [];

    public List<PackageDownloadPauseController?> PauseControllers { get; } = [];

    /// <summary>
    ///     Reported once from each operation when set.
    /// </summary>
    public PackageUpdateProgress? ProgressToReport { get; init; }

    public Task UpdateAsync(
        S3PackageUpdateRequest request,
        IProgress<PackageUpdateProgress>? progress,
        CancellationToken cancellationToken,
        PackageDownloadPauseController? pauseController = null)
    {
        UpdateRequests.Add(request);
        PauseControllers.Add(pauseController);
        ReportProgress(progress);
        return Task.CompletedTask;
    }

    public Task RepairFilesAsync(
        S3PackageFileRepairRequest request,
        IProgress<PackageUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        RepairRequests.Add(request);
        ReportProgress(progress);
        return Task.CompletedTask;
    }

    private void ReportProgress(IProgress<PackageUpdateProgress>? progress)
    {
        if (ProgressToReport is not null)
        {
            progress?.Report(ProgressToReport);
        }
    }
}
