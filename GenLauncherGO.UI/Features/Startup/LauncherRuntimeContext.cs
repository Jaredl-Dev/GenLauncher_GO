using System;
using Avalonia;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.UI.Features.Startup;

/// <summary>
///     Stores mutable runtime state shared by launcher startup and Avalonia views.
/// </summary>
internal sealed class LauncherRuntimeContext
{
    public LauncherRuntimeContext(LauncherRuntimePathContext runtimePaths, string currentLauncherVersion)
    {
        RuntimePaths = runtimePaths ?? throw new ArgumentNullException(nameof(runtimePaths));
        CurrentLauncherVersion = string.IsNullOrWhiteSpace(currentLauncherVersion)
            ? LauncherApplicationDefaults.UnavailableLauncherVersion
            : currentLauncherVersion;
    }

    public string CurrentLauncherVersion { get; }

    /// <summary>
    ///     Gets or sets the active launcher theme.
    /// </summary>
    /// <remarks>
    ///     Assigning also publishes the theme to application-scoped resources, so every open window follows through
    ///     <c>DynamicResource</c> without being told. This is what makes the assignment the single place a theme can
    ///     change; windows that deliberately preview a different palette shadow it with their own resources instead.
    ///     Publishing is skipped when no Avalonia application is running, which is the case in plain unit tests.
    /// </remarks>
    public ColorsInfo Colors
    {
        get => field ??
               throw new InvalidOperationException("Launcher theme has not been initialized.");
        set
        {
            field = value ?? throw new ArgumentNullException(nameof(value));
            if (Application.Current is { } application)
            {
                LauncherThemeResourceApplier.Apply(application.Resources, value);
            }
        }
    }

    /// <summary>
    ///     Gets the resolved launcher paths for the active session.
    /// </summary>
    public LauncherPaths LauncherPaths => RuntimePaths.ActivePaths;

    /// <summary>
    ///     Gets the active-path authority shared by path-sensitive services.
    /// </summary>
    public LauncherRuntimePathContext RuntimePaths { get; }

    /// <summary>
    ///     Gets the standalone launcher's shared storage paths.
    /// </summary>
    public LauncherStoragePaths StoragePaths => RuntimePaths.StoragePaths;

    public string? ModsRepositoryUrl { get; set; }

    public SupportedGame CurrentlyManagedGame => LauncherPaths.Game;

    public bool Connected { get; set; }

    /// <summary>
    ///     Applies the validated active game's repository and visual defaults as one session update.
    /// </summary>
    public void ApplyActiveGameDefaults()
    {
        SupportedGame managedGame = LauncherPaths.Game;
        ModsRepositoryUrl = managedGame switch
        {
            SupportedGame.ZeroHour => LauncherApplicationDefaults.ZeroHourRepositoryUrl,
            SupportedGame.Generals => LauncherApplicationDefaults.GeneralsRepositoryUrl,
            _ => null
        };

        Colors = LauncherThemePresets.Create(managedGame);
    }
}
