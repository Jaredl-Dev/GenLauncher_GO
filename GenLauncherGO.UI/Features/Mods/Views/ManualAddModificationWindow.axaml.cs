using System;
using Avalonia.Controls;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Mods.ViewModels;
using GenLauncherGO.UI.Shared.Controls;
using GenLauncherGO.UI.Shared.Dialogs;

namespace GenLauncherGO.UI.Features.Mods.Views;

/// <summary>
///     Collects names and versions for manually imported launcher content.
/// </summary>
internal partial class ManualAddModificationWindow : Window
{
    public ManualAddModificationWindow()
    {
        ViewModel = null!;
        InitializeComponent();
        LauncherWindowScaling.Attach(this);
    }

    internal ManualAddModificationWindow(ManualAddModificationViewModel viewModel)
        : this()
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;
        ViewModel.CloseRequested += ViewModel_CloseRequested;
    }

    internal ManualAddModificationViewModel ViewModel { get; }

    /// <summary>
    ///     Gets the entered import details after the dialog is accepted.
    /// </summary>
    internal ManualModificationDialogResult? ImportResult => ViewModel.ImportResult;

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        ViewModel?.CloseRequested -= ViewModel_CloseRequested;

        base.OnClosed(e);
    }

    private void ViewModel_CloseRequested(object? sender, EventArgs e)
    {
        AvaloniaDialog.CloseWithResult(this, ViewModel.DialogResult);
    }
}
