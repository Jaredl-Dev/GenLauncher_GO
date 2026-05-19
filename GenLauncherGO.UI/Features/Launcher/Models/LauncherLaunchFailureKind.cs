namespace GenLauncherGO.UI.Features.Launcher.Models;

internal enum LauncherLaunchFailureKind
{
    None,

    AlreadyRunning,

    VerificationAlreadyRunning,

    VerificationCanceled,

    PreparationFailed
}
