using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Core.Settings.Models;

public sealed record LauncherInstallations
{
    public string? Generals { get; init; }

    public string? ZeroHour { get; init; }

    public string? GetPath(SupportedGame game)
    {
        return PerGame.Select(game, Generals, ZeroHour, nameof(game));
    }

    public LauncherInstallations WithPath(SupportedGame game, string? path)
    {
        return game switch
        {
            SupportedGame.Generals => this with { Generals = path },
            SupportedGame.ZeroHour => this with { ZeroHour = path },
            _ => throw PerGame.Unsupported(game, nameof(game))
        };
    }

    /// <summary>
    ///     Resolves the preferred configured game, falling back when exactly one installation is available.
    /// </summary>
    public SupportedGame? ResolvePreferredGame(SupportedGame? preferredGame)
    {
        bool hasGenerals = !string.IsNullOrWhiteSpace(Generals);
        bool hasZeroHour = !string.IsNullOrWhiteSpace(ZeroHour);

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
