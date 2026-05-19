using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.UI.Features.Launcher.Services;

/// <summary>
/// Coordinates pre-launch content readiness checks and user confirmations.
/// </summary>
internal sealed class LauncherLaunchReadinessCoordinator
{
    private readonly IGameExecutableDiscoveryService _gameExecutableDiscoveryService;

    private readonly ILauncherDialogService _dialogService;

    private readonly ILauncherStringLocalizer _stringLocalizer;

    public LauncherLaunchReadinessCoordinator(
        IGameExecutableDiscoveryService gameExecutableDiscoveryService,
        ILauncherDialogService dialogService,
        ILauncherStringLocalizer stringLocalizer)
    {
        _gameExecutableDiscoveryService = gameExecutableDiscoveryService ??
                                          throw new ArgumentNullException(nameof(gameExecutableDiscoveryService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
    }

    /// <summary>
    /// Checks whether the selected content can begin a launch workflow.
    /// </summary>
    public async Task<bool> EnsureSelectedContentCanLaunchAsync(
        IReadOnlyList<ModificationViewModel> selectedMods,
        IReadOnlyList<ModificationViewModel> selectedPatches,
        IReadOnlyList<ModificationViewModel> selectedAddons,
        Window owner)
    {
        ArgumentNullException.ThrowIfNull(selectedMods);
        ArgumentNullException.ThrowIfNull(selectedPatches);
        ArgumentNullException.ThrowIfNull(selectedAddons);
        ArgumentNullException.ThrowIfNull(owner);

        if (selectedMods.FirstOrDefault()?.ContainerModification.ModificationType == ModificationType.Advertising)
        {
            return false;
        }

        IReadOnlyList<ModificationViewModel> selectedContent =
            GetSelectedContent(selectedMods, selectedPatches, selectedAddons);

        if (!await ModificationsAreReadyToRunAsync(selectedContent, owner))
        {
            return false;
        }

        return await ModificationsAreInstalledAsync(selectedContent, owner);
    }

    /// <summary>
    /// Checks whether the selected executable can be launched.
    /// </summary>
    public async Task<bool> EnsureExecutableAvailableAsync(
        string? executableName,
        string missingExecutableMessage,
        Window owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missingExecutableMessage);
        ArgumentNullException.ThrowIfNull(owner);

        if (_gameExecutableDiscoveryService.IsExecutableAvailable(executableName))
        {
            return true;
        }

        await ShowErrorAsync(_stringLocalizer["LaunchAborted"], missingExecutableMessage, owner);
        return false;
    }

    /// <summary>
    /// Shows user confirmations for selected content updates and compatibility warnings.
    /// </summary>
    public async Task<bool> ConfirmSelectedContentWarningsAsync(
        IReadOnlyList<ModificationViewModel> selectedMods,
        IReadOnlyList<ModificationViewModel> selectedPatches,
        IReadOnlyList<ModificationViewModel> selectedAddons,
        Window owner)
    {
        ArgumentNullException.ThrowIfNull(selectedMods);
        ArgumentNullException.ThrowIfNull(selectedPatches);
        ArgumentNullException.ThrowIfNull(selectedAddons);
        ArgumentNullException.ThrowIfNull(owner);

        IReadOnlyList<ModificationViewModel> selectedContent =
            GetSelectedContent(selectedMods, selectedPatches, selectedAddons);

        return await ModificationsDontNeedUpdateAsync(selectedContent, owner) &&
               await ModificationsAreNotDeprecatedAsync(selectedContent, owner);
    }

    /// <summary>
    /// Checks whether selected content is already installed.
    /// </summary>
    private async Task<bool> ModificationsAreInstalledAsync(
        IReadOnlyList<ModificationViewModel> selectedContent,
        Window owner)
    {
        string mainMessage = _stringLocalizer["LaunchAborted"];
        foreach (ModificationViewModel viewModel in selectedContent)
        {
            LauncherContentVersion? selectedVersion = viewModel.SelectedVersion;
            if (selectedVersion != null && !selectedVersion.Installation.Installed)
            {
                await ShowErrorAsync(
                    mainMessage,
                    string.Format(_stringLocalizer["NotInstalled"], selectedVersion.Name),
                    owner);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks whether selected content has no active package downloads.
    /// </summary>
    private async Task<bool> ModificationsAreReadyToRunAsync(
        IReadOnlyList<ModificationViewModel> selectedContent,
        Window owner)
    {
        string mainMessage = _stringLocalizer["LaunchAborted"];
        foreach (ModificationViewModel viewModel in selectedContent)
        {
            if (viewModel.HasActivePackageActivity)
            {
                string modificationName = viewModel.ContainerModification.ModificationType == ModificationType.Mod
                    ? viewModel.SelectedVersion?.Name ?? viewModel.ContainerModification.Name
                    : viewModel.ContainerModification.Name;
                await ShowErrorAsync(
                    mainMessage,
                    string.Format(_stringLocalizer["InstallInProgress"], modificationName),
                    owner);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Confirms launch when a newer selected content version is available but not installed.
    /// </summary>
    private async Task<bool> ModificationsDontNeedUpdateAsync(
        IReadOnlyList<ModificationViewModel> selectedContent,
        Window owner)
    {
        ModificationViewModel? contentWithUpdate = FindWarningContent(
            selectedContent,
            includeModification: true,
            version => !version.Installation.Installed);
        if (contentWithUpdate == null)
        {
            return true;
        }

        string modsMessage = string.Format(
            _stringLocalizer["UninstalledUpdate"],
            contentWithUpdate.ContainerModification.LatestVersion.Name);
        return await _dialogService.ShowWarningConfirmationAsync(
            new LauncherInfoDialogRequest(_stringLocalizer["ModificationsWithUpdate"], modsMessage),
            owner: owner);
    }

    /// <summary>
    /// Confirms launch when selected content has compatibility deprecation warnings.
    /// </summary>
    private async Task<bool> ModificationsAreNotDeprecatedAsync(
        IReadOnlyList<ModificationViewModel> selectedContent,
        Window owner)
    {
        ModificationViewModel? deprecatedContent = FindWarningContent(
            selectedContent,
            includeModification: false,
            version => version.Deprecated);
        if (deprecatedContent == null)
        {
            return true;
        }

        string modsMessage = string.Format(
            _stringLocalizer["Deprecated"],
            deprecatedContent.ContainerModification.Name);
        return await _dialogService.ShowWarningConfirmationAsync(
            new LauncherInfoDialogRequest(_stringLocalizer["Compatibility"], modsMessage),
            owner: owner);
    }

    private static IReadOnlyList<ModificationViewModel> GetSelectedContent(
        IReadOnlyList<ModificationViewModel> selectedMods,
        IReadOnlyList<ModificationViewModel> selectedPatches,
        IReadOnlyList<ModificationViewModel> selectedAddons)
    {
        return selectedMods.Take(1)
            .Concat(selectedPatches.Take(1))
            .Concat(selectedAddons)
            .ToArray();
    }

    private static ModificationViewModel? FindWarningContent(
        IReadOnlyList<ModificationViewModel> selectedContent,
        bool includeModification,
        Func<LauncherContentVersion, bool> hasWarning)
    {
        ModificationViewModel? warningContent = null;

        foreach (ModificationViewModel viewModel in selectedContent)
        {
            ModificationType modificationType = viewModel.ContainerModification.ModificationType;
            bool isEligible = includeModification ||
                              modificationType is ModificationType.Patch or ModificationType.Addon;
            if (isEligible &&
                hasWarning(viewModel.ContainerModification.LatestVersion))
            {
                warningContent = viewModel;
                if (modificationType == ModificationType.Addon)
                {
                    break;
                }
            }
        }

        return warningContent;
    }

    private Task ShowErrorAsync(string mainMessage, string modMessage, Window owner)
    {
        return _dialogService.ShowErrorAsync(new LauncherInfoDialogRequest(mainMessage, modMessage), owner);
    }
}
