using System;
using System.Collections.Generic;

namespace GenLauncherGO.Core.Settings.Models;

public sealed record LauncherGamePreferences
{
    public int LaunchesCount { get; init; }

    public string SelectedGameClient { get; init; } = string.Empty;

    public string SelectedWorldBuilder { get; init; } = string.Empty;

    public string GameArguments { get; init; } = string.Empty;

    public string WorldBuilderArguments { get; init; } = string.Empty;

    public IReadOnlyList<LauncherCustomExecutable> CustomGameClients { get; init; } =
        Array.Empty<LauncherCustomExecutable>();

    public IReadOnlyList<LauncherCustomExecutable> CustomWorldBuilders { get; init; } =
        Array.Empty<LauncherCustomExecutable>();
}
