using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.UI.Features.Integrity;

namespace GenLauncherGO.Tests.UI.Features.Integrity;

public sealed class LauncherPackageActivityServiceTests
{
    [Fact]
    public void TryBegin_RejectsSecondActivityUntilLeaseReleasesLifecycle()
    {
        var service = new LauncherPackageActivityService();

        bool firstStarted = service.TryBegin(
            "First",
            out LauncherPackageActivityService.LauncherPackageActivityLease? firstLease);
        bool secondStarted = service.TryBegin(
            "Second",
            out LauncherPackageActivityService.LauncherPackageActivityLease? secondLease);
        firstLease?.Dispose();
        bool thirdStarted = service.TryBegin(
            "Third",
            out LauncherPackageActivityService.LauncherPackageActivityLease? thirdLease);

        firstStarted.Should().BeTrue();
        secondStarted.Should().BeFalse();
        secondLease.Should().BeNull();
        thirdStarted.Should().BeTrue();
        thirdLease.Should().NotBeNull();

        thirdLease?.Dispose();
    }

    [Fact]
    public async Task ConcurrentDownload_StartsAtomicallyPublishOneOwnerAsync()
    {
        var service = new LauncherPackageActivityService();
        using Barrier startBarrier = new(2);
        TaskCompletionSource<PackageDownloadResult> release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        object[] owners = [new(), new()];
        bool[] started = new bool[2];
        var lifecycles = new Task<PackageDownloadResult>?[2];

        var starts = new Task[2];
        for (int index = 0; index < starts.Length; index++)
        {
            int capturedIndex = index;
            starts[index] = Task.Run(() =>
            {
                startBarrier.SignalAndWait();
                started[capturedIndex] = service.TryStartDownload(
                    owners[capturedIndex],
                    $"Download {capturedIndex}",
                    (_, _, _) => release.Task,
                    () => { },
                    _ => { },
                    () => { },
                    _ => { },
                    out lifecycles[capturedIndex]);
            });
        }

        await Task.WhenAll(starts);

        started.Should().ContainSingle(value => value);
        lifecycles.Should().ContainSingle(task => task != null);
        object winningOwner = owners[Array.IndexOf(started, true)];
        service.GetActiveDownloadTask(winningOwner).Should().BeSameAs(service.ActiveDownloadTask);

        release.SetResult(PackageDownloadResult.Succeeded());
        await lifecycles.Single(task => task != null)!;
        service.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task OwningDownload_CanPauseAndResumeWithoutEndingLifecycleAsync()
    {
        var service = new LauncherPackageActivityService();
        object owner = new();
        TaskCompletionSource<PackageDownloadResult> release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        service.TryStartDownload(
                owner,
                "Download",
                (_, _, _) => release.Task,
                () => { },
                _ => { },
                () => { },
                _ => { },
                out Task<PackageDownloadResult>? lifecycle)
            .Should()
            .BeTrue();

        service.TryToggleDownloadPause(owner, out bool paused).Should().BeTrue();
        paused.Should().BeTrue();
        lifecycle!.IsCompleted.Should().BeFalse();

        service.TryToggleDownloadPause(new object(), out _).Should().BeFalse();
        service.TryToggleDownloadPause(owner, out paused).Should().BeTrue();
        paused.Should().BeFalse();

        release.SetResult(PackageDownloadResult.Succeeded());
        await lifecycle;
    }

    [Fact]
    public async Task TryStartDownload_AfterSuspensionRequestEnded_ReportsNextCancellationAsCanceledAsync()
    {
        var service = new LauncherPackageActivityService();
        object suspendedOwner = new();
        object canceledOwner = new();
        TaskCompletionSource<PackageDownloadResult> releaseSuspended = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        bool canceledCleanupRan = false;
        PackageDownloadResult? terminalResult = null;
        service.TryStartDownload(
                suspendedOwner,
                "Suspended",
                (_, _, _) => releaseSuspended.Task,
                () => { },
                _ => { },
                () => { },
                _ => { },
                out Task<PackageDownloadResult>? suspendedLifecycle)
            .Should()
            .BeTrue();
        service.RequestActiveDownloadSuspension().Should().BeTrue();
        releaseSuspended.SetResult(PackageDownloadResult.Succeeded());
        await suspendedLifecycle!;
        service.TryStartDownload(
                canceledOwner,
                "Canceled",
                (_, _, cancellationToken) => TestPackageDownload.WaitForCancellationAsync(cancellationToken),
                () => { },
                _ => { },
                () => canceledCleanupRan = true,
                result => terminalResult = result,
                out Task<PackageDownloadResult>? canceledLifecycle)
            .Should()
            .BeTrue();

        service.RequestDownloadCancellation(new object()).Should().BeFalse();
        canceledLifecycle!.IsCompleted.Should().BeFalse();

        service.RequestDownloadCancellation(canceledOwner).Should().BeTrue();
        PackageDownloadResult canceledResult = await canceledLifecycle;

        canceledResult.Status.Should().Be(PackageDownloadStatus.Canceled);
        canceledCleanupRan.Should().BeTrue();
        terminalResult.Should().BeSameAs(canceledResult);
    }

    [Fact]
    public async Task ReleasePausedDownload_SuspendsThePausedTransferAndFreesTheLauncherAsync()
    {
        var service = new LauncherPackageActivityService();
        object owner = new();
        bool canceledCleanupRan = false;
        PackageDownloadResult? terminalResult = null;
        service.TryStartDownload(
                owner,
                "Download",
                (_, _, cancellationToken) => TestPackageDownload.WaitForCancellationAsync(cancellationToken),
                () => { },
                _ => { },
                () => canceledCleanupRan = true,
                result => terminalResult = result,
                out Task<PackageDownloadResult>? lifecycle)
            .Should()
            .BeTrue();
        service.TryToggleDownloadPause(owner, out _).Should().BeTrue();

        await service.ReleasePausedDownloadAsync();

        (await lifecycle!).Status.Should().Be(PackageDownloadStatus.Suspended);
        terminalResult!.Status.Should().Be(PackageDownloadStatus.Suspended);
        canceledCleanupRan.Should().BeFalse();
        service.IsActive.Should().BeFalse();
        service.TryBegin("Next", out LauncherPackageActivityService.LauncherPackageActivityLease? lease)
            .Should()
            .BeTrue();
        lease?.Dispose();
    }

    [Fact]
    public async Task ReleasePausedDownload_WhileTheTransferIsRunning_LeavesItAloneAsync()
    {
        var service = new LauncherPackageActivityService();
        object owner = new();
        TaskCompletionSource<PackageDownloadResult> release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.TryStartDownload(
                owner,
                "Download",
                (_, _, _) => release.Task,
                () => { },
                _ => { },
                () => { },
                _ => { },
                out Task<PackageDownloadResult>? lifecycle)
            .Should()
            .BeTrue();

        await service.ReleasePausedDownloadAsync();

        lifecycle!.IsCompleted.Should().BeFalse();
        service.IsActive.Should().BeTrue();

        release.SetResult(PackageDownloadResult.Succeeded());
        await lifecycle;
    }

    [Fact]
    public void ReportProgress_ClampsActiveProgressAndRaisesChange()
    {
        var service = new LauncherPackageActivityService();
        service.TryBegin(
                "Download",
                out LauncherPackageActivityService.LauncherPackageActivityLease? lease)
            .Should()
            .BeTrue();
        int changeCount = 0;
        service.ActivityChanged += (_, _) => changeCount++;

        try
        {
            service.ReportProgress(125D);

            service.ProgressPercentage.Should().Be(100D);
            changeCount.Should().Be(1);
        }
        finally
        {
            lease?.Dispose();
        }
    }

    [Fact]
    public void ReportProgress_WhileIdle_IsIgnored()
    {
        var service = new LauncherPackageActivityService();
        int changeCount = 0;
        service.ActivityChanged += (_, _) => changeCount++;

        service.ReportProgress(50D);

        service.ProgressPercentage.Should().BeNull();
        changeCount.Should().Be(0);
    }

    [Fact]
    public async Task WaitForIdle_CompletesOnlyAfterLeaseReleasesCleanupAsync()
    {
        var service = new LauncherPackageActivityService();
        service.TryBegin(
                "Download",
                out LauncherPackageActivityService.LauncherPackageActivityLease? lease)
            .Should()
            .BeTrue();

        Task idleTask = service.WaitForIdleAsync();

        idleTask.IsCompleted.Should().BeFalse();
        service.IsActive.Should().BeTrue();
        lease?.Dispose();
        await idleTask;
        service.IsActive.Should().BeFalse();
        service.ActiveDisplayName.Should().BeEmpty();
        service.ProgressPercentage.Should().BeNull();
    }

}
