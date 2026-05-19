using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Remote;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Exceptions;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Core.Startup.Models;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Shared.Localization;
using GenLauncherGO.UI.Shared.Themes;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.UI.Features.Launcher.Services;

/// <summary>
///     Owns restartless game-session switching, cleanup, initialization, persistence, and rollback.
/// </summary>
internal sealed class LauncherGameSessionCoordinator
{
    private readonly ILauncherContentCatalog _catalog;
    private readonly IRemoteConnectionProbe _connectionProbe;
    private readonly ILauncherDialogService _dialogService;
    private readonly IGameInstallationService _installationService;
    private readonly LauncherLaunchCoordinator _launchCoordinator;
    private readonly ILaunchPreparationService _launchPreparationService;
    private readonly ILogger<LauncherGameSessionCoordinator> _logger;
    private readonly LauncherPackageActivityService _packageActivityService;
    private readonly ILauncherPathResolver _pathResolver;
    private readonly ILauncherPreferencesService _preferencesService;
    private readonly LauncherRuntimeContext _runtimeContext;
    private readonly ILauncherStringLocalizer _stringLocalizer;

    public LauncherGameSessionCoordinator(
        LauncherRuntimeContext runtimeContext,
        ILauncherPreferencesService preferencesService,
        IGameInstallationService installationService,
        ILauncherPathResolver pathResolver,
        ILaunchPreparationService launchPreparationService,
        IRemoteConnectionProbe connectionProbe,
        ILauncherContentCatalog catalog,
        LauncherPackageActivityService packageActivityService,
        LauncherLaunchCoordinator launchCoordinator,
        ILauncherDialogService dialogService,
        ILauncherStringLocalizer stringLocalizer,
        ILogger<LauncherGameSessionCoordinator> logger)
    {
        _runtimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
        _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        _installationService = installationService ?? throw new ArgumentNullException(nameof(installationService));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _launchPreparationService = launchPreparationService ??
                                    throw new ArgumentNullException(nameof(launchPreparationService));
        _connectionProbe = connectionProbe ?? throw new ArgumentNullException(nameof(connectionProbe));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _packageActivityService = packageActivityService ??
                                  throw new ArgumentNullException(nameof(packageActivityService));
        _launchCoordinator = launchCoordinator ?? throw new ArgumentNullException(nameof(launchCoordinator));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> SwitchGameAsync(
        SupportedGame game,
        Window owner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!await CanChangeSessionAsync(owner))
        {
            return false;
        }

        LauncherPreferences previous = _preferencesService.Current;
        return await ApplySessionAsync(
            game,
            previous,
            previous.Installations,
            previous.LastSelectedGame,
            owner,
            cancellationToken);
    }

    public async Task<bool> ApplyInstallationChangesAsync(
        LauncherInstallations previousInstallations,
        SupportedGame? previousSelectedGame,
        Window owner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(previousInstallations);
        ArgumentNullException.ThrowIfNull(owner);
        if (!await CanChangeSessionAsync(owner))
        {
            RollBackPreferences(previousInstallations, previousSelectedGame);
            return false;
        }

        LauncherPreferences current = _preferencesService.Current;
        SupportedGame activeGame = _runtimeContext.CurrentlyManagedGame;
        string? activeConfiguredPath = current.Installations.GetPath(activeGame);
        SupportedGame targetGame = current.Installations.ResolvePreferredGame(activeGame) ??
                                   SupportedGame.Unknown;

        if (targetGame == activeGame &&
            LexicalPath.AreEquivalent(activeConfiguredPath, _runtimeContext.LauncherPaths.GameDirectory))
        {
            return true;
        }

        return await ApplySessionAsync(
            targetGame,
            current,
            previousInstallations,
            previousSelectedGame,
            owner,
            cancellationToken);
    }

    private async Task<bool> ApplySessionAsync(
        SupportedGame game,
        LauncherPreferences currentPreferences,
        LauncherInstallations rollbackInstallations,
        SupportedGame? rollbackSelectedGame,
        Window owner,
        CancellationToken cancellationToken)
    {
        if (game is not SupportedGame.Generals and not SupportedGame.ZeroHour)
        {
            return false;
        }

        string? configuredPath = currentPreferences.Installations.GetPath(game);
        GameInstallationValidationResult validation = _installationService.Validate(
            game,
            configuredPath,
            _runtimeContext.StoragePaths.ExecutableDirectory);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalPath))
        {
            await ShowSwitchFailureAsync(owner, _stringLocalizer["InstallationPathUnavailable"]);
            RollBackPreferences(rollbackInstallations, rollbackSelectedGame);
            return false;
        }

        LauncherPaths oldPaths = _runtimeContext.RuntimePaths.ActivePaths;
        ColorsInfo oldColors = _runtimeContext.Colors;
        string? oldRepositoryUrl = _runtimeContext.ModsRepositoryUrl;
        bool oldConnected = _runtimeContext.Connected;
        LauncherPaths newPaths = _runtimeContext.StoragePaths.CreateGamePaths(game, validation.CanonicalPath);

        if (oldPaths.Game == newPaths.Game &&
            LexicalPath.AreEquivalent(oldPaths.GameDirectory, newPaths.GameDirectory))
        {
            return await TryPersistSelectedGameAsync(game, rollbackInstallations, rollbackSelectedGame, owner);
        }

        if (!await TryPersistSelectedGameAsync(game, rollbackInstallations, rollbackSelectedGame, owner))
        {
            return false;
        }

        bool switched = false;
        try
        {
            _catalog.SaveLauncherData();
            bool cleanupSucceeded = await Task.Run(
                () => _launchPreparationService.Cleanup(oldPaths, cancellationToken),
                cancellationToken);
            if (!cleanupSucceeded)
            {
                throw new InvalidOperationException(_stringLocalizer["DeploymentRecoveryFailed"]);
            }

            _pathResolver.PrepareGameDirectories(newPaths, true);
            _runtimeContext.RuntimePaths.SwitchActive(newPaths);
            switched = true;

            bool recoverySucceeded = await Task.Run(
                () => _launchPreparationService.Recover(newPaths, cancellationToken),
                cancellationToken);
            if (!recoverySucceeded)
            {
                throw new InvalidOperationException(_stringLocalizer["DeploymentRecoveryFailed"]);
            }

            _runtimeContext.ApplyActiveGameDefaults();
            bool connected = await CheckConnectionAsync(cancellationToken);
            await _catalog.InitDataAsync(
                CreateCatalogRequest(newPaths, connected, _runtimeContext.ModsRepositoryUrl),
                cancellationToken);
            _runtimeContext.Connected = connected && _catalog.RepositoryModificationNames != null;
            _logger.LogInformation(
                "Switched the active launcher session from {OldGame} to {NewGame}.",
                oldPaths.Game,
                newPaths.Game);
            return true;
        }
        catch (Exception exception)
        {
            if (switched)
            {
                await RollBackSessionAsync(
                    oldPaths,
                    oldColors,
                    oldRepositoryUrl,
                    oldConnected);
            }

            RollBackPreferences(rollbackInstallations, rollbackSelectedGame);
            if (exception is OperationCanceledException)
            {
                _logger.LogInformation("Canceled the live game-session change and restored the previous session.");
                throw;
            }

            _logger.LogError(exception, "Could not apply the requested live game-session change.");
            await ShowSwitchFailureAsync(owner, exception.Message);
            return false;
        }
    }

    private async Task RollBackSessionAsync(
        LauncherPaths oldPaths,
        ColorsInfo oldColors,
        string? oldRepositoryUrl,
        bool oldConnected)
    {
        _runtimeContext.RuntimePaths.SwitchActive(oldPaths);
        // Restoring the theme republishes it at application scope, so every open window rolls back with it.
        _runtimeContext.Colors = oldColors;
        _runtimeContext.ModsRepositoryUrl = oldRepositoryUrl;
        _runtimeContext.Connected = oldConnected;
        try
        {
            await _catalog.InitDataAsync(
                CreateCatalogRequest(oldPaths, oldConnected, oldRepositoryUrl),
                CancellationToken.None);
        }
        catch (Exception rollbackException)
        {
            _logger.LogError(rollbackException, "Could not reload the previous game catalog during rollback.");
        }
    }

    private async Task<bool> TryPersistSelectedGameAsync(
        SupportedGame game,
        LauncherInstallations rollbackInstallations,
        SupportedGame? rollbackSelectedGame,
        Window owner)
    {
        if (_preferencesService.Current.LastSelectedGame == game)
        {
            return true;
        }

        try
        {
            _preferencesService.Update(_preferencesService.Current with { LastSelectedGame = game });
            return true;
        }
        catch (LauncherPreferencesPersistenceException)
        {
            RollBackPreferences(rollbackInstallations, rollbackSelectedGame);
            await ShowSwitchFailureAsync(owner, _stringLocalizer["SettingsSaveFailedDetails"]);
            return false;
        }
    }

    private void RollBackPreferences(
        LauncherInstallations installations,
        SupportedGame? selectedGame)
    {
        try
        {
            _preferencesService.Update(_preferencesService.Current with
            {
                Installations = installations,
                LastSelectedGame = selectedGame
            });
        }
        catch (LauncherPreferencesPersistenceException exception)
        {
            _logger.LogError(exception, "Could not restore the previous installation preferences.");
        }
    }

    private async Task<bool> CanChangeSessionAsync(Window owner)
    {
        await _packageActivityService.ReleasePausedDownloadAsync();

        if (!_launchCoordinator.IsLaunchInProgress &&
            !_launchCoordinator.HasActiveProcess &&
            !_launchCoordinator.IsPreparingLaunch &&
            !_packageActivityService.IsActive)
        {
            return true;
        }

        await _dialogService.ShowInfoAsync(
            new LauncherInfoDialogRequest(
                _stringLocalizer["GameSwitchBlockedTitle"],
                _stringLocalizer["GameSwitchBlockedDetails"]),
            owner);
        return false;
    }

    private async Task<bool> CheckConnectionAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_runtimeContext.ModsRepositoryUrl))
        {
            return false;
        }

        return await _connectionProbe.CanConnectAsync(
            new Uri(_runtimeContext.ModsRepositoryUrl, UriKind.Absolute),
            cancellationToken);
    }

    private static LauncherContentCatalogInitializationRequest CreateCatalogRequest(
        LauncherPaths paths,
        bool connected,
        string? repositoryUrl)
    {
        return new LauncherContentCatalogInitializationRequest(
            connected && !string.IsNullOrWhiteSpace(repositoryUrl)
                ? new Uri(repositoryUrl, UriKind.Absolute)
                : null,
            paths);
    }

    private Task ShowSwitchFailureAsync(Window owner, string details)
    {
        return _dialogService.ShowErrorAsync(
            new LauncherInfoDialogRequest(
                _stringLocalizer["GameSwitchFailedTitle"],
                string.Format(CultureInfo.CurrentCulture, _stringLocalizer["GameSwitchFailedDetails"], details)),
            owner);
    }

}
