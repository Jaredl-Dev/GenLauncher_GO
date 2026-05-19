using System;
using Avalonia.Controls;
using GenLauncherGO.UI.Features.Integrity.ViewModels;
using GenLauncherGO.UI.Shared.Controls;
using GenLauncherGO.UI.Shared.Dialogs;

namespace GenLauncherGO.UI.Features.Integrity;

/// <summary>
///     Displays all active-content integrity issues and requests confirmation before applying their resolutions.
/// </summary>
internal partial class IntegrityReviewDialog : Window
{
    public IntegrityReviewDialog()
    {
        ViewModel = null!;
        InitializeComponent();
        LauncherWindowScaling.Attach(this);
    }

    internal IntegrityReviewDialog(IntegrityReviewViewModel viewModel)
        : this()
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;
        ViewModel.CloseRequested += ViewModel_CloseRequested;
    }

    internal IntegrityReviewViewModel ViewModel { get; }

    /// <summary>
    ///     Gets a value indicating whether the user confirmed the offered resolution.
    /// </summary>
    public bool ResolutionConfirmed => ViewModel.ResolutionConfirmed;

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        ViewModel?.CloseRequested -= ViewModel_CloseRequested;

        base.OnClosed(e);
    }

    private void ViewModel_CloseRequested(object? sender, EventArgs e)
    {
        AvaloniaDialog.CloseWithResult(this, ViewModel.ResolutionConfirmed);
    }
}
