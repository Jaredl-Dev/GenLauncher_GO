using System;
using GenLauncherGO.Core.IO;

namespace GenLauncherGO.Core.Startup.Models;

public sealed record GameInstallationLocation
{
    public GameInstallationLocation(SupportedGame game, string directory)
    {
        if (game is not SupportedGame.Generals and not SupportedGame.ZeroHour)
        {
            throw new ArgumentOutOfRangeException(nameof(game), game, "A supported game is required.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Game = game;
        Directory = LexicalPath.NormalizeFullPath(directory);
    }

    public SupportedGame Game { get; }

    public string Directory { get; }
}
