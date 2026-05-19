using System;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Core.Settings.Models;

public sealed record LauncherGamePreferencesSet
{
    public LauncherGamePreferences Generals { get; init; } = new();

    public LauncherGamePreferences ZeroHour { get; init; } = new();

    public LauncherGamePreferences Get(SupportedGame game)
    {
        return PerGame.Select(game, Generals, ZeroHour, nameof(game));
    }

    public LauncherGamePreferencesSet With(SupportedGame game, LauncherGamePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        return game switch
        {
            SupportedGame.Generals => this with { Generals = preferences },
            SupportedGame.ZeroHour => this with { ZeroHour = preferences },
            _ => throw PerGame.Unsupported(game, nameof(game))
        };
    }
}
