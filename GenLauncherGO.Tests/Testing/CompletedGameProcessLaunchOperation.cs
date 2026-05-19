using System;
using System.Threading.Tasks;
using GenLauncherGO.Core.Launching.Contracts;

namespace GenLauncherGO.Tests.Testing;

internal sealed class CompletedGameProcessLaunchOperation : IGameProcessLaunchOperation
{
    public CompletedGameProcessLaunchOperation(bool succeeded, string executableName)
    {
        CurrentExecutableName = executableName;
        Completion = Task.FromResult(succeeded);
    }

    public string CurrentExecutableName { get; }

    public Task<bool> Completion { get; }

    public event EventHandler? CurrentExecutableNameChanged
    {
        add { }
        remove { }
    }

    public void ForceClose()
    {
    }
}
