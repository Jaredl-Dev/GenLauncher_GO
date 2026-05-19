using System;
using Avalonia.Controls;
using GenLauncherGO.UI.Features.Mods.ViewModels;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.UI.Features.Mods.Views;

/// <summary>
/// Allows the user to choose a repository modification to add to the launcher list.
/// </summary>
internal partial class AddModificationWindow : Window
{
    private readonly ColorsInfo _colors = null!;

    public AddModificationWindow()
    {
        ViewModel = null!;
        InitializeComponent();
    }

    internal AddModificationWindow(AddModificationViewModel viewModel, ColorsInfo colors)
        : this()
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _colors = colors ?? throw new ArgumentNullException(nameof(colors));
        DataContext = ViewModel;
        ViewModel.CloseRequested += ViewModel_CloseRequested;
        SetColors();
    }

    internal AddModificationViewModel ViewModel { get; }

    /// <summary>
    /// Gets the selected modification name after the dialog is accepted.
    /// </summary>
    public string? SelectedModificationName => ViewModel.SelectedModificationName;

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.CancelMetadataLoading();
            ViewModel.CloseRequested -= ViewModel_CloseRequested;
        }

        base.OnClosed(e);
    }

    /// <inheritdoc />
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        SearchBox.Focus();
    }

    private void ViewModel_CloseRequested(object? sender, EventArgs e)
    {
        if (IsDialog && ViewModel.DialogResult.HasValue)
        {
            Close(ViewModel.DialogResult.Value);
            return;
        }

        Close();
    }

    private void SetColors()
    {
        LauncherThemeResourceApplier.Apply(this, _colors, includeBackgroundImage: false);
    }
}
