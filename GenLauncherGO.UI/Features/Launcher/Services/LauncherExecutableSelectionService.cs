using System;
using System.Collections.Generic;
using System.Linq;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.UI.Features.Launcher.Services;

/// <summary>
///     Builds executable selector options for the launcher UI.
/// </summary>
internal sealed class LauncherExecutableSelectionService
{
    private readonly IGameExecutableDiscoveryService _gameExecutableDiscoveryService;

    private readonly LauncherRuntimeContext _launcherContext;

    private readonly ILauncherPreferencesService _preferencesService;

    private readonly ILauncherStringLocalizer _stringLocalizer;

    public LauncherExecutableSelectionService(
        IGameExecutableDiscoveryService gameExecutableDiscoveryService,
        LauncherRuntimeContext launcherContext,
        ILauncherPreferencesService preferencesService,
        ILauncherStringLocalizer stringLocalizer)
    {
        _gameExecutableDiscoveryService = gameExecutableDiscoveryService ??
                                          throw new ArgumentNullException(nameof(gameExecutableDiscoveryService));
        _launcherContext = launcherContext ?? throw new ArgumentNullException(nameof(launcherContext));
        _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
    }

    /// <summary>
    ///     Gets selectable executable options for one launch target in the current managed game.
    /// </summary>
    public IReadOnlyList<ExecutableOption> GetOptions(GameLaunchTargetKind targetKind)
    {
        LauncherGamePreferences preferences = GetActiveGamePreferences();
        return targetKind switch
        {
            GameLaunchTargetKind.GameClient => CreateGameClientOptions(preferences),
            GameLaunchTargetKind.WorldBuilder => CreateWorldBuilderOptions(preferences),
            _ => throw new ArgumentOutOfRangeException(nameof(targetKind), targetKind, "Unknown launch target.")
        };
    }

    private IReadOnlyList<ExecutableOption> CreateGameClientOptions(LauncherGamePreferences preferences)
    {
        IReadOnlyList<BuiltInExecutable> builtIns =
            _gameExecutableDiscoveryService.GetGameClients();
        var options = builtIns
            .Select((executable, index) => new ExecutableOption(
                GetGameClientDisplayName(executable.Kind),
                executable.ExecutableName,
                executable.IsAvailable,
                true,
                isGeneralsOnline: executable.Kind == BuiltInExecutableKind.GeneralsOnline,
                isRetail: executable.Kind == BuiltInExecutableKind.Retail,
                groupDisplayName: _stringLocalizer["BuiltInExecutables"],
                showGroupHeader: index == 0))
            .ToList();

        options.AddRange(CreateCustomOptions(preferences.GetCustomExecutables(GameLaunchTargetKind.GameClient)));
        return options;
    }

    /// <summary>
    ///     Selects a saved executable when present, otherwise the first available option or first option.
    /// </summary>
    public static ExecutableOption? SelectPreferredOption(
        IReadOnlyList<ExecutableOption> options,
        string? selectedExecutableName)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.FirstOrDefault(option => string.Equals(
                   option.ExecutableName,
                   selectedExecutableName,
                   StringComparison.OrdinalIgnoreCase))
               ?? options.FirstOrDefault(option => option.IsAvailable)
               ?? options.FirstOrDefault();
    }

    private IReadOnlyList<ExecutableOption> CreateWorldBuilderOptions(LauncherGamePreferences preferences)
    {
        IReadOnlyList<BuiltInExecutable> builtIns =
            _gameExecutableDiscoveryService.GetWorldBuilders();
        var options = builtIns
            .Select((executable, index) => new ExecutableOption(
                GetWorldBuilderDisplayName(executable.Kind),
                executable.ExecutableName,
                executable.IsAvailable,
                true,
                groupDisplayName: _stringLocalizer["BuiltInExecutables"],
                showGroupHeader: index == 0))
            .ToList();

        options.AddRange(CreateCustomOptions(preferences.GetCustomExecutables(GameLaunchTargetKind.WorldBuilder)));

        return options;
    }

    private IEnumerable<ExecutableOption> CreateCustomOptions(
        IReadOnlyList<LauncherCustomExecutable> executables)
    {
        return executables.Select((executable, index) => new ExecutableOption(
            executable.DisplayName,
            executable.ExecutableName,
            _gameExecutableDiscoveryService.IsExecutableAvailable(executable.ExecutableName),
            false,
            groupDisplayName: _stringLocalizer["CustomExecutables"],
            showGroupHeader: index == 0));
    }

    private LauncherGamePreferences GetActiveGamePreferences()
    {
        return _preferencesService.Current.Games.Get(_launcherContext.CurrentlyManagedGame);
    }

    private string GetGameClientDisplayName(BuiltInExecutableKind kind)
    {
        return kind switch
        {
            BuiltInExecutableKind.Community => _stringLocalizer["CommunityGameClientDisplayName"],
            BuiltInExecutableKind.Retail => _stringLocalizer["RetailGameClientDisplayName"],
            BuiltInExecutableKind.GeneralsOnline => _stringLocalizer["GeneralsOnlineGameClientDisplayName"],
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown game client executable kind.")
        };
    }

    private string GetWorldBuilderDisplayName(BuiltInExecutableKind kind)
    {
        return kind switch
        {
            BuiltInExecutableKind.Community => _stringLocalizer["SuperHackersWorldBuilder"],
            BuiltInExecutableKind.Retail => _stringLocalizer["RetailWorldBuilder"],
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown World Builder executable kind.")
        };
    }
}
