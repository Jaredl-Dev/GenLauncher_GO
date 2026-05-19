using System;

namespace GenLauncherGO.Core.Startup;

/// <summary>
///     Selects values keyed by <see cref="SupportedGame" /> and owns the rejection of unsupported values.
/// </summary>
/// <remarks>
///     Generals and Zero Hour stay explicit named members on the types that hold them: the launcher manages exactly
///     these two games, both are named in the persisted YAML, and every consumer wants them addressable by name. What
///     the pair sites genuinely shared was the rejection of <see cref="SupportedGame.Unknown" /> and its message,
///     which was written out at each site and now lives here once.
/// </remarks>
public static class PerGame
{
    /// <summary>
    ///     Returns the value belonging to a supported game.
    /// </summary>
    /// <remarks>
    ///     Both arguments are evaluated before the selection, so callers whose branches allocate — a record
    ///     <c>with</c> expression, for instance — should switch directly and throw <see cref="Unsupported" /> in the
    ///     default arm instead.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="game" /> is not supported.</exception>
    public static T Select<T>(SupportedGame game, T generals, T zeroHour, string paramName = "game")
    {
        return game switch
        {
            SupportedGame.Generals => generals,
            SupportedGame.ZeroHour => zeroHour,
            _ => throw Unsupported(game, paramName)
        };
    }

    /// <summary>
    ///     Rejects a game value the launcher does not manage.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="game" /> is not supported.</exception>
    public static void EnsureSupported(SupportedGame game, string paramName = "game")
    {
        if (game is not SupportedGame.Generals and not SupportedGame.ZeroHour)
        {
            throw Unsupported(game, paramName);
        }
    }

    /// <summary>
    ///     Creates the rejection for a game value the launcher does not manage.
    /// </summary>
    public static ArgumentOutOfRangeException Unsupported(SupportedGame game, string paramName = "game")
    {
        return new ArgumentOutOfRangeException(paramName, game, "A supported game is required.");
    }
}
