using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Core.Settings.Models;

public sealed record LauncherPreferences
{
    public LauncherInstallations Installations { get; init; } = new();

    public SupportedGame? LastSelectedGame { get; init; }

    public LauncherSharedPreferences Shared { get; init; } = new();

    public LauncherGamePreferencesSet Games { get; init; } = new();
}
