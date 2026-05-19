using System;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Updating.Contracts;
using GenLauncherGO.Core.Updating.Models;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     A download that stays in flight until the test ends it, so a test can observe the launcher while a package
///     download is running.
/// </summary>
internal sealed class ControllablePackageDownloadService : IPackageDownloadService
{
    private readonly TaskCompletionSource<PackageDownloadResult?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private IProgress<PackageUpdateProgress>? _progress;

    /// <summary>
    ///     Completes once the download has begun.
    /// </summary>
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    ///     Completes once the download's cancellation token has been signalled.
    /// </summary>
    public TaskCompletionSource CancellationObserved { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int CallCount { get; private set; }

    public async Task<PackageDownloadResult> DownloadAsync(
        LauncherContent modification,
        LauncherContentVersion version,
        IProgress<PackageUpdateProgress>? progress,
        CancellationToken cancellationToken,
        PackageDownloadPauseController? pauseController = null)
    {
        CallCount++;
        _progress = progress;
        using CancellationTokenRegistration registration =
            cancellationToken.Register(() => CancellationObserved.TrySetResult());
        Started.TrySetResult();

        PackageDownloadResult? result = await _completion.Task;
        if (result is not null)
        {
            return result;
        }

        return cancellationToken.IsCancellationRequested
            ? PackageDownloadResult.Canceled()
            : PackageDownloadResult.Succeeded();
    }

    public void Report(PackageUpdateProgress progress)
    {
        if (_progress is null)
        {
            throw new InvalidOperationException("The download has not started, so nothing observes progress yet.");
        }

        _progress.Report(progress);
    }

    /// <summary>
    ///     Ends the download with an explicit terminal result.
    /// </summary>
    public void Complete(PackageDownloadResult result)
    {
        _completion.TrySetResult(result);
    }

    /// <summary>
    ///     Ends the download the way production would: canceled when the token was signalled, otherwise succeeded.
    /// </summary>
    public void Release()
    {
        _completion.TrySetResult(null);
    }

    /// <summary>
    ///     Ends the download by faulting it, for the failure paths the launcher has to survive.
    /// </summary>
    public void Throw(Exception exception)
    {
        _completion.TrySetException(exception);
    }
}

/// <summary>
///     A download that returns its configured result immediately.
/// </summary>
internal sealed class StubPackageDownloadService : IPackageDownloadService
{
    private readonly PackageDownloadResult _result;

    public StubPackageDownloadService(PackageDownloadResult result)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
    }

    public Task<PackageDownloadResult> DownloadAsync(
        LauncherContent modification,
        LauncherContentVersion version,
        IProgress<PackageUpdateProgress>? progress,
        CancellationToken cancellationToken,
        PackageDownloadPauseController? pauseController = null)
    {
        return Task.FromResult(_result);
    }
}
