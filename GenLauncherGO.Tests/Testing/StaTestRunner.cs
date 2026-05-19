using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using GenLauncherGO.UI.Features.Startup;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
/// Runs Avalonia-dependent test code in an isolated headless UI session.
/// </summary>
internal static class StaTestRunner
{
    private static readonly object _sessionGate = new();

    private static readonly Lazy<HeadlessUnitTestSession> _session =
        new(() => HeadlessUnitTestSession.StartNew(typeof(LauncherAvaloniaApplication)));

    public static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (_sessionGate)
        {
            _session.Value.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    public static void Run(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (_sessionGate)
        {
            _session.Value.Dispatch(
                    async () =>
                    {
                        await action();
                        return true;
                    },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
    }

    public static TResult Run<TResult>(Func<TResult> function)
    {
        ArgumentNullException.ThrowIfNull(function);

        lock (_sessionGate)
        {
            return _session.Value.Dispatch(function, CancellationToken.None).GetAwaiter().GetResult();
        }
    }
}
