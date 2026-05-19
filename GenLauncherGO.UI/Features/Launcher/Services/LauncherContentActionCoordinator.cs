using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Shell.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Contracts;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Support;
using GenLauncherGO.UI.Features.Launcher.ViewModels;
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Shared.Localization;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.UI.Features.Launcher.Services;

/// <summary>
///     Coordinates main-window content imports, downloads, removal, links, and manual image replacement.
/// </summary>
internal sealed class LauncherContentActionCoordinator
{
    private readonly LauncherPackageActivityAdmissionService _activityAdmissionService;
    private readonly ILauncherDialogService _dialogService;
    private readonly LauncherModificationDownloadCoordinator _downloadCoordinator;
    private readonly ILauncherFilePicker _filePicker;
    private readonly LaunchContentIntegrityCoordinator _launchContentIntegrityCoordinator;
    private readonly ILauncherShellService _launcherShellService;
    private readonly ILogger<LauncherContentActionCoordinator> _logger;
    private readonly LauncherManualImportCoordinator _manualImportCoordinator;
    private readonly IModificationImageFileService _modificationImageFileService;
    private readonly ILauncherStringLocalizer _stringLocalizer;
    private readonly LauncherTileActionService _tileActionService;

    public LauncherContentActionCoordinator(
        LauncherTileActionService tileActionService,
        LauncherManualImportCoordinator manualImportCoordinator,
        LauncherModificationDownloadCoordinator downloadCoordinator,
        LauncherPackageActivityAdmissionService activityAdmissionService,
        ILauncherShellService launcherShellService,
        ILauncherFilePicker filePicker,
        LaunchContentIntegrityCoordinator launchContentIntegrityCoordinator,
        ILauncherStringLocalizer stringLocalizer,
        IModificationImageFileService modificationImageFileService,
        ILauncherDialogService dialogService,
        ILogger<LauncherContentActionCoordinator> logger)
    {
        _tileActionService = tileActionService ?? throw new ArgumentNullException(nameof(tileActionService));
        _manualImportCoordinator = manualImportCoordinator ??
                                   throw new ArgumentNullException(nameof(manualImportCoordinator));
        _downloadCoordinator = downloadCoordinator ?? throw new ArgumentNullException(nameof(downloadCoordinator));
        _activityAdmissionService = activityAdmissionService ??
                                    throw new ArgumentNullException(nameof(activityAdmissionService));
        _launcherShellService = launcherShellService ?? throw new ArgumentNullException(nameof(launcherShellService));
        _filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));
        _launchContentIntegrityCoordinator = launchContentIntegrityCoordinator ??
                                             throw new ArgumentNullException(
                                                 nameof(launchContentIntegrityCoordinator));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _modificationImageFileService = modificationImageFileService ??
                                        throw new ArgumentNullException(nameof(modificationImageFileService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Opens repository modification selection and adds the selected modification.
    /// </summary>
    public async Task AddRepositoryModificationAsync(
        LauncherWindowContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!await _activityAdmissionService.EnsureCanStartAsync(context.Owner))
        {
            _logger.LogWarning("Repository modification add workflow was blocked by active package activity.");
            context.Content.RestoreFocuses();
            return;
        }

        IReadOnlyList<string> availableModificationNames =
            context.ViewModel.GetNotAddedRepositoryModificationNames();

        string? modificationName = await _dialogService.ShowModificationSelectionAsync(
            availableModificationNames,
            context.Owner);
        if (!string.IsNullOrWhiteSpace(modificationName))
        {
            _logger.LogInformation(
                "Adding repository modification {ModificationName}.",
                modificationName);
            context.ViewModel.SetMainControlsEnabled(false);
            try
            {
                await context.ViewModel.AddModToListAsync(modificationName, cancellationToken);
            }
            finally
            {
                context.ViewModel.SetMainControlsEnabled(true);
            }
        }
        else
        {
            _logger.LogDebug("Repository modification add workflow was canceled.");
        }

        context.Content.RestoreFocuses();
    }

    /// <summary>
    ///     Opens one of the external links on a modification tile.
    /// </summary>
    public void OpenTileLink(ModificationViewModel modification, LauncherTileLinkKind kind)
    {
        ArgumentNullException.ThrowIfNull(modification);
        ApplyTileLinkAction(
            modification,
            LauncherTileActionService.GetLinkAction(modification.ContainerModification, kind));
    }

    /// <summary>
    ///     Deletes an installed version from the catalog and refreshes tile state.
    /// </summary>
    public async Task DeleteVersionAsync(
        LauncherWindowContext context,
        ModificationVersionSelection versionData)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(versionData);

        // A delete button lives inside the add-on ListBoxItem. Toggle selection can clear the outer item before the
        // nested click arrives, but invoking an action that was visible on a selected tile must preserve that selection.
        context.ViewModel.SelectContent(versionData.ModificationViewModel);

        LauncherPackageActivityService.LauncherPackageActivityLease? activityLease =
            await _activityAdmissionService.TryReserveAsync(versionData.SelectedVersion.Name, context.Owner);
        if (activityLease == null)
        {
            _logger.LogWarning(
                "Content removal for {ContentName} {ContentVersion} was blocked by active package activity.",
                versionData.SelectedVersion.Name,
                versionData.SelectedVersion.Version);
            return;
        }

        bool controlsDisabled = false;
        try
        {
            if (!await ConfirmContentRemovalAsync(versionData.SelectedVersion.Name, context.Owner))
            {
                _logger.LogInformation(
                    "Content removal for {ContentName} {ContentVersion} was declined.",
                    versionData.SelectedVersion.Name,
                    versionData.SelectedVersion.Version);
                return;
            }

            context.ViewModel.SetMainControlsEnabled(false);
            controlsDisabled = true;
            _logger.LogInformation(
                "Deleting launcher content version {ContentName} {ContentVersion}.",
                versionData.SelectedVersion.Name,
                versionData.SelectedVersion.Version);

            // Directory-tree deletion and the following disk reconciliation are synchronous. Keep both away from the
            // Avalonia UI thread so a large add-on cannot stop input and queue repeated removal clicks.
            bool removedContentCard = await Task.Run(() => _tileActionService.DeleteVersion(versionData));
            if (removedContentCard)
            {
                context.ViewModel.RemoveContentFromList(versionData.ModificationViewModel);
                context.Content.RefreshTabs();
            }
            else
            {
                versionData.ModificationViewModel.RefreshFromModelAndPresentation();
            }

            context.ViewModel.UpdateAddonAndPatchTabLabels();
            context.ViewModel.SaveLauncherData();
        }
        finally
        {
            if (controlsDisabled)
            {
                context.ViewModel.SetMainControlsEnabled(true);
            }

            activityLease.Dispose();
        }
    }

    /// <summary>
    ///     Removes an uninstalled modification card, or cancels its active download and retains the card.
    /// </summary>
    public async Task DeleteModificationAsync(
        LauncherWindowContext context,
        ModificationViewModel modification)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(modification);

        if (await _downloadCoordinator.TryCancelActiveDownloadAsync(context, modification))
        {
            return;
        }

        if (modification.ContainerModification.ModificationType != ModificationType.Mod ||
            modification.ContainerModification.Installed)
        {
            return;
        }

        if (!await ConfirmListRemovalAsync(modification.ContainerModification.Name, context.Owner))
        {
            _logger.LogInformation(
                "List removal for {ModificationName} was declined.",
                modification.ContainerModification.Name);
            return;
        }

        _logger.LogInformation(
            "Removing uninstalled modification {ModificationName}.",
            modification.ContainerModification.Name);
        RemoveUninstalledModification(context.ViewModel, context.Content, modification);
        context.Content.RestoreFocuses();
    }

    /// <summary>
    ///     Runs the requested manual content import workflow.
    /// </summary>
    public async Task ImportManualContentAsync(
        LauncherWindowContext context,
        ModificationType kind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string activityDisplayName = kind switch
        {
            ModificationType.Mod => _stringLocalizer["AddModFromFiles"],
            ModificationType.Patch => context.ViewModel.ManualAddPatchText,
            ModificationType.Addon => context.ViewModel.ManualAddAddonText,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown manual import kind.")
        };
        LauncherPackageActivityService.LauncherPackageActivityLease? activityLease =
            await _activityAdmissionService.TryReserveAsync(activityDisplayName, context.Owner);
        if (activityLease == null)
        {
            _logger.LogWarning(
                "Manual content import workflow {ImportKind} was blocked by active package activity.",
                kind);
            return;
        }

        try
        {
            context.ViewModel.SetMainControlsEnabled(false);
            _logger.LogInformation(
                "Starting manual content import workflow {ImportKind}.",
                kind);
            context.ViewModel.ApplySelectionToPersistenceModel();
            LauncherContent? importedContent = await _manualImportCoordinator.ImportAsync(
                kind,
                context.Owner,
                kind == ModificationType.Mod
                    ? null
                    : context.ViewModel.GetSelectedModificationName(),
                cancellationToken);

            if (importedContent != null)
            {
                context.ViewModel.AddImportedContentToList(importedContent);
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
            context.ViewModel.SetMainControlsEnabled(true);
        }
    }

    /// <summary>
    ///     Starts a modification package download, or toggles pause for the active download.
    /// </summary>
    public async Task UpdateModificationAsync(
        LauncherWindowContext context,
        ModificationViewModel modification)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(modification);

        LauncherTileLinkAction advertisingAction =
            LauncherTileActionService.GetAdvertisingDownloadAction(modification.ContainerModification);
        if (!string.IsNullOrEmpty(advertisingAction.Uri))
        {
            _logger.LogInformation(
                "Opening advertising download link for {ModificationName}.",
                modification.ContainerModification.Name);
            ApplyTileLinkAction(modification, advertisingAction);
            return;
        }

        await _downloadCoordinator.StartOrToggleAsync(context, modification);
    }

    /// <summary>
    ///     Replaces the tile image for trusted manual content.
    /// </summary>
    public async Task ChangeVersionImageAsync(
        LauncherWindowContext context,
        ModificationViewModel container,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(container);

        MainWindowViewModel viewModel = context.ViewModel;
        Window owner = context.Owner;
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

    private void RemoveUninstalledModification(
        MainWindowViewModel viewModel,
        LauncherWindowListController content,
        ModificationViewModel modification)
    {
        _tileActionService.DiscardContentVersion(modification);
        viewModel.RemoveContentFromList(modification);
        content.RefreshTabs();
        viewModel.SaveLauncherData();
        viewModel.UpdateAddonAndPatchTabLabels();
    }

    private Task<bool> ConfirmListRemovalAsync(string contentName, Window owner)
    {
        return _dialogService.ShowWarningConfirmationAsync(
            new LauncherInfoDialogRequest(
                _stringLocalizer["RemoveFromListConfirmation"],
                string.Format(CultureInfo.CurrentCulture,
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
                string.Format(CultureInfo.CurrentCulture,
                    _stringLocalizer["RemoveContentDetails"],
                    contentName)),
            _stringLocalizer["Remove"],
            owner);
    }

    private void ApplyTileLinkAction(ModificationViewModel modification, LauncherTileLinkAction action)
    {
        if (action.ShowThankYouMessage)
        {
            modification.SetStatusMessage(_stringLocalizer["ThankYou"]);
        }

        if (!string.IsNullOrEmpty(action.Uri))
        {
            _launcherShellService.OpenUri(action.Uri);
        }
    }
}
