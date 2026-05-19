using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Updating.Contracts;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Shared.Formatting;
using GenLauncherGO.UI.Shared.Localization;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.UI.Features.Launcher.Services;

/// <summary>
///     Sequences package download, installation, and post-install processing for launcher modification tiles.
/// </summary>
internal sealed class LauncherModificationDownloadCoordinator
{
    private readonly LauncherPackageActivityAdmissionService _activityAdmissionService;
    private readonly ILauncherContentCatalog _catalog;
    private readonly ILauncherDialogService _dialogService;
    private readonly LaunchContentIntegrityCoordinator _launchContentIntegrityCoordinator;
    private readonly ILauncherPreferencesService _launcherPreferencesService;
    private readonly ILogger<LauncherModificationDownloadCoordinator> _logger;
    private readonly LauncherPackageActivityService _packageActivityService;
    private readonly IPackageDownloadService _packageDownloadService;
    private readonly ILauncherStringLocalizer _stringLocalizer;
    private readonly LauncherTileActionService _tileActionService;

    /// <summary>
    ///     Initializes the package download sequencing workflow.
    /// </summary>
    public LauncherModificationDownloadCoordinator(
        ILauncherPreferencesService launcherPreferencesService,
        ILauncherContentCatalog catalog,
        IPackageDownloadService packageDownloadService,
        LaunchContentIntegrityCoordinator launchContentIntegrityCoordinator,
        LauncherPackageActivityService packageActivityService,
        LauncherPackageActivityAdmissionService activityAdmissionService,
        LauncherTileActionService tileActionService,
        ILauncherDialogService dialogService,
        ILauncherStringLocalizer stringLocalizer,
        ILogger<LauncherModificationDownloadCoordinator> logger)
    {
        _launcherPreferencesService = launcherPreferencesService ??
                                      throw new ArgumentNullException(nameof(launcherPreferencesService));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _packageDownloadService = packageDownloadService ??
                                  throw new ArgumentNullException(nameof(packageDownloadService));
        _launchContentIntegrityCoordinator = launchContentIntegrityCoordinator ??
                                             throw new ArgumentNullException(
                                                 nameof(launchContentIntegrityCoordinator));
        _packageActivityService = packageActivityService ??
                                  throw new ArgumentNullException(nameof(packageActivityService));
        _activityAdmissionService = activityAdmissionService ??
                                    throw new ArgumentNullException(nameof(activityAdmissionService));
        _tileActionService = tileActionService ?? throw new ArgumentNullException(nameof(tileActionService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Starts a modification download, or toggles pause when that modification is already downloading.
    /// </summary>
    public async Task StartOrToggleAsync(
        LauncherWindowContext context,
        ModificationViewModel modification)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(modification);

        if (_packageActivityService.GetActiveDownloadTask(modification) is { IsCompleted: false })
        {
            if (_packageActivityService.TryToggleDownloadPause(modification, out bool isPaused))
            {
                modification.SetPackageDownloadPaused(isPaused);
                _logger.LogInformation(
                    "{DownloadAction} active download for {ModificationName}.",
                    isPaused ? "Paused" : "Resumed",
                    modification.ContainerModification.Name);
            }

            return;
        }

        if (!await _activityAdmissionService.EnsureCanStartAsync(context.Owner))
        {
            _logger.LogWarning(
                "Download workflow for {ModificationName} was blocked by active package activity.",
                modification.ContainerModification.Name);
            return;
        }

        if (modification.ContainerModification.LatestVersion.Deprecated &&
            !await ConfirmDeprecatedModificationAsync(
                string.Format(
                    CultureInfo.CurrentCulture,
                    _stringLocalizer["Deprecated"],
                    modification.ContainerModification.Name),
                context.Owner))
        {
            _logger.LogInformation(
                "Download workflow for deprecated modification {ModificationName} was canceled.",
                modification.ContainerModification.Name);
            return;
        }

        _logger.LogInformation(
            "Starting download workflow for {ModificationName} {ContentVersion}.",
            modification.ContainerModification.Name,
            modification.LatestVersion.Version);
        context.ViewModel.SelectContent(modification);
        modification.SetUpdateButtonEnabled(false);
        await StartDownloadAsync(
            modification,
            context.Owner,
            () => CleanupCanceledDownload(context, modification));
    }

    /// <summary>
    ///     Cancels an active download after confirmation and reports whether removal handling was consumed.
    /// </summary>
    public async Task<bool> TryCancelActiveDownloadAsync(
        LauncherWindowContext context,
        ModificationViewModel modification)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(modification);

        Task<PackageDownloadResult>? downloadTask = _packageActivityService.GetActiveDownloadTask(modification);
        if (downloadTask is not { IsCompleted: false })
        {
            return false;
        }

        if (!await ConfirmDownloadCancellationAsync(modification, context.Owner))
        {
            _logger.LogInformation(
                "Download cancellation for {ModificationName} was declined.",
                modification.ContainerModification.Name);
            return true;
        }

        if (!ReferenceEquals(_packageActivityService.GetActiveDownloadTask(modification), downloadTask))
        {
            _logger.LogInformation(
                "Download cancellation for {ModificationName} was skipped because the active task changed.",
                modification.ContainerModification.Name);
            return true;
        }

        modification.SetUpdateButtonEnabled(false);
        if (!_packageActivityService.RequestDownloadCancellation(modification))
        {
            return true;
        }

        PackageDownloadResult result = await downloadTask;
        if (result.Status != PackageDownloadStatus.Canceled)
        {
            _logger.LogInformation(
                "Download cancellation for {ModificationName} lost the completion race with status {DownloadStatus}; committed content was preserved.",
                modification.ContainerModification.Name,
                result.Status);
            return true;
        }

        context.Content.RestoreFocuses();
        _logger.LogInformation(
            "Canceled download and cleaned partial content for {ModificationName}.",
            modification.ContainerModification.Name);
        return true;
    }

    /// <summary>
    ///     Attempts to start the selected package and waits for its lifecycle-owned terminal publication and cleanup.
    /// </summary>
    /// <param name="modification">The tile that projects package state.</param>
    /// <param name="owner">The owner window for package workflow dialogs.</param>
    /// <param name="canceledCleanup">Removes canceled partial content from the launcher UI and catalog.</param>
    public async Task StartDownloadAsync(
        ModificationViewModel modification,
        Window owner,
        Action canceledCleanup)
    {
        ArgumentNullException.ThrowIfNull(modification);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(canceledCleanup);

        if (!_packageActivityService.TryStartDownload(
                modification,
                modification.ContainerModification.Name,
                (progress, pauseController, cancellationToken) => DownloadAndFinalizeAsync(
                    modification,
                    owner,
                    progress,
                    pauseController,
                    cancellationToken),
                () =>
                {
                    modification.BeginPackageActivityPresentation();
                    modification.SetStatusMessage(_stringLocalizer["Preparing"]);
                    modification.SetUpdateButtonEnabled(true);
                },
                progress => DownloadProgressChanged(progress, modification),
                () => CleanupCanceledDownload(modification, canceledCleanup),
                result => PublishTerminalState(modification, result),
                out Task<PackageDownloadResult>? lifecycleTask))
        {
            await _activityAdmissionService.ShowInProgressAsync(owner);
            modification.SetUpdateButtonEnabled(true);
            return;
        }

        await (lifecycleTask ??
               throw new InvalidOperationException("Package download lifecycle task was not created."));
    }

    private async Task<PackageDownloadResult> DownloadAndFinalizeAsync(
        ModificationViewModel modification,
        Window owner,
        IProgress<PackageUpdateProgress> progress,
        PackageDownloadPauseController pauseController,
        CancellationToken cancellationToken)
    {
        PackageDownloadResult result;
        try
        {
            result = await _packageDownloadService.DownloadAsync(
                modification.ContainerModification,
                modification.LatestVersion,
                progress,
                cancellationToken,
                pauseController);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = PackageDownloadResult.Canceled();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Could not complete package download workflow for {ModificationName}.",
                modification.ContainerModification.Name);
            result = PackageDownloadResult.UnexpectedFailure(
                "An unexpected package download error occurred.");
        }

        if (result.Status != PackageDownloadStatus.Succeeded)
        {
            return result;
        }

        try
        {
            await FinalizeSuccessfulInstallAsync(modification);
        }
        catch (Exception exception)
        {
            // Installation already committed atomically. Post-install failures must never turn success into
            // cancellation because canceled cleanup would delete valid installed content.
            _logger.LogError(
                exception,
                "Package installation committed for {ModificationName}, but post-install processing failed.",
                modification.ContainerModification.Name);
            ReconcileCommittedInstall(modification);
            await ShowPostInstallWarningAsync(modification, owner);
        }

        return result;
    }

    private void DownloadProgressChanged(
        PackageUpdateProgress progress,
        ModificationViewModel modification)
    {
        if (PackageProgressTextFormatter.TryFormat(
                progress,
                _stringLocalizer,
                out string message,
                out int percentage))
        {
            modification.ReportPackageProgress(message, percentage);
        }
    }

    private async Task FinalizeSuccessfulInstallAsync(ModificationViewModel modification)
    {
        if (_launcherPreferencesService.Current.Shared.AutoDeleteOldVersions)
        {
            DeleteOutdatedModifications(modification);
        }

        _catalog.UpdateLocalModificationsData();
        modification.LatestVersion.Installation.Installed = true;
        await _launchContentIntegrityCoordinator.CaptureManagedInstallSnapshotAsync(modification.LatestVersion);
    }

    private void ReconcileCommittedInstall(ModificationViewModel modification)
    {
        modification.LatestVersion.Installation.Installed = true;

        try
        {
            _catalog.UpdateLocalModificationsData();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Could not reconcile committed package state for {ModificationName}.",
                modification.ContainerModification.Name);
        }
    }

    private async Task ShowPostInstallWarningAsync(
        ModificationViewModel modification,
        Window owner)
    {
        try
        {
            await _dialogService.ShowErrorAsync(
                new LauncherInfoDialogRequest(
                    _stringLocalizer["UnexpectedErrorTitle"],
                    _stringLocalizer["UnexpectedErrorDetails"]),
                owner);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Could not show the post-install warning for {ModificationName}.",
                modification.ContainerModification.Name);
        }
    }

    private void CleanupCanceledDownload(
        ModificationViewModel modification,
        Action canceledCleanup)
    {
        try
        {
            canceledCleanup();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Could not clean canceled package content for {ModificationName}.",
                modification.ContainerModification.Name);
        }
    }

    private void CleanupCanceledDownload(
        LauncherWindowContext context,
        ModificationViewModel modification)
    {
        _tileActionService.UninstallContentVersion(modification);
        modification.LatestVersion.Installation.Installed = false;
        context.ViewModel.SaveLauncherData();
        context.ViewModel.UpdateAddonAndPatchTabLabels();
    }

    private void PublishTerminalState(
        ModificationViewModel modification,
        PackageDownloadResult result)
    {
        try
        {
            modification.CompletePackageActivityPresentation(result);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Could not publish terminal package state for {ModificationName}.",
                modification.ContainerModification.Name);
        }
    }

    private void DeleteOutdatedModifications(ModificationViewModel modification)
    {
        foreach (LauncherContentVersion version in modification.ContainerModification.Versions)
        {
            if (version != modification.LatestVersion)
            {
                _catalog.UninstallVersion(version.ContentKey);
            }
        }
    }

    private Task<bool> ConfirmDownloadCancellationAsync(ModificationViewModel modification, Window owner)
    {
        return _dialogService.ShowWarningConfirmationAsync(
            new LauncherInfoDialogRequest(
                _stringLocalizer["CancelDownload"],
                string.Format(
                    CultureInfo.CurrentCulture,
                    _stringLocalizer["CancelDownloadDetails"],
                    modification.ContainerModification.Name)),
            _stringLocalizer["Yes"],
            owner);
    }

    private Task<bool> ConfirmDeprecatedModificationAsync(string details, Window owner)
    {
        return _dialogService.ShowWarningConfirmationAsync(
            new LauncherInfoDialogRequest(_stringLocalizer["Compatibility"], details),
            owner: owner);
    }
}
