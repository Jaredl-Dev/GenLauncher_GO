using System;
using System.Collections.Generic;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Mods.Contracts;
using GenLauncherGO.Infrastructure.Mods.Models;

namespace GenLauncherGO.Tests.Testing;

internal sealed class RecordingLauncherContentStateStore : ILauncherContentStateStore
{
    public LauncherContentState StateToLoad { get; set; } = new();

    public Dictionary<SupportedGame, LauncherContentState> StatesToLoadByGame { get; } = [];

    public List<LauncherPaths> LoadedPaths { get; } = [];

    public List<LauncherContentState> SavedStates { get; } = [];

    public List<LauncherPaths> SavedPaths { get; } = [];

    public Action<LauncherContentState>? SaveHandler { get; set; }

    public LauncherContentState Load(LauncherPaths paths)
    {
        LoadedPaths.Add(paths);
        return StatesToLoadByGame.TryGetValue(paths.Game, out LauncherContentState? state)
            ? state
            : StateToLoad;
    }

    public void Save(LauncherPaths paths, LauncherContentState state)
    {
        SavedPaths.Add(paths);
        SavedStates.Add(state);
        SaveHandler?.Invoke(state);
    }
}
