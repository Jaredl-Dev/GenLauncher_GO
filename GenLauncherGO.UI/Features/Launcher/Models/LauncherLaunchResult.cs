namespace GenLauncherGO.UI.Features.Launcher.Models;

internal sealed record LauncherLaunchResult(
    bool LaunchStarted,
    bool ProcessSucceeded,
    LauncherLaunchFailureKind FailureKind)
{
    public static LauncherLaunchResult Stopped(LauncherLaunchFailureKind failureKind)
    {
        return new LauncherLaunchResult(false, false, failureKind);
    }

    public static LauncherLaunchResult Attempted(bool processSucceeded)
    {
        return new LauncherLaunchResult(true, processSucceeded, LauncherLaunchFailureKind.None);
    }
}
