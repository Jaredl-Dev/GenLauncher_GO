using System;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.UI.Features.Startup.Models;

/// <summary>
/// Identifies whether standalone setup completed and the validated game session that should start.
/// </summary>
internal sealed record StandaloneStartupResult
{
    private StandaloneStartupResult(bool canStart, SupportedGame game, string? gameDirectory)
    {
        CanStart = canStart;
        Game = game;
        GameDirectory = gameDirectory;
    }

    public static StandaloneStartupResult Canceled { get; } =
        new(false, SupportedGame.Unknown, null);

    public bool CanStart { get; }

    public SupportedGame Game { get; }

    public string? GameDirectory { get; }

    public static StandaloneStartupResult Ready(SupportedGame game, string gameDirectory)
    {
        if (game is not SupportedGame.Generals and not SupportedGame.ZeroHour)
        {
            throw new ArgumentOutOfRangeException(nameof(game), game, "A supported game is required.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        return new StandaloneStartupResult(true, game, gameDirectory);
    }
}
