using System;
using Avalonia.Controls;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Mods.ViewModels;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.UI.Features.Mods.Views;

/// <summary>
/// Displays a themed launcher confirmation or information dialog.
/// </summary>
internal partial class InfoWindow : Window
{
    private readonly ColorsInfo _colors = null!;

    public InfoWindow()
    {
        ViewModel = null!;
        InitializeComponent();
    }

    internal InfoWindow(
        LauncherInfoDialogRequest request,
        InfoDialogKind kind,
        ColorsInfo colors,
        string? continueText = null,
        string? cancelText = null)
        : this()
    {
        ArgumentNullException.ThrowIfNull(request);

        ViewModel = new InfoDialogViewModel(request, kind, continueText, cancelText);
        _colors = colors ?? throw new ArgumentNullException(nameof(colors));
        DataContext = ViewModel;
        ViewModel.CloseRequested += ViewModel_CloseRequested;
        ApplyColors();
    }

    internal InfoDialogViewModel ViewModel { get; }

    internal bool Accepted => ViewModel?.DialogResult == true;

    private void ViewModel_CloseRequested(object? sender, EventArgs e)
    {
        if (IsDialog && ViewModel.DialogResult.HasValue)
        {
            Close(ViewModel.DialogResult.Value);
            return;
        }

        Close();
    }

    private void ApplyColors()
    {
        LauncherThemeResourceApplier.Apply(this, _colors, includeBackgroundImage: false);
        Resources["GenLauncherActiveColor"] = Resources["GenLauncherBorderColor"];
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.CloseRequested -= ViewModel_CloseRequested;
        }

        base.OnClosed(e);
    }
}
