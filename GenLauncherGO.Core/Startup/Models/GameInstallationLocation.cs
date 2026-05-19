using System;
using GenLauncherGO.Core.IO;

namespace GenLauncherGO.Core.Startup.Models;

public sealed record GameInstallationLocation
{
    public GameInstallationLocation(SupportedGame game, string directory)
    {
        PerGame.EnsureSupported(game, nameof(game));

        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Game = game;
        Directory = LexicalPath.NormalizeFullPath(directory);
    }

    public SupportedGame Game { get; }

    public string Directory { get; }
}
