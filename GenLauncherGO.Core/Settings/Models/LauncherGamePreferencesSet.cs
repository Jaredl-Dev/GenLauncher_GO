using System;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Core.Settings.Models;

public sealed record LauncherGamePreferencesSet
{
    public LauncherGamePreferences Generals { get; init; } = new();

    public LauncherGamePreferences ZeroHour { get; init; } = new();

    public LauncherGamePreferences Get(SupportedGame game)
    {
        return game switch
        {
            SupportedGame.Generals => Generals,
            SupportedGame.ZeroHour => ZeroHour,
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, "A supported game is required."),
        };
    }

    public LauncherGamePreferencesSet With(SupportedGame game, LauncherGamePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        return game switch
        {
            SupportedGame.Generals => this with { Generals = preferences },
            SupportedGame.ZeroHour => this with { ZeroHour = preferences },
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, "A supported game is required."),
        };
    }
}
