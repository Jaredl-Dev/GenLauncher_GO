using System;
using System.IO;
using GenLauncherGO.Features.Startup;

namespace GenLauncherGO.Features.Launching;

/// <summary>
///     Describes a game or World Builder process launch request.
/// </summary>
internal sealed record GameLaunchRequest
{
    private GameLaunchRequest(
        GameLaunchTargetKind targetKind,
        string executablePath,
        string? arguments)
    {
        if (!Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException("A launch requires a fully qualified executable path.", nameof(executablePath));
        }

        TargetKind = targetKind;
        ExecutablePath = LauncherFileSystemLayout.NormalizeExecutablePath(executablePath);
        Arguments = arguments ?? string.Empty;
    }

    public GameLaunchTargetKind TargetKind { get; }

    /// <summary>
    ///     Gets the absolute executable location; its parent is the working directory, independent of mod deployment.
    /// </summary>
    public string ExecutablePath { get; }

    public string Arguments { get; }

    public static GameLaunchRequest ForGameClient(
        string executablePath,
        string? arguments)
    {
        return new GameLaunchRequest(
            GameLaunchTargetKind.GameClient,
            executablePath,
            arguments);
    }

    public static GameLaunchRequest ForWorldBuilder(
        string executablePath,
        string? arguments)
    {
        return new GameLaunchRequest(
            GameLaunchTargetKind.WorldBuilder,
            executablePath,
            arguments);
    }
}
