using System.Collections.Generic;
using System.Threading.Tasks;
using GenLauncherGO.Features.Startup;

namespace GenLauncherGO.Tests.Testing;

internal sealed class RecordingStartupDialogService : IStartupDialogService
{
    public List<string> Messages { get; } = [];
    public List<(string Title, string Message)> TitledMessages { get; } = [];

    public List<(string Title, string Message)> RetryCancelWarnings { get; } = [];

    public bool RetryResult { get; init; }

    public Task ShowMessageAsync(string message)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }

    public Task ShowMessageAsync(string title, string message)
    {
        Messages.Add(message);
        TitledMessages.Add((title, message));
        return Task.CompletedTask;
    }

    public Task<bool> ShowRetryCancelWarningAsync(string title, string message)
    {
        Messages.Add(message);
        RetryCancelWarnings.Add((title, message));
        return Task.FromResult(RetryResult);
    }
}
