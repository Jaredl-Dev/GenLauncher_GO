namespace GenLauncherGO.Core.Settings.Models;

public sealed record LauncherSharedPreferences
{
    public bool AutoDeleteOldVersions { get; init; }

    public bool HideLauncherAfterGameStart { get; init; }

    public bool UseEnglishLanguage { get; init; }
}
