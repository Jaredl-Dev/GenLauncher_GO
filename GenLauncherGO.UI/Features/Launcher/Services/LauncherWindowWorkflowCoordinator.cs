using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Exceptions;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Shell.Contracts;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Support;
using GenLauncherGO.UI.Features.Launcher.ViewModels;
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Features.Settings.Views;
using GenLauncherGO.UI.Features.Startup.Services;
using GenLauncherGO.UI.Shared.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.UI.Features.Launcher.Services;

/// <summary>
///     Coordinates main-window launcher workflows that need window ownership but should not live in code-behind.
/// </summary>
internal sealed class LauncherWindowWorkflowCoordinator
{
    private const string GenPatcherDownloadPageUrl = "https://legi.cc/genpatcher/";

    private readonly LauncherCloseGuard _closeGuard;

    private readonly ILauncherDialogService _dialogService;

    private readonly LauncherGameSessionCoordinator _gameSessionCoordinator;

    private readonly LauncherLaunchCoordinator _launchCoordinator;

    private readonly LauncherLaunchReadinessCoordinator _launchReadinessCoordinator;

    private readonly Func<LauncherSettingsWindow> _launcherSettingsWindowFactory;

    private readonly ILauncherShellService _launcherShellService;

    private readonly ILauncherPreferencesService _preferencesService;

    private readonly ILogger<LauncherWindowWorkflowCoordinator> _logger;

    private readonly LauncherPackageActivityService _packageActivityService;

    private readonly LauncherRestartCoordinator _restartCoordinator;

    private readonly ILauncherStringLocalizer _stringLocalizer;

    private bool _retailRecommendationInProgress;

    private bool _retailRecommendationShownThisSession;

    public LauncherWindowWorkflowCoordinator(
        LauncherLaunchCoordinator launchCoordinator,
        LauncherLaunchReadinessCoordinator launchReadinessCoordinator,
        LauncherGameSessionCoordinator gameSessionCoordinator,
        LauncherPackageActivityService packageActivityService,
        ILauncherShellService launcherShellService,
        ILauncherPreferencesService preferencesService,
        ILauncherStringLocalizer stringLocalizer,
        Func<LauncherSettingsWindow> launcherSettingsWindowFactory,
        ILauncherDialogService dialogService,
        LauncherCloseGuard closeGuard,
        LauncherRestartCoordinator restartCoordinator,
        ILogger<LauncherWindowWorkflowCoordinator>? logger = null)
    {
        _launchCoordinator = launchCoordinator ?? throw new ArgumentNullException(nameof(launchCoordinator));
        _launchReadinessCoordinator = launchReadinessCoordinator ??
                                      throw new ArgumentNullException(nameof(launchReadinessCoordinator));
        _gameSessionCoordinator = gameSessionCoordinator ??
                                  throw new ArgumentNullException(nameof(gameSessionCoordinator));
        _packageActivityService =
            packageActivityService ?? throw new ArgumentNullException(nameof(packageActivityService));
        _launcherShellService = launcherShellService ?? throw new ArgumentNullException(nameof(launcherShellService));
        _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _launcherSettingsWindowFactory = launcherSettingsWindowFactory ??
                                         throw new ArgumentNullException(nameof(launcherSettingsWindowFactory));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _closeGuard = closeGuard ?? throw new ArgumentNullException(nameof(closeGuard));
        _restartCoordinator = restartCoordinator ?? throw new ArgumentNullException(nameof(restartCoordinator));
        _logger = logger ?? NullLogger<LauncherWindowWorkflowCoordinator>.Instance;
    }

    /// <summary>
    ///     Shows the globally one-time GenPatcher recommendation when the retail client becomes selected.
    /// </summary>
    public async Task ShowRetailClientRecommendationIfNeededAsync(
        ExecutableOption? selectedClient,
        Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (selectedClient?.IsRetail != true ||
            _retailRecommendationInProgress ||
            _retailRecommendationShownThisSession ||
            _preferencesService.Current.Shared.HasShownRetailGenPatcherRecommendation)
        {
            return;
        }

        _retailRecommendationInProgress = true;
        try
        {
            bool visitDownloadPage = await _dialogService.ShowInfoActionAsync(
                new LauncherInfoDialogRequest(
                    _stringLocalizer["GenPatcherRecommendationTitle"],
                    _stringLocalizer["GenPatcherRecommendationMessage"]),
                _stringLocalizer["VisitGenPatcherDownloadPage"],
                owner);
            _retailRecommendationShownThisSession = true;

            try
            {
                LauncherPreferences preferences = _preferencesService.Current;
                _preferencesService.Update(preferences with
                {
                    Shared = preferences.Shared with
                    {
                        HasShownRetailGenPatcherRecommendation = true
                    }
                });
            }
            catch (LauncherPreferencesPersistenceException exception)
            {
                _logger.LogWarning(
                    exception,
                    "The GenPatcher recommendation state could not be persisted; it remains suppressed for this session.");
            }

            if (visitDownloadPage)
            {
                _launcherShellService.OpenUri(GenPatcherDownloadPageUrl);
            }
        }
        finally
        {
            _retailRecommendationInProgress = false;
        }
    }

    /// <summary>
    ///     Runs the selected game-client or World Builder launch workflow.
    /// </summary>
    public async Task LaunchAsync(
        GameLaunchTargetKind targetKind,
        LauncherWindowContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string targetName = targetKind switch
        {
            GameLaunchTargetKind.GameClient => "game client",
            GameLaunchTargetKind.WorldBuilder => "World Builder",
            _ => throw new ArgumentOutOfRangeException(nameof(targetKind), targetKind, "Unknown launch target.")
        };

        IReadOnlyList<ModificationViewModel> selectedContent = context.ViewModel.SelectedContent;
        ModificationViewModel? selectedModification = context.ViewModel.SelectedModifications.FirstOrDefault();

        try
        {
            if (!await _launchReadinessCoordinator.EnsureSelectedContentCanLaunchAsync(
                    selectedContent,
                    context.Owner))
            {
                _logger.LogWarning(
                    "{LaunchTarget} launch was blocked because selected content is not launchable.",
                    targetName);
                return;
            }

            string? executableName;
            bool useGeneralsOnline;
            if (targetKind == GameLaunchTargetKind.GameClient)
            {
                ExecutableOption? selectedGameClient = context.ViewModel.SelectedGameClientOption;
                executableName = selectedGameClient?.ExecutableName;
                useGeneralsOnline = selectedGameClient?.IsGeneralsOnline == true;
            }
            else
            {
                executableName = context.ViewModel.SelectedWorldBuilderOption?.ExecutableName;
                useGeneralsOnline = false;
            }

            if (targetKind == GameLaunchTargetKind.GameClient &&
                !await ConfirmLaunchWarningsAsync(
                    selectedContent,
                    context.Owner,
                    targetName))
            {
                return;
            }

            if (!await _launchReadinessCoordinator.EnsureExecutableAvailableAsync(
                    executableName,
                    _stringLocalizer["ExecutableUnavailable"],
                    context.Owner))
            {
                _logger.LogWarning(
                    "{LaunchTarget} launch was blocked because no supported executable is available.",
                    targetName);
                return;
            }

            if (targetKind == GameLaunchTargetKind.WorldBuilder &&
                !await ConfirmLaunchWarningsAsync(
                    selectedContent,
                    context.Owner,
                    targetName))
            {
                return;
            }

            _logger.LogInformation(
                "Starting {LaunchTarget} launch workflow for {ExecutableName}.",
                targetName,
                executableName);
            context.ViewModel.ApplySelectionToPersistenceModel();
            LauncherLaunchResult result = await _launchCoordinator.LaunchAsync(
                new LauncherLaunchRequest(
                    targetKind,
                    executableName!,
                    useGeneralsOnline,
                    context.ViewModel.GetSelectedVersionsOfAllSelectedModifications()),
                selectedContent.Cast<ILaunchContentIntegrityProgressTarget>().ToList(),
                context.Owner,
                cancellationToken);
            _logger.LogInformation(
                "{LaunchTarget} launch workflow completed. Started: {LaunchStarted}; process succeeded: {ProcessSucceeded}; failure: {FailureKind}.",
                targetName,
                result.LaunchStarted,
                result.ProcessSucceeded,
                result.FailureKind);

            if (targetKind == GameLaunchTargetKind.GameClient &&
                result.ProcessSucceeded &&
                selectedModification != null)
            {
                selectedModification.SetSupportButtonBlinking(true);
            }
        }
        finally
        {
            context.Content.RestoreFocuses();
        }
    }

    private async Task<bool> ConfirmLaunchWarningsAsync(
        IReadOnlyList<ModificationViewModel> selectedContent,
        Window owner,
        string targetName)
    {
        bool confirmed = await _launchReadinessCoordinator.ConfirmSelectedContentWarningsAsync(
            selectedContent,
            owner);
        if (!confirmed)
        {
            _logger.LogInformation(
                "{LaunchTarget} launch was canceled after selected content warning confirmation.",
                targetName);
        }

        return confirmed;
    }

    /// <summary>
    ///     Opens the launcher settings workflow.
    /// </summary>
    public async Task OpenOptionsAsync(
        LauncherWindowContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_launchCoordinator.HasActiveProcess)
        {
            _logger.LogWarning("Launcher settings window was blocked because a launched process is still running.");
            await _dialogService.ShowErrorAsync(
                new LauncherInfoDialogRequest(
                    _stringLocalizer["GameIsStillRunning"],
                    _stringLocalizer["FinishProcess"]),
                context.Owner);
            return;
        }

        _logger.LogInformation("Opening launcher settings window.");
        LauncherSettingsWindow settingsWindow = _launcherSettingsWindowFactory();
        settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        settingsWindow.ConfigureGameManagement(
            (previousInstallations, previousSelectedGame) =>
                ApplyInstallationChangesFromSettingsAsync(
                    previousInstallations,
                    previousSelectedGame,
                    context.ViewModel,
                    context.Content,
                    settingsWindow,
                    cancellationToken),
            game => SwitchGameFromSettingsAsync(
                game,
                context.ViewModel,
                context.Content,
                settingsWindow,
                cancellationToken));
        await settingsWindow.ShowDialog<bool>(context.Owner);

        if (settingsWindow.RestartRequested &&
            await _restartCoordinator.TryRequestRestartAsync(context.Owner))
        {
            _logger.LogInformation("Closing the main window for a requested launcher restart.");
            context.Owner.Close();
            return;
        }

        context.Content.RefreshTabs();
        context.ViewModel.UpdateCurrentLauncherVersionText();
        context.ViewModel.RefreshModificationContainerData();
        _logger.LogInformation("Launcher settings window closed; refreshed launcher state.");
    }

    private async Task<bool> ApplyInstallationChangesFromSettingsAsync(
        LauncherInstallations previousInstallations,
        SupportedGame? previousSelectedGame,
        MainWindowViewModel viewModel,
        LauncherWindowListController content,
        Window settingsOwner,
        CancellationToken cancellationToken)
    {
        return await RunLiveGameManagementChangeAsync(
            viewModel,
            content,
            () => _gameSessionCoordinator.ApplyInstallationChangesAsync(
                previousInstallations,
                previousSelectedGame,
                settingsOwner,
                cancellationToken));
    }

    private async Task<bool> SwitchGameFromSettingsAsync(
        SupportedGame game,
        MainWindowViewModel viewModel,
        LauncherWindowListController content,
        Window settingsOwner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(settingsOwner);
        if (game == SupportedGame.Unknown)
        {
            return false;
        }

        return await RunLiveGameManagementChangeAsync(
            viewModel,
            content,
            () => _gameSessionCoordinator.SwitchGameAsync(
                game,
                settingsOwner,
                cancellationToken));
    }

    private static async Task<bool> RunLiveGameManagementChangeAsync(
        MainWindowViewModel viewModel,
        LauncherWindowListController content,
        Func<Task<bool>> change)
    {
        viewModel.SetMainControlsEnabled(false);
        try
        {
            // Tile selection is intentionally UI-owned until a persistence boundary. Project it before
            // a live reload so the current game's selection is saved or restored from the same authority.
            viewModel.ApplySelectionToPersistenceModel();
            if (!await change())
            {
                return false;
            }

            RefreshAfterLiveGameManagementChange(viewModel, content);
            return true;
        }
        finally
        {
            viewModel.SetMainControlsEnabled(true);
        }
    }

    private static void RefreshAfterLiveGameManagementChange(
        MainWindowViewModel viewModel,
        LauncherWindowListController content)
    {
        viewModel.ReloadForActiveGame();
        content.Initialize();
    }

    /// <summary>
    ///     Applies the shared active-operation safety policy before normal application exit.
    /// </summary>
    public Task<bool> ConfirmCloseDuringActiveOperationsAsync(Window owner)
    {
        return _closeGuard.CanCloseAsync(owner, LauncherCloseReason.Exit);
    }

    /// <summary>
    ///     Suspends any in-flight download and waits for lifecycle-owned cleanup before the host may be disposed.
    /// </summary>
    /// <remarks>
    ///     Closing never discards a download. The transfer stops and its partial content stays on disk so the next
    ///     session can resume it; only an explicit cancel throws that content away.
    /// </remarks>
    public async Task PrepareForCloseAsync()
    {
        Task packageIdle = _packageActivityService.WaitForIdleAsync();
        Task<PackageDownloadResult>? activeDownload = _packageActivityService.ActiveDownloadTask;
        if (activeDownload != null)
        {
            _packageActivityService.RequestActiveDownloadSuspension();
            PackageDownloadResult result = await activeDownload;
            _logger.LogInformation(
                "Close-time package suspension reached terminal status {DownloadStatus}.",
                result.Status);
        }

        await packageIdle;
    }

    /// <summary>
    ///     Confirms and force closes the active launched process.
    /// </summary>
    public async Task ForceCloseRunningProcessAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (!_launchCoordinator.HasActiveProcess)
        {
            _logger.LogDebug("Force-close request ignored because no launched process is active.");
            return;
        }

        string processName = _launchCoordinator.ActiveProcessName ?? _stringLocalizer["RunningProcessUnknown"];
        bool confirmed = await _dialogService.ShowWarningConfirmationAsync(
            new LauncherInfoDialogRequest(
                _stringLocalizer["ForceQuitRunningProcessConfirmationTitle"],
                string.Format(CultureInfo.CurrentCulture, _stringLocalizer["ForceQuitRunningProcessConfirmationDetails"], processName)),
            _stringLocalizer["ForceQuitRunningProcess"],
            owner);
        if (confirmed)
        {
            _logger.LogWarning(
                "Force-close confirmed for launched process {ProcessName}.",
                processName);
            _launchCoordinator.ForceCloseActiveProcess();
            return;
        }

        _logger.LogInformation(
            "Force-close canceled for launched process {ProcessName}.",
            processName);
    }

}
