using System;
using System.Threading.Tasks;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Infrastructure.Launching.Support;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     A launched process the test decides the lifetime of, for the states the launcher only shows while a game is
///     running.
/// </summary>
internal sealed class ControllableGameProcessLaunchOperation : IGameProcessLaunchOperation
{
    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ControllableGameProcessLaunchOperation(string executableName = "generals.exe")
    {
        CurrentExecutableName = executableName;
        Completion = _completion.Task;
    }

    public string CurrentExecutableName { get; private set; }

    public Task<bool> Completion { get; set; }

    public event EventHandler? CurrentExecutableNameChanged;

    public void ForceClose()
    {
    }

    public void RaiseCurrentExecutableNameChanged(string executableName)
    {
        CurrentExecutableName = executableName;
        CurrentExecutableNameChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Complete(bool succeeded)
    {
        _completion.TrySetResult(succeeded);
    }
}

/// <summary>
///     The <see cref="IProcessFamilyLaunchOperation" /> counterpart, which reports elapsed run time rather than a
///     success flag.
/// </summary>
internal sealed class ControllableProcessFamilyLaunchOperation : IProcessFamilyLaunchOperation
{
    private readonly TaskCompletionSource<TimeSpan> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ControllableProcessFamilyLaunchOperation(string executableName = "generals.exe")
    {
        CurrentExecutableName = executableName;
        Completion = _completion.Task;
    }

    public string CurrentExecutableName { get; private set; }

    public Task<TimeSpan> Completion { get; set; }

    public int ForceCloseCount { get; private set; }

    public event EventHandler? CurrentExecutableNameChanged;

    public void ForceClose()
    {
        ForceCloseCount++;
    }

    public void RaiseCurrentExecutableNameChanged(string executableName)
    {
        CurrentExecutableName = executableName;
        CurrentExecutableNameChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Complete(TimeSpan runningDuration)
    {
        _completion.TrySetResult(runningDuration);
    }
}
