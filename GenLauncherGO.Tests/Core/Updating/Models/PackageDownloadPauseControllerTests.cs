using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Updating.Models;

namespace GenLauncherGO.Tests.Core.Updating.Models;

public sealed class PackageDownloadPauseControllerTests
{
    [Fact]
    public void WaitWhilePausedAsync_WithoutController_CompletesImmediately()
    {
        ValueTask wait = PackageDownloadPauseController.WaitWhilePausedAsync(null, CancellationToken.None);

        wait.IsCompletedSuccessfully.Should().BeTrue();
    }

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
        await waiter.WaitAsync(TestTimeouts.Wait);
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

        await Task.WhenAll(waiters).WaitAsync(TestTimeouts.Wait);
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
        await remainingWaiter.WaitAsync(TestTimeouts.Wait);
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
