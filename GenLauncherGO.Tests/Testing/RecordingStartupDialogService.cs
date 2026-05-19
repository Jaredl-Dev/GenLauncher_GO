using System.Collections.Generic;
using System.Threading.Tasks;
using GenLauncherGO.UI.Features.Startup.Contracts;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Records the startup dialogs the launcher showed before the main dialog service exists.
/// </summary>
internal sealed class RecordingStartupDialogService : IStartupDialogService
{
    /// <summary>
    ///     Every message shown, whatever dialog carried it.
    /// </summary>
    public List<string> Messages { get; } = [];

    /// <summary>
    ///     The titled messages only, so a test can assert the title as well as the body.
    /// </summary>
    public List<(string Title, string Message)> TitledMessages { get; } = [];

    public List<(string Title, string Message)> RetryCancelWarnings { get; } = [];

    /// <summary>
    ///     The answer every retry prompt gets.
    /// </summary>
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
