using System;
using Avalonia.Controls;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Mods.ViewModels;
using GenLauncherGO.UI.Shared.Controls;
using GenLauncherGO.UI.Shared.Dialogs;

namespace GenLauncherGO.UI.Features.Mods.Views;

/// <summary>
///     Displays a themed launcher confirmation or information dialog.
/// </summary>
internal partial class InfoWindow : Window
{
    public InfoWindow()
    {
        ViewModel = null!;
        InitializeComponent();
        LauncherWindowScaling.Attach(this);
    }

    internal InfoWindow(
        LauncherInfoDialogRequest request,
        InfoDialogKind kind,
        string? continueText = null,
        string? cancelText = null,
        string? actionText = null)
        : this()
    {
        ArgumentNullException.ThrowIfNull(request);

        ViewModel = new InfoDialogViewModel(request, kind, continueText, cancelText, actionText);
        DataContext = ViewModel;
        ViewModel.CloseRequested += ViewModel_CloseRequested;
    }

    internal InfoDialogViewModel ViewModel { get; }

    internal bool Accepted => ViewModel?.DialogResult == true;

    private void ViewModel_CloseRequested(object? sender, EventArgs e)
    {
        AvaloniaDialog.CloseWithResult(this, ViewModel.DialogResult);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        ViewModel?.CloseRequested -= ViewModel_CloseRequested;

        base.OnClosed(e);
    }
}
