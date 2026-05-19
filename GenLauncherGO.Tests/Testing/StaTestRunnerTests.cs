using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace GenLauncherGO.Tests.Testing;

[Collection("Avalonia")]
public sealed class StaTestRunnerTests
{
    [Fact]
    public void Run_Action_ExecutesOnAvaloniaUiThread()
    {
        bool ranOnDispatcherThread = false;

        StaTestRunner.Run(() => { ranOnDispatcherThread = Dispatcher.UIThread.CheckAccess(); });

        ranOnDispatcherThread.Should().BeTrue();
    }

    [Fact]
    public void Run_AsyncAction_PreservesDispatcherAffinityAcrossAwait()
    {
        bool keptDispatcherAffinity = false;

        StaTestRunner.Run(async () =>
        {
            int dispatcherThreadId = Environment.CurrentManagedThreadId;
            var dispatchedThreadId = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(() =>
                dispatchedThreadId.TrySetResult(Environment.CurrentManagedThreadId));

            int callbackThreadId = await dispatchedThreadId.Task;

            keptDispatcherAffinity =
                Dispatcher.UIThread.CheckAccess() &&
                callbackThreadId == dispatcherThreadId &&
                Environment.CurrentManagedThreadId == dispatcherThreadId;
        });

        keptDispatcherAffinity.Should().BeTrue();
    }

    [Fact]
    public void Run_ActionThrows_PropagatesException()
    {
        static void FailingAction()
        {
            throw new InvalidOperationException("dispatched failure");
        }

        Action run = () => StaTestRunner.Run(FailingAction);

        run.Should().Throw<InvalidOperationException>().WithMessage("dispatched failure");
    }

    [Fact]
    public void Run_AsyncActionThrowsAfterAwait_PropagatesException()
    {
        static async Task FailingActionAsync()
        {
            await Task.Yield();

            throw new InvalidOperationException("dispatched failure after await");
        }

        Action run = () => StaTestRunner.Run(FailingActionAsync);

        run.Should().Throw<InvalidOperationException>().WithMessage("dispatched failure after await");
    }
}
