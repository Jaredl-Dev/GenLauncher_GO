using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Shell.Contracts;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Contracts;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Support;
using GenLauncherGO.UI.Features.Launcher.ViewModels;
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Features.Settings.Views;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Features.Startup.Services;
using GenLauncherGO.UI.Shared.Localization;
using GenLauncherGO.UI.Shared.Themes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.UI.Features.Launcher.Services;

/// <summary>
/// Coordinates main-window launcher workflows that need window ownership but should not live in code-behind.
/// </summary>
internal sealed class LauncherWindowWorkflowCoordinator
{
    private readonly LauncherLaunchCoordinator _launchCoordinator;

    private readonly LauncherLaunchReadinessCoordinator _launchReadinessCoordinator;

    private readonly LauncherTileActionService _tileActionService;

    private readonly LauncherRuntimeContext _runtimeContext;

    private readonly LauncherGameSessionCoordinator _gameSessionCoordinator;

    private readonly LauncherManualImportCoordinator _manualImportCoordinator;

    private readonly LauncherModificationDownloadCoordinator _downloadCoordinator;

    private readonly LauncherPackageActivityService _packageActivityService;

    private readonly ILauncherShellService _launcherShellService;

    private readonly ILauncherFilePicker _filePicker;

    private readonly LaunchContentIntegrityCoordinator _launchContentIntegrityCoordinator;

    private readonly ILauncherStringLocalizer _stringLocalizer;

    private readonly IModificationImageFileService _modificationImageFileService;

    private readonly Func<LauncherSettingsWindow> _launcherSettingsWindowFactory;

    private readonly ILauncherDialogService _dialogService;

    private readonly LauncherCloseGuard _closeGuard;

    private readonly LauncherRestartCoordinator _restartCoordinator;

    private readonly ILogger<LauncherWindowWorkflowCoordinator> _logger;

    public LauncherWindowWorkflowCoordinator(
        LauncherLaunchCoordinator launchCoordinator,
        LauncherLaunchReadinessCoordinator launchReadinessCoordinator,
        LauncherTileActionService tileActionService,
        LauncherRuntimeContext runtimeContext,
        LauncherGameSessionCoordinator gameSessionCoordinator,
        LauncherManualImportCoordinator manualImportCoordinator,
        LauncherModificationDownloadCoordinator downloadCoordinator,
        LauncherPackageActivityService packageActivityService,
        ILauncherShellService launcherShellService,
        ILauncherFilePicker filePicker,
        LaunchContentIntegrityCoordinator launchContentIntegrityCoordinator,
        ILauncherStringLocalizer stringLocalizer,
        IModificationImageFileService modificationImageFileService,
        Func<LauncherSettingsWindow> launcherSettingsWindowFactory,
        ILauncherDialogService dialogService,
        LauncherCloseGuard closeGuard,
        LauncherRestartCoordinator restartCoordinator,
        ILogger<LauncherWindowWorkflowCoordinator>? logger = null)
    {
        _launchCoordinator = launchCoordinator ?? throw new ArgumentNullException(nameof(launchCoordinator));
        _launchReadinessCoordinator = launchReadinessCoordinator ??
                                      throw new ArgumentNullException(nameof(launchReadinessCoordinator));
        _tileActionService = tileActionService ?? throw new ArgumentNullException(nameof(tileActionService));
        _runtimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
        _gameSessionCoordinator = gameSessionCoordinator ??
                                  throw new ArgumentNullException(nameof(gameSessionCoordinator));
        _manualImportCoordinator = manualImportCoordinator ??
                                   throw new ArgumentNullException(nameof(manualImportCoordinator));
        _downloadCoordinator = downloadCoordinator ?? throw new ArgumentNullException(nameof(downloadCoordinator));
        _packageActivityService = packageActivityService ?? throw new ArgumentNullException(nameof(packageActivityService));
        _launcherShellService = launcherShellService ?? throw new ArgumentNullException(nameof(launcherShellService));
        _filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));
        _launchContentIntegrityCoordinator = launchContentIntegrityCoordinator ??
                                             throw new ArgumentNullException(
                                                 nameof(launchContentIntegrityCoordinator));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _modificationImageFileService = modificationImageFileService ??
                                        throw new ArgumentNullException(nameof(modificationImageFileService));
        _launcherSettingsWindowFactory = launcherSettingsWindowFactory ??
                                         throw new ArgumentNullException(nameof(launcherSettingsWindowFactory));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _closeGuard = closeGuard ?? throw new ArgumentNullException(nameof(closeGuard));
        _restartCoordinator = restartCoordinator ?? throw new ArgumentNullException(nameof(restartCoordinator));
        _logger = logger ?? NullLogger<LauncherWindowWorkflowCoordinator>.Instance;
    }

    /// <summary>
    /// Runs the selected game-client or World Builder launch workflow.
    /// </summary>
    public async Task LaunchAsync(
        GameLaunchTargetKind targetKind,
        MainWindowViewModel viewModel,
        LauncherWindowListController content,
        Window owner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(owner);

        string targetName = targetKind switch
        {
            GameLaunchTargetKind.GameClient => "game client",
            GameLaunchTargetKind.WorldBuilder => "World Builder",
            _ => throw new ArgumentOutOfRangeException(nameof(targetKind), targetKind, "Unknown launch target.")
        };

        IReadOnlyList<ModificationViewModel> selectedModifications = viewModel.SelectedModifications;
        IReadOnlyList<ModificationViewModel> selectedPatches = viewModel.SelectedPatches;
        IReadOnlyList<ModificationViewModel> selectedAddons = viewModel.SelectedAddons;

        try
        {
            if (!await _launchReadinessCoordinator.EnsureSelectedContentCanLaunchAsync(
                    selectedModifications,
                    selectedPatches,
                    selectedAddons,
                    owner))
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
                ExecutableOption? selectedGameClient = viewModel.SelectedGameClientOption;
                executableName = selectedGameClient?.ExecutableName;
                useGeneralsOnline = selectedGameClient?.IsGeneralsOnline == true;
            }
            else
            {
                executableName = viewModel.SelectedWorldBuilderOption?.ExecutableName;
                useGeneralsOnline = false;
            }

            if (targetKind == GameLaunchTargetKind.GameClient &&
                !await _launchReadinessCoordinator.ConfirmSelectedContentWarningsAsync(
                    selectedModifications,
                    selectedPatches,
                    selectedAddons,
                    owner))
            {
                _logger.LogInformation(
                    "{LaunchTarget} launch was canceled after selected content warning confirmation.",
                    targetName);
                return;
            }

            if (!await _launchReadinessCoordinator.EnsureExecutableAvailableAsync(
                    executableName,
                    _stringLocalizer["ExecutableUnavailable"],
                    owner))
            {
                _logger.LogWarning(
                    "{LaunchTarget} launch was blocked because no supported executable is available.",
                    targetName);
                return;
            }

            if (targetKind == GameLaunchTargetKind.WorldBuilder &&
                !await _launchReadinessCoordinator.ConfirmSelectedContentWarningsAsync(
                    selectedModifications,
                    selectedPatches,
                    selectedAddons,
                    owner))
            {
                _logger.LogInformation(
                    "{LaunchTarget} launch was canceled after selected content warning confirmation.",
                    targetName);
                return;
            }

            _logger.LogInformation(
                "Starting {LaunchTarget} launch workflow for {ExecutableName}.",
                targetName,
                executableName);
            viewModel.ApplySelectionToPersistenceModel();
            LauncherLaunchResult result = await _launchCoordinator.LaunchAsync(
                new LauncherLaunchRequest(
                    targetKind,
                    executableName!,
                    useGeneralsOnline,
                    viewModel.GetSelectedVersionsOfAllSelectedModifications()),
                GetSelectedIntegrityProgressTargets(
                    selectedModifications,
                    selectedPatches,
                    selectedAddons),
                owner,
                cancellationToken);
            _logger.LogInformation(
                "{LaunchTarget} launch workflow completed. Started: {LaunchStarted}; process succeeded: {ProcessSucceeded}; failure: {FailureKind}.",
                targetName,
                result.LaunchStarted,
                result.ProcessSucceeded,
                result.FailureKind);

            if (targetKind == GameLaunchTargetKind.GameClient &&
                result.ProcessSucceeded &&
                selectedModifications.Count > 0)
            {
                selectedModifications[0].SetSupportButtonBlinking(true);
            }
        }
        finally
        {
            content.RestoreFocuses();
        }
    }

    /// <summary>
    /// Opens the launcher settings workflow.
    /// </summary>
    public async Task OpenOptionsAsync(
        MainWindowViewModel viewModel,
        LauncherWindowListController content,
        Window owner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(owner);

        if (!_launchCoordinator.HasActiveProcess)
        {
            _logger.LogInformation("Opening launcher settings window.");
            LauncherSettingsWindow settingsWindow = _launcherSettingsWindowFactory();
            settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            LauncherThemeResourceApplier.Apply(settingsWindow, _runtimeContext.Colors);
            settingsWindow.ConfigureGameManagement(
                (previousInstallations, previousSelectedGame) =>
                    ApplyInstallationChangesFromSettingsAsync(
                        previousInstallations,
                        previousSelectedGame,
                        viewModel,
                        content,
                        settingsWindow,
                        owner,
                        cancellationToken),
                game => SwitchGameFromSettingsAsync(
                    game,
                    viewModel,
                    content,
                    settingsWindow,
                    owner,
                    cancellationToken));
            await settingsWindow.ShowDialog<bool>(owner);

            if (settingsWindow.RestartRequested &&
                await _restartCoordinator.TryRequestRestartAsync(owner))
            {
                _logger.LogInformation("Closing the main window for a requested launcher restart.");
                owner.Close();
                return;
            }

            content.RefreshTabs();
            viewModel.UpdateCurrentLauncherVersionText();
            viewModel.RefreshModificationContainerData();
            _logger.LogInformation("Launcher settings window closed; refreshed launcher state.");
            return;
        }

        _logger.LogWarning("Launcher settings window was blocked because a launched process is still running.");
        await _dialogService.ShowErrorAsync(
            new LauncherInfoDialogRequest(
                _stringLocalizer["GameIsStillRunning"],
                _stringLocalizer["FinishProcess"]),
            owner);
    }

    private async Task<bool> ApplyInstallationChangesFromSettingsAsync(
        LauncherInstallations previousInstallations,
        SupportedGame? previousSelectedGame,
        MainWindowViewModel viewModel,
        LauncherWindowListController content,
        Window settingsOwner,
        Window mainOwner,
        CancellationToken cancellationToken)
    {
        viewModel.SetMainControlsEnabled(false);
        try
        {
            // Tile selection is intentionally UI-owned until a persistence boundary. Project it before
            // a live reload so the current game's selection is saved or restored from the same authority.
            viewModel.ApplySelectionToPersistenceModel();
            bool applied = await _gameSessionCoordinator.ApplyInstallationChangesAsync(
                previousInstallations,
                previousSelectedGame,
                settingsOwner,
                cancellationToken);
            if (applied)
            {
                RefreshAfterLiveGameManagementChange(viewModel, content, mainOwner);
            }

            return applied;
        }
        finally
        {
            viewModel.SetMainControlsEnabled(true);
        }
    }

    private async Task<bool> SwitchGameFromSettingsAsync(
        SupportedGame game,
        MainWindowViewModel viewModel,
        LauncherWindowListController content,
        Window settingsOwner,
        Window mainOwner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(settingsOwner);
        ArgumentNullException.ThrowIfNull(mainOwner);
        if (game == SupportedGame.Unknown)
        {
            return false;
        }

        viewModel.SetMainControlsEnabled(false);
        try
        {
            viewModel.ApplySelectionToPersistenceModel();
            bool switched = await _gameSessionCoordinator.SwitchGameAsync(
                game,
                settingsOwner,
                cancellationToken);
            if (!switched)
            {
                return false;
            }

            RefreshAfterLiveGameManagementChange(viewModel, content, mainOwner);
            return true;
        }
        finally
        {
            viewModel.SetMainControlsEnabled(true);
        }
    }

    private void RefreshAfterLiveGameManagementChange(
        MainWindowViewModel viewModel,
        LauncherWindowListController content,
        Window mainOwner)
    {
        LauncherThemeResourceApplier.Apply(mainOwner, _runtimeContext.Colors);
        viewModel.ReloadForActiveGame();
        content.Initialize();
    }

    /// <summary>
    /// Applies the shared active-operation safety policy before normal application exit.
    /// </summary>
    public Task<bool> ConfirmCloseDuringActiveOperationsAsync(Window owner)
    {
        return _closeGuard.CanCloseAsync(owner, LauncherCloseReason.Exit);
    }

    /// <summary>
    /// Requests cancellation and waits for lifecycle-owned terminal cleanup before the application host may be
    /// disposed.
    /// </summary>
    public async Task PrepareForCloseAsync()
    {
        Task packageIdle = _packageActivityService.WaitForIdleAsync();
        Task<PackageDownloadResult>? activeDownload = _packageActivityService.ActiveDownloadTask;
        if (activeDownload != null)
        {
            _packageActivityService.RequestActiveDownloadCancellation();
            PackageDownloadResult result = await activeDownload;
            _logger.LogInformation(
                "Close-time package cancellation reached terminal status {DownloadStatus}.",
                result.Status);
        }

        await packageIdle;
    }

    /// <summary>
    /// Confirms and force closes the active launched process.
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
                String.Format(_stringLocalizer["ForceQuitRunningProcessConfirmationDetails"], processName)),
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

    /// <summary>
    /// Opens repository modification selection and adds the selected modification.
    /// </summary>
    public async Task AddRepositoryModificationAsync(
        MainWindowViewModel viewModel,
        LauncherWindowListController content,
        Window owner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(owner);

        if (!await EnsurePackageActivityCanStartAsync(owner))
        {
            _logger.LogWarning("Repository modification add workflow was blocked by active package activity.");
            content.RestoreFocuses();
            return;
        }

        IReadOnlyList<string> notAddedModificationsNames = viewModel.GetNotAddedRepositoryModificationNames();

        string? modificationName = await _dialogService.ShowModificationSelectionAsync(
            notAddedModificationsNames,
            owner);
        if (!String.IsNullOrWhiteSpace(modificationName))
        {
            _logger.LogInformation(
                "Adding repository modification {ModificationName}.",
                modificationName);
            viewModel.SetMainControlsEnabled(false);
            try
            {
                await viewModel.AddModToListAsync(modificationName, cancellationToken);
            }
            finally
            {
                viewModel.SetMainControlsEnabled(true);
            }
        }
        else
        {
            _logger.LogDebug("Repository modification add workflow was canceled.");
        }

        content.RestoreFocuses();
    }

    public Task UpdateModificationAsync(
        MainWindowViewModel viewModel,
        LauncherWindowListController content,
        Window owner,
        ModificationViewModel modification)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(modification);

        return UpdateModificationCoreAsync(viewModel, content, owner, modification);
    }

    public void OpenChangeLog(ModificationViewModel modification)
    {
        ArgumentNullException.ThrowIfNull(modification);
        ApplyTileLinkAction(
            modification,
            LauncherTileActionService.GetNewsAction(modification.ContainerModification));
    }

    public void OpenNetworkInfo(ModificationViewModel modification)
    {
        ArgumentNullException.ThrowIfNull(modification);
        ApplyTileLinkAction(
            modification,
            LauncherTileActionService.GetNetworkInfoAction(modification.ContainerModification));
    }

    public void OpenSupport(ModificationViewModel modification)
    {
        ArgumentNullException.ThrowIfNull(modification);
        ApplyTileLinkAction(
            modification,
            LauncherTileActionService.GetSupportAction(modification.ContainerModification));
    }

    public Task ChangeVersionImageAsync(
        MainWindowViewModel viewModel,
        Window owner,
        ModificationViewModel modification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(modification);

        return ChangeVersionImageCoreAsync(viewModel, owner, modification, cancellationToken);
    }

    public void OpenModDb(ModificationViewModel modification)
    {
        ArgumentNullException.ThrowIfNull(modification);
        ApplyTileLinkAction(
            modification,
            LauncherTileActionService.GetModDbAction(modification.ContainerModification));
    }

    public void OpenDiscord(ModificationViewModel modification)
    {
        ArgumentNullException.ThrowIfNull(modification);
        ApplyTileLinkAction(
            modification,
            LauncherTileActionService.GetDiscordAction(modification.ContainerModification));
    }

    /// <summary>
    /// Deletes an installed version from the catalog and refreshes tile state.
    /// </summary>
    public async Task DeleteVersionAsync(
        MainWindowViewModel viewModel,
        LauncherWindowListController content,
        Window owner,
        ModificationVersionSelection versionData)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(versionData);

        if (!await ConfirmContentRemovalAsync(versionData.SelectedVersion.Name, owner))
        {
            _logger.LogInformation(
                "Content removal for {ContentName} {ContentVersion} was declined.",
                versionData.SelectedVersion.Name,
                versionData.SelectedVersion.Version);
            return;
        }

        _logger.LogInformation(
            "Deleting launcher content version {ContentName} {ContentVersion}.",
            versionData.SelectedVersion.Name,
            versionData.SelectedVersion.Version);
        bool removedContentCard = _tileActionService.DeleteVersion(versionData);
        if (removedContentCard)
        {
            viewModel.RemoveContentFromList(versionData.ModificationViewModel);
            content.RefreshTabs();
        }
        else
        {
            versionData.ModificationViewModel.RefreshFromModelAndPresentation();
        }

        viewModel.UpdateAddonAndPatchTabLabels();
        viewModel.SaveLauncherData();
    }

    /// <summary>
    /// Removes an uninstalled modification card, or cancels its active download and retains the card.
    /// </summary>
    public async Task DeleteModificationAsync(
        MainWindowViewModel viewModel,
        LauncherWindowListController content,
        Window owner,
        ModificationViewModel modification)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(modification);

        if (_packageActivityService.GetActiveDownloadTask(modification) is { IsCompleted: false })
        {
            await CancelActiveDownloadAsync(viewModel, content, owner, modification);
            return;
        }

        if (modification.ContainerModification.ModificationType != ModificationType.Mod ||
            modification.ContainerModification.Installed)
        {
            return;
        }

        if (!await ConfirmListRemovalAsync(modification.ContainerModification.Name, owner))
        {
            _logger.LogInformation(
                "List removal for {ModificationName} was declined.",
                modification.ContainerModification.Name);
            return;
        }

        _logger.LogInformation(
            "Removing uninstalled modification {ModificationName}.",
            modification.ContainerModification.Name);
        RemoveUninstalledModification(viewModel, content, modification);
        content.RestoreFocuses();
    }

    /// <summary>
    /// Runs the requested manual content import workflow.
    /// </summary>
    public async Task ImportManualContentAsync(
        MainWindowViewModel viewModel,
        LauncherWindowListController content,
        Window owner,
        ModificationType kind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(owner);

        string activityDisplayName = kind switch
        {
            ModificationType.Mod => _stringLocalizer["AddModFromFiles"],
            ModificationType.Patch => viewModel.ManualAddPatchText,
            ModificationType.Addon => viewModel.ManualAddAddonText,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown manual import kind.")
        };
        if (!_packageActivityService.TryBegin(
                activityDisplayName,
                out LauncherPackageActivityService.LauncherPackageActivityLease? activityLeaseResult))
        {
            await EnsurePackageActivityCanStartAsync(owner);
            _logger.LogWarning(
                "Manual content import workflow {ImportKind} was blocked by active package activity.",
                kind);
            return;
        }

        LauncherPackageActivityService.LauncherPackageActivityLease activityLease =
            activityLeaseResult ?? throw new InvalidOperationException("Package activity lease was not created.");
        try
        {
            viewModel.SetMainControlsEnabled(false);
            _logger.LogInformation(
                "Starting manual content import workflow {ImportKind}.",
                kind);
            viewModel.ApplySelectionToPersistenceModel();
            Task<LauncherManualImportResult?> importTask = _manualImportCoordinator.ImportAsync(
                new LauncherManualImportRequest(
                    kind,
                    owner,
                    kind == ModificationType.Mod
                        ? null
                        : viewModel.GetSelectedModificationName()),
                cancellationToken);
            LauncherManualImportResult? result = await importTask;

            if (result != null)
            {
                viewModel.AddImportedContentToList(result);
                _logger.LogInformation(
                    "Manual content import workflow {ImportKind} completed.",
                    kind);
            }
            else
            {
                _logger.LogInformation(
                    "Manual content import workflow {ImportKind} was canceled or did not produce content.",
                    kind);
            }
        }
        finally
        {
            activityLease.Dispose();
            viewModel.SetMainControlsEnabled(true);
        }
    }

    private static IReadOnlyList<ILaunchContentIntegrityProgressTarget> GetSelectedIntegrityProgressTargets(
        IReadOnlyList<ModificationViewModel> selectedModifications,
        IReadOnlyList<ModificationViewModel> selectedPatches,
        IReadOnlyList<ModificationViewModel> selectedAddons)
    {
        return selectedModifications.Take(1)
            .Cast<ILaunchContentIntegrityProgressTarget>()
            .Concat(selectedPatches.Take(1))
            .Concat(selectedAddons)
            .ToList();
    }

    /// <summary>
    /// Starts a modification package download, or toggles pause for the active download.
    /// </summary>
    private async Task UpdateModificationCoreAsync(
        MainWindowViewModel viewModel,
        LauncherWindowListController content,
        Window owner,
        ModificationViewModel modData)
    {
        LauncherTileLinkAction advertisingAction =
            LauncherTileActionService.GetAdvertisingDownloadAction(modData.ContainerModification);
        if (!String.IsNullOrEmpty(advertisingAction.Uri))
        {
            _logger.LogInformation(
                "Opening advertising download link for {ModificationName}.",
                modData.ContainerModification.Name);
            ApplyTileLinkAction(modData, advertisingAction);
            return;
        }

        if (_packageActivityService.GetActiveDownloadTask(modData) is { IsCompleted: false })
        {
            if (_packageActivityService.TryToggleDownloadPause(modData, out bool isPaused))
            {
                modData.SetPackageDownloadPaused(isPaused);
                _logger.LogInformation(
                    "{DownloadAction} active download for {ModificationName}.",
                    isPaused ? "Paused" : "Resumed",
                    modData.ContainerModification.Name);
            }

            return;
        }

        if (!await EnsurePackageActivityCanStartAsync(owner))
        {
            _logger.LogWarning(
                "Download workflow for {ModificationName} was blocked by active package activity.",
                modData.ContainerModification.Name);
            return;
        }

        if (modData.ContainerModification.LatestVersion.Deprecated &&
            !await ConfirmDeprecatedModificationAsync(String.Format(
                _stringLocalizer["Deprecated"],
                modData.ContainerModification.Name),
                owner))
        {
            _logger.LogInformation(
                "Download workflow for deprecated modification {ModificationName} was canceled.",
                modData.ContainerModification.Name);
            return;
        }

        _logger.LogInformation(
            "Starting download workflow for {ModificationName} {ContentVersion}.",
            modData.ContainerModification.Name,
            modData.LatestVersion.Version);
        viewModel.EnsureContentSelected(modData);
        modData.SetUpdateButtonEnabled(false);
        await _downloadCoordinator.StartDownloadAsync(
            modData,
            owner,
            () => CleanupCanceledDownload(viewModel, modData));
    }

    /// <summary>
    /// Confirms and cancels an active package download, then removes any partial content.
    /// </summary>
    private async Task CancelActiveDownloadAsync(
        MainWindowViewModel viewModel,
        LauncherWindowListController content,
        Window owner,
        ModificationViewModel modData)
    {
        Task<PackageDownloadResult>? downloadTask = _packageActivityService.GetActiveDownloadTask(modData);
        if (downloadTask is not { IsCompleted: false })
        {
            return;
        }

        if (!await ConfirmDownloadCancellationAsync(modData, owner))
        {
            _logger.LogInformation(
                "Download cancellation for {ModificationName} was declined.",
                modData.ContainerModification.Name);
            return;
        }

        if (!ReferenceEquals(_packageActivityService.GetActiveDownloadTask(modData), downloadTask))
        {
            _logger.LogInformation(
                "Download cancellation for {ModificationName} was skipped because the active task changed.",
                modData.ContainerModification.Name);
            return;
        }

        modData.SetUpdateButtonEnabled(false);
        if (!_packageActivityService.RequestDownloadCancellation(modData))
        {
            return;
        }

        PackageDownloadResult result = await downloadTask;
        if (result.Status != PackageDownloadStatus.Canceled)
        {
            _logger.LogInformation(
                "Download cancellation for {ModificationName} lost the completion race with status {DownloadStatus}; committed content was preserved.",
                modData.ContainerModification.Name,
                result.Status);
            return;
        }

        content.RestoreFocuses();
        _logger.LogInformation(
            "Canceled download and cleaned partial content for {ModificationName}.",
            modData.ContainerModification.Name);
    }

    private void CleanupCanceledDownload(
        MainWindowViewModel viewModel,
        ModificationViewModel modData)
    {
        _tileActionService.UninstallContentVersion(modData);
        modData.LatestVersion.Installation.Installed = false;
        viewModel.SaveLauncherData();
        viewModel.UpdateAddonAndPatchTabLabels();
    }

    private void RemoveUninstalledModification(
        MainWindowViewModel viewModel,
        LauncherWindowListController content,
        ModificationViewModel modData)
    {
        _tileActionService.DiscardContentVersion(modData);
        viewModel.RemoveContentFromList(modData);
        content.RefreshTabs();
        viewModel.SaveLauncherData();
        viewModel.UpdateAddonAndPatchTabLabels();
    }

    private Task<bool> ConfirmDownloadCancellationAsync(ModificationViewModel modData, Window owner)
    {
        return _dialogService.ShowWarningConfirmationAsync(
            new LauncherInfoDialogRequest(
                _stringLocalizer["CancelDownload"],
                String.Format(
                    _stringLocalizer["CancelDownloadDetails"],
                    modData.ContainerModification.Name)),
            _stringLocalizer["Yes"],
            owner);
    }

    private Task<bool> ConfirmListRemovalAsync(string contentName, Window owner)
    {
        return _dialogService.ShowWarningConfirmationAsync(
            new LauncherInfoDialogRequest(
                _stringLocalizer["RemoveFromListConfirmation"],
                String.Format(
                    _stringLocalizer["RemoveFromListDetails"],
                    contentName)),
            _stringLocalizer["RemoveFromList"],
            owner);
    }

    private Task<bool> ConfirmContentRemovalAsync(string contentName, Window owner)
    {
        return _dialogService.ShowWarningConfirmationAsync(
            new LauncherInfoDialogRequest(
                _stringLocalizer["RemoveContent"],
                String.Format(
                    _stringLocalizer["RemoveContentDetails"],
                    contentName)),
            _stringLocalizer["Remove"],
            owner);
    }

    private async Task<bool> EnsurePackageActivityCanStartAsync(Window owner)
    {
        if (!_packageActivityService.IsActive)
        {
            return true;
        }

        await _dialogService.ShowInfoAsync(
            new LauncherInfoDialogRequest(
                _stringLocalizer["PackageActivityInProgress"],
                _stringLocalizer["PackageActivityInProgressDetails"]),
            owner);
        _logger.LogWarning(
            "Package activity {ActivityName} is already active; blocked a new package workflow.",
            _packageActivityService.ActiveDisplayName);
        return false;
    }

    /// <summary>
    /// Confirms that the user wants to install deprecated content.
    /// </summary>
    private Task<bool> ConfirmDeprecatedModificationAsync(string modsMessage, Window owner)
    {
        string mainMessage = _stringLocalizer["Compatibility"];

        return _dialogService.ShowWarningConfirmationAsync(
            new LauncherInfoDialogRequest(mainMessage, modsMessage),
            owner: owner);
    }

    private void ApplyTileLinkAction(ModificationViewModel modData, LauncherTileLinkAction action)
    {
        if (action.ShowThankYouMessage)
        {
            modData.SetStatusMessage(_stringLocalizer["ThankYou"]);
        }

        if (!String.IsNullOrEmpty(action.Uri))
        {
            _launcherShellService.OpenUri(action.Uri);
        }
    }

    /// <summary>
    /// Replaces the tile image for trusted manual content.
    /// </summary>
    private async Task ChangeVersionImageCoreAsync(
        MainWindowViewModel viewModel,
        Window owner,
        ModificationViewModel container,
        CancellationToken cancellationToken)
    {
        if (!_launchContentIntegrityCoordinator.IsManual(container.LatestVersion))
        {
            _logger.LogWarning(
                "Modification image replacement was blocked for non-manual content {ModificationName}.",
                container.ContainerModification.Name);
            return;
        }

        string? selectedImageFile = await _filePicker.PickModificationImageFileAsync(
            owner,
            _stringLocalizer["Image"]);
        if (selectedImageFile == null)
        {
            _logger.LogDebug(
                "Modification image replacement was canceled for {ModificationName}.",
                container.ContainerModification.Name);
            return;
        }

        viewModel.SetMainControlsEnabled(false);
        try
        {
            _logger.LogInformation(
                "Replacing modification image for {ModificationName} {ContentVersion}.",
                container.ContainerModification.Name,
                container.LatestVersion.Version);
            await _modificationImageFileService.ReplaceImageAsync(
                new ModificationImageReplacementRequest(
                    container.ContainerModification.Name,
                    container.LatestVersion.Version,
                    selectedImageFile),
                cancellationToken);
            await _launchContentIntegrityCoordinator.CaptureManualImageSnapshotAsync(container.LatestVersion);
            container.RefreshPresentation();
            _logger.LogInformation(
                "Replaced modification image for {ModificationName} {ContentVersion}.",
                container.ContainerModification.Name,
                container.LatestVersion.Version);
        }
        finally
        {
            viewModel.SetMainControlsEnabled(true);
        }
    }
}
