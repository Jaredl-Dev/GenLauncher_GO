using GenLauncherGO.Features.Startup;

namespace GenLauncherGO.Features.Settings;

internal sealed record LauncherPreferences
{
    public LauncherInstallations Installations { get; init; } = new();

    public SupportedGame? LastSelectedGame { get; init; }

    public LauncherSharedPreferences Shared { get; init; } = new();

    public LauncherGamePreferencesSet Games { get; init; } = new();
}
