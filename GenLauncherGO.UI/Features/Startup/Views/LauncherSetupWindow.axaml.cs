using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Startup.Contracts;
using GenLauncherGO.UI.Features.Startup.ViewModels;
using GenLauncherGO.UI.Shared.Controls;
using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.UI.Features.Startup.Views;

/// <summary>
///     Collects at least one valid game installation before normal launcher startup.
/// </summary>
internal partial class LauncherSetupWindow : Window
{
    private readonly LauncherInstallationsViewModel _installations = null!;
    private readonly IStartupDialogService _startupDialogService = null!;
    private readonly ILauncherStringLocalizer _stringLocalizer = null!;
    private readonly LauncherSetupViewModel _viewModel = null!;

    public LauncherSetupWindow()
    {
        InitializeComponent();
        LauncherWindowScaling.Attach(this);
    }

    public LauncherSetupWindow(
        LauncherSetupViewModel viewModel,
        ILauncherStringLocalizer stringLocalizer,
        IStartupDialogService startupDialogService,
        bool useSettingsActions = false)
        : this()
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _installations = _viewModel.Installations;
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _startupDialogService = startupDialogService ?? throw new ArgumentNullException(nameof(startupDialogService));
        CanMoveWindow = !useSettingsActions;

        DataContext = _viewModel;
        if (useSettingsActions)
        {
            CancelActionButton.Content = _stringLocalizer["Cancel"];
            PrimaryActionButton.Content = _stringLocalizer["Save"];
        }

        _viewModel.Completed += ViewModel_Completed;
        _viewModel.CancelRequested += ViewModel_CancelRequested;
        _viewModel.SaveFailed += ViewModel_SaveFailedAsync;
        _installations.RegistryDetectionFailed += Installations_RegistryDetectionFailedAsync;
        _installations.RegistryDetectionSucceeded += Installations_RegistryDetectionSucceeded;
    }

    public bool Accepted { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether this window may be dragged by its title band.
    /// </summary>
    /// <remarks>
    ///     Reused from settings, the window is a modal panel inside an already-placed dialog, so moving it there
    ///     would break the owner's placement.
    /// </remarks>
    public bool CanMoveWindow { get; }

    private void ViewModel_Completed(object? sender, EventArgs e)
    {
        CloseWithResult(true);
    }

    private void ViewModel_CancelRequested(object? sender, EventArgs e)
    {
        CloseWithResult(false);
    }

    private void CloseWithResult(bool accepted)
    {
        Accepted = accepted;
        Close(accepted);
    }

    private async void ViewModel_SaveFailedAsync(object? sender, EventArgs e)
    {
        var request =
            LauncherInfoDialogRequest.CreateSettingsSaveFailure(_stringLocalizer);
        await _startupDialogService.ShowMessageAsync(
            request.MainMessage,
            request.DetailMessage);
    }

    private async void Installations_RegistryDetectionFailedAsync(SupportedGame game)
    {
        await _startupDialogService.ShowMessageAsync(
            _stringLocalizer["RegistryDetectionFailedTitle"],
            _stringLocalizer["RegistryInstallationNotFound"]);
    }

    private void Installations_RegistryDetectionSucceeded(SupportedGame game)
    {
        if (FindPathTextBox(game) is { } pathTextBox)
        {
            LauncherTextBoxFeedback.Flash(
                pathTextBox,
                this.FindResource("GenLauncherBorderColor") as IBrush);
        }
    }

    /// <summary>
    ///     Finds the path field for one game among the templated installation rows.
    /// </summary>
    /// <remarks>
    ///     The rows come from one template, so the fields carry no per-game names; the row's own view model identifies
    ///     which game each field edits.
    /// </remarks>
    private TextBox? FindPathTextBox(SupportedGame game)
    {
        return this.GetVisualDescendants()
            .OfType<TextBox>()
            .FirstOrDefault(textBox =>
                textBox.DataContext is LauncherGameInstallationViewModel row && row.Game == game);
    }

    private void PathTextBox_ToolTipOpening(object? sender, CancelRoutedEventArgs eventArgs)
    {
        if (sender is not TextBox textBox ||
            string.IsNullOrWhiteSpace(textBox.Text) ||
            GetTextScrollViewer(textBox) is not { } contentHost ||
            contentHost.Extent.Width <= contentHost.Viewport.Width)
        {
            eventArgs.Cancel = true;
        }
    }

    private void PathTextBox_TextChanged(object? sender, TextChangedEventArgs eventArgs)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        if (textBox.IsKeyboardFocusWithin)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (!textBox.IsKeyboardFocusWithin &&
                    GetTextScrollViewer(textBox) is { } contentHost)
                {
                    contentHost.Offset = new Vector(0, contentHost.Offset.Y);
                }
            },
            DispatcherPriority.Loaded);
    }

    private static ScrollViewer? GetTextScrollViewer(TextBox textBox)
    {
        textBox.ApplyTemplate();
        return textBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.Completed -= ViewModel_Completed;
            _viewModel.CancelRequested -= ViewModel_CancelRequested;
            _viewModel.SaveFailed -= ViewModel_SaveFailedAsync;
            _installations.RegistryDetectionFailed -= Installations_RegistryDetectionFailedAsync;
            _installations.RegistryDetectionSucceeded -= Installations_RegistryDetectionSucceeded;
        }

        base.OnClosed(e);
    }
}
