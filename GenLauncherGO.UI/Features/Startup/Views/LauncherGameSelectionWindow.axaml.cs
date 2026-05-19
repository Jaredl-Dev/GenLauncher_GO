using System;
using Avalonia;
using Avalonia.Controls;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Startup.Contracts;
using GenLauncherGO.UI.Features.Startup.ViewModels;
using GenLauncherGO.UI.Shared.Controls;
using GenLauncherGO.UI.Shared.Localization;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.UI.Features.Startup.Views;

/// <summary>
///     Displays the official game covers and persists an explicit initial active-game choice.
/// </summary>
internal partial class LauncherGameSelectionWindow : Window
{
    private readonly IStartupDialogService _startupDialogService = null!;
    private readonly ILauncherStringLocalizer _stringLocalizer = null!;
    private readonly LauncherGameSelectionViewModel _viewModel = null!;

    public LauncherGameSelectionWindow()
    {
        InitializeComponent();
        LauncherWindowScaling.Attach(this);
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

    /// <summary>
    ///     Publishes the highlighted game's theme so this window and any dialog it opens preview it together.
    /// </summary>
    /// <remarks>
    ///     Writing to application scope is safe here because no game is active yet; the launcher adopts whichever
    ///     game this window settles on.
    /// </remarks>
    private static void ApplyTheme(SupportedGame game)
    {
        if (Application.Current is { } application)
        {
            LauncherThemeResourceApplier.Apply(application.Resources, LauncherThemePresets.Create(game));
        }
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
        var request =
            LauncherInfoDialogRequest.CreateSettingsSaveFailure(_stringLocalizer);
        await _startupDialogService.ShowMessageAsync(
            request.MainMessage,
            request.DetailMessage);
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
