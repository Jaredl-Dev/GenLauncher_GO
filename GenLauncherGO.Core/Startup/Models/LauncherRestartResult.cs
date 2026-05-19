namespace GenLauncherGO.Core.Startup.Models;

/// <summary>
/// Reports whether launching the replacement process for an application restart succeeded.
/// </summary>
public sealed record LauncherRestartResult
{
    private LauncherRestartResult(bool succeeded, string? errorMessage)
    {
        Succeeded = succeeded;
        ErrorMessage = errorMessage;
    }

    public bool Succeeded { get; }

    public string? ErrorMessage { get; }

    public static LauncherRestartResult Success { get; } = new(succeeded: true, errorMessage: null);

    public static LauncherRestartResult Failure(string errorMessage)
    {
        System.ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new LauncherRestartResult(succeeded: false, errorMessage);
    }
}
