using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace GenLauncherGO.Tests.Testing;

public sealed class StaTestRunnerTests
{
    [Fact]
    public void RunExecutesActionOnAvaloniaUiThread()
    {
        StaTestRunner.Run(() =>
        {
            Dispatcher.UIThread.CheckAccess().Should().BeTrue();
        });
    }

    [Fact]
    public void RunPumpsAvaloniaDispatcherAndPreservesAffinityAcrossAwait()
    {
        StaTestRunner.Run(async () =>
        {
            int dispatcherThreadId = Environment.CurrentManagedThreadId;
            var dispatchedThreadId = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            Dispatcher.UIThread.Post(() =>
                dispatchedThreadId.TrySetResult(Environment.CurrentManagedThreadId));

            int callbackThreadId = await dispatchedThreadId.Task;

            SynchronizationContext.Current.Should().NotBeNull();
            Dispatcher.UIThread.CheckAccess().Should().BeTrue();
            callbackThreadId.Should().Be(dispatcherThreadId);
            Environment.CurrentManagedThreadId.Should().Be(dispatcherThreadId);
        });
    }
}
