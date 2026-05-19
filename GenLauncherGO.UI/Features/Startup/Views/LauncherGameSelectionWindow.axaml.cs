using System;
using Avalonia.Controls;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Startup.Contracts;
using GenLauncherGO.UI.Features.Startup.ViewModels;
using GenLauncherGO.UI.Shared.Localization;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.UI.Features.Startup.Views;

/// <summary>
/// Displays the official game covers and persists an explicit initial active-game choice.
/// </summary>
internal partial class LauncherGameSelectionWindow : Window
{
    private readonly LauncherGameSelectionViewModel _viewModel = null!;
    private readonly ILauncherStringLocalizer _stringLocalizer = null!;
    private readonly IStartupDialogService _startupDialogService = null!;

    public LauncherGameSelectionWindow()
    {
        InitializeComponent();
    }

    public LauncherGameSelectionWindow(
        LauncherGameSelectionViewModel viewModel,
        ILauncherStringLocalizer stringLocalizer,
        IStartupDialogService startupDialogService)
        : this()
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _startupDialogService = startupDialogService ?? throw new ArgumentNullException(nameof(startupDialogService));

        DataContext = _viewModel;
        ApplyTheme(_viewModel.SelectedGame);
        _viewModel.SelectionChanged += ViewModel_SelectionChanged;
        _viewModel.Completed += ViewModel_Completed;
        _viewModel.CancelRequested += ViewModel_CancelRequested;
        _viewModel.SaveFailed += ViewModel_SaveFailedAsync;
    }

    public bool Accepted { get; private set; }

    private void ViewModel_SelectionChanged(object? sender, EventArgs e)
    {
        ApplyTheme(_viewModel.SelectedGame);
    }

    private void ApplyTheme(SupportedGame game)
    {
        LauncherThemeResourceApplier.Apply(this, LauncherThemePresets.Create(game));
        // Selection dialogs use each game's border accent instead of the green content accent.
        Resources["GenLauncherActiveColor"] = Resources["GenLauncherBorderColor"];
    }

    private void ViewModel_Completed(object? sender, EventArgs e)
    {
        Accepted = true;
        Close(true);
    }

    private void ViewModel_CancelRequested(object? sender, EventArgs e)
    {
        Accepted = false;
        Close(false);
    }

    private async void ViewModel_SaveFailedAsync(object? sender, EventArgs e)
    {
        await _startupDialogService.ShowThemedMessageAsync(
            _stringLocalizer["PreferencesSaveFailedTitle"],
            _stringLocalizer["PreferencesSaveFailedDetails"],
            LauncherThemePresets.Create(_viewModel.SelectedGame));
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.SelectionChanged -= ViewModel_SelectionChanged;
            _viewModel.Completed -= ViewModel_Completed;
            _viewModel.CancelRequested -= ViewModel_CancelRequested;
            _viewModel.SaveFailed -= ViewModel_SaveFailedAsync;
        }

        base.OnClosed(e);
    }
}
