using System;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.UI.Features.Integrity;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Starts package downloads that hold a chosen state, so a test can drive the rest of the launcher around one.
/// </summary>
internal static class TestPackageDownload
{
    /// <summary>
    ///     Starts a transfer that only ends when its token is canceled and leaves it paused, matching a user who
    ///     paused a download and then went on to do something else with the launcher.
    /// </summary>
    public static Task<PackageDownloadResult> StartPaused(
        LauncherPackageActivityService packageActivityService)
    {
        ArgumentNullException.ThrowIfNull(packageActivityService);

        object owner = new();
        packageActivityService.TryStartDownload(
                owner,
                "Paused download",
                (_, _, cancellationToken) => WaitForCancellationAsync(cancellationToken),
                () => { },
                _ => { },
                () => { },
                _ => { },
                out Task<PackageDownloadResult>? lifecycle)
            .Should()
            .BeTrue();
        packageActivityService.TryToggleDownloadPause(owner, out bool paused).Should().BeTrue();
        paused.Should().BeTrue();

        return lifecycle ?? throw new InvalidOperationException("Download lifecycle task was not created.");
    }

    /// <summary>
    ///     Mirrors a transport that reports a canceled transfer as a terminal result rather than as a fault.
    /// </summary>
    public static async Task<PackageDownloadResult> WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return PackageDownloadResult.Canceled();
        }

        throw new InvalidOperationException("The cancellation delay unexpectedly completed.");
    }
}
