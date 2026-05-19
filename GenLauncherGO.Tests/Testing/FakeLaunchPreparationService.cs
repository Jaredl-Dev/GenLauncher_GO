using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Records launch preparation and can hold <see cref="Prepare" /> open, which is how a test observes the
///     launcher's busy state without racing a real deployment.
/// </summary>
internal sealed class FakeLaunchPreparationService : ILaunchPreparationService
{
    private TaskCompletionSource _resume = CreateResumedSource();

    public List<LaunchPreparationRequest> PrepareRequests { get; } = [];

    public List<LauncherPaths> CleanupRequests { get; } = [];

    /// <summary>
    ///     Completes once <see cref="Prepare" /> has been entered.
    /// </summary>
    public TaskCompletionSource PrepareStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool PrepareResult { get; init; } = true;

    public bool CleanupResult { get; init; } = true;

    /// <summary>
    ///     Holds the next <see cref="Prepare" /> call until <see cref="Resume" /> is called.
    /// </summary>
    public void Pause()
    {
        _resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Resume()
    {
        _resume.TrySetResult();
    }

    public bool Prepare(LaunchPreparationRequest request, CancellationToken cancellationToken)
    {
        PrepareRequests.Add(request);
        PrepareStarted.TrySetResult();
        _resume.Task.Wait(cancellationToken);
        return PrepareResult;
    }

    public bool Cleanup(LauncherPaths paths, CancellationToken cancellationToken)
    {
        CleanupRequests.Add(paths);
        return CleanupResult;
    }

    public bool Recover(LauncherPaths paths, CancellationToken cancellationToken)
    {
        return true;
    }

    private static TaskCompletionSource CreateResumedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}
