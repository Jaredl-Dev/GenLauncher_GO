using System;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Core.Launching.Models;

/// <summary>
///     Describes a game or World Builder process launch request.
/// </summary>
public sealed record GameLaunchRequest
{
    private GameLaunchRequest(
        GameLaunchTargetKind targetKind,
        string gameDirectory,
        string executableName,
        string? arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);

        TargetKind = targetKind;
        GameDirectory = LexicalPath.NormalizeFullPath(gameDirectory);
        ExecutableName = LauncherFileSystemLayout.NormalizeExecutableFileName(executableName);
        Arguments = arguments ?? string.Empty;
    }

    public GameLaunchTargetKind TargetKind { get; }

    public string GameDirectory { get; }

    public string ExecutableName { get; }

    public string Arguments { get; }

    public static GameLaunchRequest ForGameClient(
        string gameDirectory,
        string executableName,
        string? arguments)
    {
        return new GameLaunchRequest(
            GameLaunchTargetKind.GameClient,
            gameDirectory,
            executableName,
            arguments);
    }

    public static GameLaunchRequest ForWorldBuilder(
        string gameDirectory,
        string executableName,
        string? arguments)
    {
        return new GameLaunchRequest(
            GameLaunchTargetKind.WorldBuilder,
            gameDirectory,
            executableName,
            arguments);
    }
}
