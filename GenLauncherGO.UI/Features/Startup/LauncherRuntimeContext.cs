using System;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.UI.Features.Startup;

/// <summary>
/// Stores mutable runtime state shared by launcher startup and Avalonia views.
/// </summary>
internal sealed class LauncherRuntimeContext
{
    private readonly LauncherRuntimePathContext _runtimePaths;
    private bool _connected;
    private ColorsInfo? _colors;

    public LauncherRuntimeContext(LauncherRuntimePathContext runtimePaths, string currentLauncherVersion)
    {
        _runtimePaths = runtimePaths ?? throw new ArgumentNullException(nameof(runtimePaths));
        CurrentLauncherVersion = string.IsNullOrWhiteSpace(currentLauncherVersion)
            ? "Failed to retrieve"
            : currentLauncherVersion;
    }

    public string CurrentLauncherVersion { get; }

    public ColorsInfo Colors
    {
        get => _colors ??
               throw new InvalidOperationException("Launcher theme has not been initialized.");
        set => _colors = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets the resolved launcher paths for the active session.
    /// </summary>
    public LauncherPaths LauncherPaths => _runtimePaths.ActivePaths;

    /// <summary>
    /// Gets the active-path authority shared by path-sensitive services.
    /// </summary>
    public LauncherRuntimePathContext RuntimePaths => _runtimePaths;

    /// <summary>
    /// Gets the standalone launcher's shared storage paths.
    /// </summary>
    public LauncherStoragePaths StoragePaths => _runtimePaths.StoragePaths;

    public string? ModsRepositoryUrl { get; set; }

    public SupportedGame CurrentlyManagedGame => LauncherPaths.Game;

    public bool Connected
    {
        get => _connected;
        set => _connected = value;
    }

    /// <summary>
    /// Applies the validated active game's repository and visual defaults as one session update.
    /// </summary>
    public void ApplyActiveGameDefaults()
    {
        SupportedGame managedGame = LauncherPaths.Game;
        ModsRepositoryUrl = managedGame switch
        {
            SupportedGame.ZeroHour => LauncherApplicationDefaults.ZeroHourRepositoryUrl,
            SupportedGame.Generals => LauncherApplicationDefaults.GeneralsRepositoryUrl,
            _ => null,
        };

        Colors = LauncherThemePresets.Create(managedGame);
    }
}
