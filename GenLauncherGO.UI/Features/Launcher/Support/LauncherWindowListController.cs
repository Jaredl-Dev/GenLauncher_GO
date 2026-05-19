using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.UI.Features.Launcher.ViewModels;
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.UI.Features.Launcher.Support;

/// <summary>
///     Coordinates Avalonia list-control behavior for the main launcher window.
/// </summary>
internal sealed class LauncherWindowListController
{
    private readonly ListBox _addonsList;

    private readonly ListBox _modsList;

    private readonly ListBox _patchesList;

    private readonly LauncherRuntimeContext _runtimeContext;

    private readonly MainWindowViewModel _viewModel;

    private bool _initialized;

    public LauncherWindowListController(
        MainWindowViewModel viewModel,
        LauncherRuntimeContext runtimeContext,
        ListBox modsList,
        ListBox patchesList,
        ListBox addonsList)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _runtimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
        _modsList = modsList ?? throw new ArgumentNullException(nameof(modsList));
        _patchesList = patchesList ?? throw new ArgumentNullException(nameof(patchesList));
        _addonsList = addonsList ?? throw new ArgumentNullException(nameof(addonsList));
    }

    /// <summary>
    ///     Applies restored selection visuals after the view model completes its persistence-boundary restore.
    /// </summary>
    public void Initialize()
    {
        UpdateVisuals();
        _initialized = true;
    }

    /// <summary>
    ///     Re-skins the launcher for the currently selected modification.
    /// </summary>
    /// <remarks>
    ///     A modification may publish its own palette in the remote manifest; while it is selected the launcher wears
    ///     it, and it falls back to the active game's palette per slot for anything the modification left out. Colours
    ///     are applied whether or not the modification also supplies background artwork, so a mod that declares colours
    ///     alone never leaves the previously selected mod's palette on screen.
    /// </remarks>
    private void ApplySelectedModificationTheme()
    {
        ModificationViewModel? selected = _viewModel.SelectedModifications.FirstOrDefault();
        _runtimeContext.Colors = LauncherThemeResolver.Resolve(
            selected?.PublishedTheme,
            _runtimeContext.CurrentlyManagedGame,
            selected?.LoadPublishedThemeBackground());
    }

    /// <summary>
    ///     Refreshes visible child content from current semantic parent selections.
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
    ///     Refreshes add-ons for the current semantic modification and patch selections.
    /// </summary>
    public void RefreshAddonsList()
    {
        _viewModel.RefreshAddonsList();
        RestoreFocuses();
    }

    /// <summary>
    ///     Restores focus to selected list items.
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
    ///     Applies the selected modification's published palette and refreshes modification, patch, and add-on tiles.
    /// </summary>
    /// <remarks>
    ///     Tiles push brushes into bindable properties instead of resolving them through <c>DynamicResource</c>, so
    ///     they need this nudge after a theme change; the windows themselves follow application-scoped resources.
    /// </remarks>
    public void UpdateVisuals()
    {
        ApplySelectedModificationTheme();

        foreach (ModificationViewModel modification in _viewModel.ModsListSource)
        {
            modification.RefreshPresentation();
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
            _viewModel.SelectContent(addedModification);
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
        else if (e.RemovedItems.OfType<ModificationViewModel>().FirstOrDefault() is { } removedModification)
        {
            removedModification.IsSelected = false;
            if (_viewModel.SelectedModifications.Count == 0)
            {
                RefreshTabs();
            }
        }

        // The shell wears the selected modification's published palette, so a selection change is a theme change.
        UpdateVisuals();
        RestoreFocuses();
    }

    public void HandleChildContentListSelectionChanged(SelectionChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (!_initialized || e.Source is not ListBox listBox)
        {
            return;
        }

        bool isPatchesList = string.Equals(listBox.Name, "PatchesList", StringComparison.Ordinal);
        bool isAddonsList = string.Equals(listBox.Name, "AddonsList", StringComparison.Ordinal);
        if (!isPatchesList && !isAddonsList)
        {
            return;
        }

        foreach (ModificationViewModel modification in e.AddedItems.OfType<ModificationViewModel>())
        {
            _viewModel.SelectContent(modification);
        }

        if (isPatchesList)
        {
            RefreshAddonsList();
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
            version.Installation.IsSelected = version.ContentKey == versionSelection.SelectedVersion.ContentKey;
        }

        _viewModel.UpdateAddonAndPatchTabLabels();
    }

    private static void FocusSelectedItem(ListBox listBox)
    {
        FocusItem(listBox, listBox.SelectedItem);
    }

    /// <summary>
    ///     Focuses an item container if it has been generated.
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
