namespace GenLauncherGO.Features.Settings;

internal sealed record LauncherSharedPreferences
{
    public bool AutoDeleteOldVersions { get; init; }

    public bool HideLauncherAfterGameStart { get; init; }

    public bool EnableDiagnosticLogging { get; init; }

    public bool UseEnglishLanguage { get; init; }

    public bool HasShownRetailGenPatcherRecommendation { get; init; }
}
