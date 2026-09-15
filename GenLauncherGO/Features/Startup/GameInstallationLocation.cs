using System;
using GenLauncherGO.Shared.IO;

namespace GenLauncherGO.Features.Startup;

internal sealed record GameInstallationLocation
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
