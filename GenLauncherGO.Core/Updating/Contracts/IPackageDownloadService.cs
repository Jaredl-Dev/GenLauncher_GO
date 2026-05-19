using System;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Updating.Models;

namespace GenLauncherGO.Core.Updating.Contracts;

/// <summary>
///     Downloads and installs launcher-managed modification packages.
/// </summary>
public interface IPackageDownloadService
{
    /// <summary>
    ///     Downloads and installs one package, reporting progress until the returned task completes.
    /// </summary>
    Task<PackageDownloadResult> DownloadAsync(
        LauncherContent modification,
        LauncherContentVersion version,
        IProgress<PackageUpdateProgress>? progress,
        CancellationToken cancellationToken,
        PackageDownloadPauseController? pauseController = null);
}
