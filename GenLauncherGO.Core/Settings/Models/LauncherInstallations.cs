using System;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Core.Settings.Models;

public sealed record LauncherInstallations
{
    public string? Generals { get; init; }

    public string? ZeroHour { get; init; }

    public string? GetPath(SupportedGame game)
    {
        return game switch
        {
            SupportedGame.Generals => Generals,
            SupportedGame.ZeroHour => ZeroHour,
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, "A supported game is required."),
        };
    }

    public LauncherInstallations WithPath(SupportedGame game, string? path)
    {
        return game switch
        {
            SupportedGame.Generals => this with { Generals = path },
            SupportedGame.ZeroHour => this with { ZeroHour = path },
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, "A supported game is required."),
        };
    }

    /// <summary>
    /// Resolves the preferred configured game, falling back when exactly one installation is available.
    /// </summary>
    public SupportedGame? ResolvePreferredGame(SupportedGame? preferredGame)
    {
        bool hasGenerals = !String.IsNullOrWhiteSpace(Generals);
        bool hasZeroHour = !String.IsNullOrWhiteSpace(ZeroHour);

        if (preferredGame == SupportedGame.Generals && hasGenerals)
        {
            return SupportedGame.Generals;
        }

        if (preferredGame == SupportedGame.ZeroHour && hasZeroHour)
        {
            return SupportedGame.ZeroHour;
        }

        if (hasGenerals == hasZeroHour)
        {
            return null;
        }

        return hasGenerals ? SupportedGame.Generals : SupportedGame.ZeroHour;
    }
}
