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
    public void TryBeginRejectsSecondActivityUntilLeaseReleasesLifecycle()
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
    public async Task ConcurrentDownloadStartsAtomicallyPublishOneOwnerAsync()
    {
        var service = new LauncherPackageActivityService();
        using Barrier startBarrier = new(2);
        TaskCompletionSource<PackageDownloadResult> release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        object[] owners = { new(), new() };
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
    public async Task OwningDownloadCanPauseAndResumeWithoutEndingLifecycleAsync()
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
    public void ReportProgressClampsActiveProgressAndRaisesChange()
    {
        var service = new LauncherPackageActivityService();
        int changeCount = 0;
        service.ActivityChanged += (_, _) => changeCount++;
        service.TryBegin(
                "Download",
                out LauncherPackageActivityService.LauncherPackageActivityLease? lease)
            .Should()
            .BeTrue();

        try
        {
            service.ReportProgress(125D);

            service.ProgressPercentage.Should().Be(100D);
            changeCount.Should().Be(2);
        }
        finally
        {
            lease?.Dispose();
        }
    }

    [Fact]
    public async Task WaitForIdleCompletesOnlyAfterLeaseReleasesCleanupAsync()
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
