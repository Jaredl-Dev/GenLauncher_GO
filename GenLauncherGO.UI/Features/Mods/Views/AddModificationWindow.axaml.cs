using System;
using Avalonia.Controls;
using GenLauncherGO.UI.Features.Mods.ViewModels;
using GenLauncherGO.UI.Shared.Controls;
using GenLauncherGO.UI.Shared.Dialogs;

namespace GenLauncherGO.UI.Features.Mods.Views;

/// <summary>
///     Allows the user to choose a repository modification to add to the launcher list.
/// </summary>
internal partial class AddModificationWindow : Window
{
    public AddModificationWindow()
    {
        ViewModel = null!;
        InitializeComponent();
        LauncherWindowScaling.Attach(this);
    }

    internal AddModificationWindow(AddModificationViewModel viewModel)
        : this()
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;
        ViewModel.CloseRequested += ViewModel_CloseRequested;
    }

    internal AddModificationViewModel ViewModel { get; }

    /// <summary>
    ///     Gets the selected modification name after the dialog is accepted.
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
        AvaloniaDialog.CloseWithResult(this, ViewModel.DialogResult);
    }
}
