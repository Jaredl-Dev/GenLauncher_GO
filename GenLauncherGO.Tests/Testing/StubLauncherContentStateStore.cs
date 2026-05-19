using System;
using System.Collections.Generic;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Mods.Contracts;
using GenLauncherGO.Infrastructure.Mods.Models;

namespace GenLauncherGO.Tests.Testing;

internal sealed class StubLauncherContentStateStore : ILauncherContentStateStore
{
    public LauncherContentState StateToLoad { get; set; } = new();

    public int LoadCallCount { get; private set; }

    public Dictionary<SupportedGame, LauncherContentState> StatesToLoadByGame { get; } = new();

    public List<LauncherPaths> LoadedPaths { get; } = new();

    public List<LauncherContentState> SavedStates { get; } = new();

    public List<LauncherPaths> SavedPaths { get; } = new();

    public Action<LauncherContentState>? SaveHandler { get; set; }

    public LauncherContentState Load(LauncherPaths paths)
    {
        LoadCallCount++;
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
