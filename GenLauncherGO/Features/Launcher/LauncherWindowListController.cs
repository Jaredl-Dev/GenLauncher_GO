using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Features.Mods;
using GenLauncherGO.Features.Startup;
using GenLauncherGO.Shared.Themes;

namespace GenLauncherGO.Features.Launcher;

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
    ///     Persists the active game's current modification-list position.
    /// </summary>
    public void SaveModsListVerticalOffset()
    {
        _viewModel.SaveModsListVerticalOffset(
            LauncherDragDropController.GetVerticalScrollOffset(_modsList));
    }

    /// <summary>
    ///     Restores the active game's modification-list position after its items have changed.
    /// </summary>
    public void RestoreModsListVerticalOffset()
    {
        _modsList.UpdateLayout();
        LauncherDragDropController.RestoreVerticalScrollOffset(
            _modsList,
            _viewModel.ModsListVerticalOffset);
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

        IReadOnlyList<ModificationViewModel> addedModifications = ResolveSelectionChange(e.AddedItems);
        IReadOnlyList<ModificationViewModel> removedModifications = ResolveSelectionChange(e.RemovedItems);

        ModificationViewModel? addedModification = addedModifications.FirstOrDefault();
        if (addedModification != null)
        {
            _viewModel.SelectContent(addedModification);
            bool replacesSameContent = removedModifications.Any(removed =>
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
        else if (removedModifications.FirstOrDefault() is { } removedModification)
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

        bool isPatchesList = ReferenceEquals(listBox, _patchesList);
        bool isAddonsList = ReferenceEquals(listBox, _addonsList);
        if (!isPatchesList && !isAddonsList)
        {
            return;
        }

        foreach (ModificationViewModel modification in ResolveSelectionChange(e.AddedItems))
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

    /// <summary>
    ///     Reads the tiles named by one selection-changed list, treating a selection the list can no longer resolve
    ///     as an empty change.
    /// </summary>
    /// <remarks>
    ///     These lists are live views over the selection model rather than snapshots taken when the event was raised.
    ///     Deleting content rebuilds the list while the selection still names the positions items used to occupy, so
    ///     enumerating the view indexes past the end of the rebuilt list. That notification describes a selection that
    ///     no longer exists, and there is nothing left for the handler to apply to it.
    /// </remarks>
    private static IReadOnlyList<ModificationViewModel> ResolveSelectionChange(IList items)
    {
        try
        {
            return items.OfType<ModificationViewModel>().ToList();
        }
        catch (ArgumentOutOfRangeException)
        {
            return [];
        }
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
