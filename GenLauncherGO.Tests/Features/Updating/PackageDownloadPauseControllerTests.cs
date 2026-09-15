using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Features.Updating;

namespace GenLauncherGO.Tests.Features.Updating;

public sealed class PackageDownloadPauseControllerTests
{
    [Fact]
    public async Task PauseAndResume_AreIdempotentAndExposeCurrentStateAsync()
    {
        PackageDownloadPauseController controller = new();

        controller.IsPaused.Should().BeFalse();
        controller.WaitWhilePausedAsync(CancellationToken.None).AsTask()
            .IsCompletedSuccessfully.Should().BeTrue();
        controller.Resume().Should().BeFalse();
        controller.Pause().Should().BeTrue();
        controller.Pause().Should().BeFalse();
        controller.IsPaused.Should().BeTrue();

        Task waiter = controller.WaitWhilePausedAsync(CancellationToken.None).AsTask();
        waiter.IsCompleted.Should().BeFalse();
        controller.Resume().Should().BeTrue();
        controller.Resume().Should().BeFalse();
        await waiter.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        controller.IsPaused.Should().BeFalse();
    }

    [Fact]
    public async Task Resume_ReleasesEveryWaiterFromTheSamePauseAsync()
    {
        PackageDownloadPauseController controller = new();
        controller.Pause();
        Task[] waiters = Enumerable.Range(0, 16)
            .Select(_ => controller.WaitWhilePausedAsync(CancellationToken.None).AsTask())
            .ToArray();

        waiters.Should().OnlyContain(waiter => !waiter.IsCompleted);
        controller.Resume().Should().BeTrue();

        await Task.WhenAll(waiters).WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        waiters.Should().OnlyContain(waiter => waiter.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task CancelingOneWaiter_DoesNotResumeOrCancelOtherWaitersAsync()
    {
        PackageDownloadPauseController controller = new();
        controller.Pause();
        using CancellationTokenSource cancellation = new();
        Task canceledWaiter = controller.WaitWhilePausedAsync(cancellation.Token).AsTask();
        Task remainingWaiter = controller.WaitWhilePausedAsync(CancellationToken.None).AsTask();

        await cancellation.CancelAsync();

        Func<Task> canceled = () => canceledWaiter;
        await canceled.Should().ThrowAsync<OperationCanceledException>();
        controller.IsPaused.Should().BeTrue();
        remainingWaiter.IsCompleted.Should().BeFalse();
        controller.Resume().Should().BeTrue();
        await remainingWaiter.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ConcurrentTransitions_ReportExactlyOneStateChangeAsync()
    {
        PackageDownloadPauseController controller = new();

        bool[] pauseResults = await Task.WhenAll(
            Enumerable.Range(0, 32).Select(_ => Task.Run(controller.Pause)));
        bool[] resumeResults = await Task.WhenAll(
            Enumerable.Range(0, 32).Select(_ => Task.Run(controller.Resume)));

        pauseResults.Should().ContainSingle(changed => changed);
        resumeResults.Should().ContainSingle(changed => changed);
        controller.IsPaused.Should().BeFalse();
    }
}
