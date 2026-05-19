using System;
using System.Collections.Generic;
using GenLauncherGO.Core.Launching.Models;

namespace GenLauncherGO.Core.Settings.Models;

public sealed record LauncherGamePreferences
{
    public int LaunchesCount { get; init; }

    public string SelectedGameClient { get; init; } = string.Empty;

    public string SelectedWorldBuilder { get; init; } = string.Empty;

    public string GameArguments { get; init; } = string.Empty;

    public string WorldBuilderArguments { get; init; } = string.Empty;

    public double ModsListVerticalOffset { get; init; }

    /// <summary>
    ///     Gets the row the advertising tile occupies in the modification list. The tile is rebuilt from the remote
    ///     catalog on every start, so unlike a modification it has no persisted card of its own to carry its position.
    /// </summary>
    public int AdvertisingPositionInList { get; init; }

    public IReadOnlyList<LauncherCustomExecutable> CustomGameClients { get; init; } =
        Array.Empty<LauncherCustomExecutable>();

    public IReadOnlyList<LauncherCustomExecutable> CustomWorldBuilders { get; init; } =
        Array.Empty<LauncherCustomExecutable>();

    /// <summary>
    ///     Gets the custom executable registrations for one launch target.
    /// </summary>
    public IReadOnlyList<LauncherCustomExecutable> GetCustomExecutables(GameLaunchTargetKind targetKind)
    {
        return targetKind switch
        {
            GameLaunchTargetKind.GameClient => CustomGameClients,
            GameLaunchTargetKind.WorldBuilder => CustomWorldBuilders,
            _ => throw new ArgumentOutOfRangeException(nameof(targetKind), targetKind, "Unknown launch target.")
        };
    }

    /// <summary>
    ///     Gets the selected executable name for one launch target.
    /// </summary>
    public string GetSelectedExecutable(GameLaunchTargetKind targetKind)
    {
        return targetKind switch
        {
            GameLaunchTargetKind.GameClient => SelectedGameClient,
            GameLaunchTargetKind.WorldBuilder => SelectedWorldBuilder,
            _ => throw new ArgumentOutOfRangeException(nameof(targetKind), targetKind, "Unknown launch target.")
        };
    }

    /// <summary>
    ///     Replaces the custom executable registrations for one launch target.
    /// </summary>
    public LauncherGamePreferences WithCustomExecutables(
        GameLaunchTargetKind targetKind,
        IReadOnlyList<LauncherCustomExecutable> executables)
    {
        ArgumentNullException.ThrowIfNull(executables);

        return targetKind switch
        {
            GameLaunchTargetKind.GameClient => this with { CustomGameClients = executables },
            GameLaunchTargetKind.WorldBuilder => this with { CustomWorldBuilders = executables },
            _ => throw new ArgumentOutOfRangeException(nameof(targetKind), targetKind, "Unknown launch target.")
        };
    }

    /// <summary>
    ///     Replaces the selected executable name for one launch target.
    /// </summary>
    public LauncherGamePreferences WithSelectedExecutable(
        GameLaunchTargetKind targetKind,
        string executableName)
    {
        ArgumentNullException.ThrowIfNull(executableName);

        return targetKind switch
        {
            GameLaunchTargetKind.GameClient => this with { SelectedGameClient = executableName },
            GameLaunchTargetKind.WorldBuilder => this with { SelectedWorldBuilder = executableName },
            _ => throw new ArgumentOutOfRangeException(nameof(targetKind), targetKind, "Unknown launch target.")
        };
    }
}
