using System;

namespace GenLauncherGO.Features.Startup;

/// <summary>
///     Reports whether launching the replacement process for an application restart succeeded.
/// </summary>
internal sealed record LauncherRestartResult
{
    private LauncherRestartResult(bool succeeded, string? errorMessage)
    {
        Succeeded = succeeded;
        ErrorMessage = errorMessage;
    }

    public bool Succeeded { get; }

    public string? ErrorMessage { get; }

    public static LauncherRestartResult Success { get; } = new(true, null);

    public static LauncherRestartResult Failure(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new LauncherRestartResult(false, errorMessage);
    }
}
