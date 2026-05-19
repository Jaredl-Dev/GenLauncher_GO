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
/// Builds executable selector options for the launcher UI.
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
    /// Gets selectable game client options for the current managed game.
    /// </summary>
    public IReadOnlyList<ExecutableOption> GetGameClientOptions()
    {
        IReadOnlyList<GameClientExecutable> builtIns =
            _gameExecutableDiscoveryService.GetGameClients();
        var options = builtIns
            .Select((executable, index) => new ExecutableOption(
                GetGameClientDisplayName(executable.Kind),
                executable.ExecutableName,
                executable.IsAvailable,
                isBuiltIn: true,
                isGeneralsOnline: executable.Kind == GameClientExecutableKind.GeneralsOnline,
                groupDisplayName: _stringLocalizer["BuiltInExecutables"],
                showGroupHeader: index == 0))
            .ToList();

        options.AddRange(GetActiveGamePreferences().CustomGameClients.Select((executable, index) =>
            new ExecutableOption(
                executable.DisplayName,
                executable.ExecutableName,
                _gameExecutableDiscoveryService.IsExecutableAvailable(executable.ExecutableName),
                isBuiltIn: false,
                groupDisplayName: _stringLocalizer["CustomExecutables"],
                showGroupHeader: index == 0)));
        return options;
    }

    /// <summary>
    /// Selects the preferred game client option.
    /// </summary>
    public ExecutableOption? SelectGameClientOption(
        IReadOnlyList<ExecutableOption> options,
        string? selectedExecutableName)
    {
        ArgumentNullException.ThrowIfNull(options);

        return SelectExecutableOption(options, selectedExecutableName);
    }

    /// <summary>
    /// Gets selectable World Builder options for the current managed game.
    /// </summary>
    public IReadOnlyList<ExecutableOption> GetWorldBuilderOptions()
    {
        IReadOnlyList<WorldBuilderExecutable> builtIns =
            _gameExecutableDiscoveryService.GetWorldBuilders();
        var options = builtIns
            .Select((executable, index) => new ExecutableOption(
                GetWorldBuilderDisplayName(executable.Kind),
                executable.ExecutableName,
                executable.IsAvailable,
                isBuiltIn: true,
                groupDisplayName: _stringLocalizer["BuiltInExecutables"],
                showGroupHeader: index == 0))
            .ToList();

        options.AddRange(GetActiveGamePreferences().CustomWorldBuilders.Select((executable, index) =>
            new ExecutableOption(
                executable.DisplayName,
                executable.ExecutableName,
                _gameExecutableDiscoveryService.IsExecutableAvailable(executable.ExecutableName),
                isBuiltIn: false,
                groupDisplayName: _stringLocalizer["CustomExecutables"],
                showGroupHeader: index == 0)));

        return options;
    }

    /// <summary>
    /// Selects the preferred World Builder option.
    /// </summary>
    public ExecutableOption? SelectWorldBuilderOption(
        IReadOnlyList<ExecutableOption> options,
        string? selectedExecutableName)
    {
        ArgumentNullException.ThrowIfNull(options);

        return SelectExecutableOption(options, selectedExecutableName);
    }

    private LauncherGamePreferences GetActiveGamePreferences()
    {
        return _preferencesService.Current.Games.Get(_launcherContext.CurrentlyManagedGame);
    }

    private static ExecutableOption? SelectExecutableOption(
        IReadOnlyList<ExecutableOption> options,
        string? selectedExecutableName)
    {
        return options.FirstOrDefault(option => string.Equals(
                   option.ExecutableName,
                   selectedExecutableName,
                   StringComparison.OrdinalIgnoreCase))
               ?? options.FirstOrDefault(option => option.IsAvailable)
               ?? options.FirstOrDefault();
    }

    private string GetGameClientDisplayName(GameClientExecutableKind kind)
    {
        return kind == GameClientExecutableKind.GeneralsOnline
            ? _stringLocalizer["GeneralsOnlineClient"]
            : _stringLocalizer["SuperHackersClient"];
    }

    private string GetWorldBuilderDisplayName(WorldBuilderExecutableKind kind)
    {
        return kind == WorldBuilderExecutableKind.Vanilla
            ? _stringLocalizer["VanillaWorldBuilder"]
            : _stringLocalizer["SuperHackersWorldBuilder"];
    }
}
