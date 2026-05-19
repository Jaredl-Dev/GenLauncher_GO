using System;
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
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Shared.Formatting;
using GenLauncherGO.UI.Shared.Localization;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.UI.Features.Launcher.Services;

/// <summary>
/// Sequences package download, installation, and post-install processing for launcher modification tiles.
/// </summary>
internal sealed class LauncherModificationDownloadCoordinator
{
    private readonly ILauncherPreferencesService _launcherPreferencesService;
    private readonly ILauncherContentCatalog _catalog;
    private readonly IPackageDownloadService _packageDownloadService;
    private readonly LaunchContentIntegrityCoordinator _launchContentIntegrityCoordinator;
    private readonly LauncherPackageActivityService _packageActivityService;
    private readonly ILauncherDialogService _dialogService;
    private readonly ILauncherStringLocalizer _stringLocalizer;
    private readonly ILogger<LauncherModificationDownloadCoordinator> _logger;

    /// <summary>
    /// Initializes the package download sequencing workflow.
    /// </summary>
    public LauncherModificationDownloadCoordinator(
        ILauncherPreferencesService launcherPreferencesService,
        ILauncherContentCatalog catalog,
        IPackageDownloadService packageDownloadService,
        LaunchContentIntegrityCoordinator launchContentIntegrityCoordinator,
        LauncherPackageActivityService packageActivityService,
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
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Attempts to start the selected package and waits for its lifecycle-owned terminal publication and cleanup.
    /// </summary>
    /// <param name="modData">The tile that projects package state.</param>
    /// <param name="owner">The owner window for package workflow dialogs.</param>
    /// <param name="canceledCleanup">Removes canceled partial content from the launcher UI and catalog.</param>
    public async Task StartDownloadAsync(
        ModificationViewModel modData,
        Window owner,
        Action canceledCleanup)
    {
        ArgumentNullException.ThrowIfNull(modData);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(canceledCleanup);

        if (!_packageActivityService.TryStartDownload(
                modData,
                modData.ContainerModification.Name,
                (progress, pauseController, cancellationToken) => DownloadAndFinalizeAsync(
                    modData,
                    owner,
                    progress,
                    pauseController,
                    cancellationToken),
                () =>
                {
                    modData.BeginPackageActivityPresentation();
                    modData.SetStatusMessage(_stringLocalizer["Preparing"]);
                    modData.SetUpdateButtonEnabled(true);
                },
                progress => DownloadProgressChanged(progress, modData),
                () => CleanupCanceledDownload(modData, canceledCleanup),
                result => PublishTerminalState(modData, result),
                out Task<PackageDownloadResult>? lifecycleTask))
        {
            await _dialogService.ShowInfoAsync(CreatePackageActivityInProgressRequest(), owner);
            modData.SetUpdateButtonEnabled(true);
            return;
        }

        await (lifecycleTask ??
               throw new InvalidOperationException("Package download lifecycle task was not created."));
    }

    private async Task<PackageDownloadResult> DownloadAndFinalizeAsync(
        ModificationViewModel modData,
        Window owner,
        IProgress<PackageUpdateProgress> progress,
        PackageDownloadPauseController pauseController,
        CancellationToken cancellationToken)
    {
        PackageDownloadResult result;
        try
        {
            result = await _packageDownloadService.DownloadAsync(
                modData.ContainerModification,
                modData.LatestVersion,
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
                modData.ContainerModification.Name);
            result = PackageDownloadResult.UnexpectedFailure(
                "An unexpected package download error occurred.");
        }

        if (result.Status != PackageDownloadStatus.Succeeded)
        {
            return result;
        }

        try
        {
            await FinalizeSuccessfulInstallAsync(modData);
        }
        catch (Exception exception)
        {
            // Installation already committed atomically. Post-install failures must never turn success into
            // cancellation because canceled cleanup would delete valid installed content.
            _logger.LogError(
                exception,
                "Package installation committed for {ModificationName}, but post-install processing failed.",
                modData.ContainerModification.Name);
            ReconcileCommittedInstall(modData);
            await ShowPostInstallWarningAsync(modData, owner);
        }

        return result;
    }

    private void DownloadProgressChanged(
        PackageUpdateProgress progress,
        ModificationViewModel modData)
    {
        if (PackageProgressTextFormatter.TryFormat(
                progress,
                _stringLocalizer,
                out string message,
                out int percentage))
        {
            modData.ReportPackageProgress(message, percentage);
        }
    }

    private async Task FinalizeSuccessfulInstallAsync(ModificationViewModel modData)
    {
        if (_launcherPreferencesService.Current.Shared.AutoDeleteOldVersions)
        {
            DeleteOutdatedModifications(modData);
        }

        _catalog.UpdateLocalModificationsData();
        modData.LatestVersion.Installation.Installed = true;
        await _launchContentIntegrityCoordinator.CaptureManagedInstallSnapshotAsync(modData.LatestVersion);
    }

    private void ReconcileCommittedInstall(ModificationViewModel modData)
    {
        modData.LatestVersion.Installation.Installed = true;

        try
        {
            _catalog.UpdateLocalModificationsData();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Could not reconcile committed package state for {ModificationName}.",
                modData.ContainerModification.Name);
        }
    }

    private async Task ShowPostInstallWarningAsync(
        ModificationViewModel modData,
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
                modData.ContainerModification.Name);
        }
    }

    private void CleanupCanceledDownload(
        ModificationViewModel modData,
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
                modData.ContainerModification.Name);
        }
    }

    private void PublishTerminalState(
        ModificationViewModel modData,
        PackageDownloadResult result)
    {
        try
        {
            modData.CompletePackageActivityPresentation(result);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Could not publish terminal package state for {ModificationName}.",
                modData.ContainerModification.Name);
        }
    }

    private void DeleteOutdatedModifications(ModificationViewModel modData)
    {
        foreach (LauncherContentVersion versionData in modData.ContainerModification.Versions)
        {
            if (versionData != modData.LatestVersion)
            {
                _catalog.UninstallVersion(versionData.ContentKey);
            }
        }
    }

    private LauncherInfoDialogRequest CreatePackageActivityInProgressRequest()
    {
        return new LauncherInfoDialogRequest(
            _stringLocalizer["PackageActivityInProgress"],
            _stringLocalizer["PackageActivityInProgressDetails"]);
    }
}
