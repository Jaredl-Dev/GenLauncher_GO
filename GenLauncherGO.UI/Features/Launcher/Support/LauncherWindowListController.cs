using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Launcher.ViewModels;
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.UI.Features.Launcher.Support;

/// <summary>
/// Coordinates Avalonia list-control behavior for the main launcher window.
/// </summary>
internal sealed class LauncherWindowListController
{
    private readonly Window _owner;

    private readonly MainWindowViewModel _viewModel;

    private readonly LauncherRuntimeContext _runtimeContext;

    private readonly ListBox _modsList;

    private readonly ListBox _patchesList;

    private readonly ListBox _addonsList;

    private bool _initialized;

    public LauncherWindowListController(
        Window owner,
        MainWindowViewModel viewModel,
        LauncherRuntimeContext runtimeContext,
        ListBox modsList,
        ListBox patchesList,
        ListBox addonsList)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _runtimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
        _modsList = modsList ?? throw new ArgumentNullException(nameof(modsList));
        _patchesList = patchesList ?? throw new ArgumentNullException(nameof(patchesList));
        _addonsList = addonsList ?? throw new ArgumentNullException(nameof(addonsList));
    }

    /// <summary>
    /// Applies restored selection visuals after the view model completes its persistence-boundary restore.
    /// </summary>
    public void Initialize()
    {
        UpdateVisuals();
        _initialized = true;
    }

    /// <summary>
    /// Refreshes visible child content from current semantic parent selections.
    /// </summary>
    public void RefreshTabs()
    {
        if (!_viewModel.RefreshTabs())
        {
            return;
        }

        _viewModel.RefreshPatchesList();
        _viewModel.RefreshAddonsList();
        _viewModel.UpdateAddonAndPatchTabLabels();
        RestoreFocuses();
    }

    /// <summary>
    /// Refreshes add-ons for the current semantic modification and patch selections.
    /// </summary>
    public void RefreshAddonsList()
    {
        _viewModel.RefreshAddonsList();
        RestoreFocuses();
    }

    /// <summary>
    /// Restores focus to selected list items.
    /// </summary>
    public void RestoreFocuses()
    {
        FocusSelectedItem(_modsList);
        FocusSelectedItem(_patchesList);

        foreach (ModificationViewModel addon in _viewModel.SelectedAddons)
        {
            FocusItem(_addonsList, addon);
        }
    }

    /// <summary>
    /// Applies launcher theme resources and refreshes modification, patch, and add-on tiles.
    /// </summary>
    public void UpdateVisuals()
    {
        LauncherThemeResourceApplier.Apply(_owner, _runtimeContext.Colors);

        foreach (ModificationViewModel modData in _viewModel.ModsListSource)
        {
            modData.RefreshPresentation();
        }

        foreach (ModificationViewModel patchData in _viewModel.PatchesListSource)
        {
            patchData.RefreshPresentation();
        }

        foreach (ModificationViewModel addonData in _viewModel.AddonsListSource)
        {
            addonData.RefreshPresentation();
        }
    }

    public async Task HandleModsListSelectionChangedAsync(SelectionChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (!_initialized || e.Source is not ListBox)
        {
            return;
        }

        ModificationViewModel? addedModification = e.AddedItems.OfType<ModificationViewModel>().FirstOrDefault();
        if (addedModification != null)
        {
            bool replacesSameContent = e.RemovedItems.OfType<ModificationViewModel>().Any(removed =>
                removed.ContainerModification.ContentKey == addedModification.ContainerModification.ContentKey);
            if (!replacesSameContent)
            {
                _viewModel.SetMainControlsEnabled(false);
                try
                {
                    await _viewModel.UpdateAddonsAndPatchesAsync(addedModification.ContainerModification);
                    RefreshTabs();
                }
                finally
                {
                    _viewModel.SetMainControlsEnabled(true);
                }
            }

            e.Handled = true;
        }
        else if (e.RemovedItems.Count > 0 && _viewModel.SelectedModifications.Count == 0)
        {
            RefreshTabs();
        }

        RestoreFocuses();
    }

    public void HandlePatchesListSelectionChanged(SelectionChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (!_initialized ||
            e.Source is not ListBox listBox ||
            !String.Equals(listBox.Name, "PatchesList", StringComparison.Ordinal))
        {
            return;
        }

        RefreshAddonsList();
        _viewModel.UpdateAddonAndPatchTabLabels();
    }

    public void HandleAddonsListSelectionChanged(SelectionChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (!_initialized ||
            e.Source is not ListBox listBox ||
            !String.Equals(listBox.Name, "AddonsList", StringComparison.Ordinal))
        {
            return;
        }

        _viewModel.UpdateAddonAndPatchTabLabels();
    }

    public void HandleVersionsListSelectionChanged(object sender)
    {
        if (sender is not ComboBox comboBox ||
            comboBox.SelectedItem is not ModificationVersionSelection versionSelection)
        {
            return;
        }

        foreach (LauncherContentVersion version in versionSelection.ModificationViewModel.ContainerModification
                     .Versions)
        {
            version.Installation.IsSelected = version.ContentKey.HasVersion(versionSelection.VersionName);
        }

        _viewModel.UpdateAddonAndPatchTabLabels();
    }

    private static void FocusSelectedItem(ListBox listBox)
    {
        FocusItem(listBox, listBox.SelectedItem);
    }

    /// <summary>
    /// Focuses an item container if it has been generated.
    /// </summary>
    /// <param name="listBox">The list box that owns the item.</param>
    /// <param name="item">The item to focus.</param>
    private static void FocusItem(ListBox listBox, object? item)
    {
        if (item == null)
        {
            return;
        }

        listBox.ScrollIntoView(item);
        listBox.Focus();
    }
}
