using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Startup.Contracts;
using GenLauncherGO.UI.Features.Startup.ViewModels;
using GenLauncherGO.UI.Shared.Controls;
using GenLauncherGO.UI.Shared.Localization;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.UI.Features.Startup.Views;

/// <summary>
/// Collects at least one valid game installation before normal launcher startup.
/// </summary>
internal partial class LauncherSetupWindow : Window
{
    private readonly LauncherSetupViewModel _viewModel = null!;
    private readonly LauncherInstallationsViewModel _installations = null!;
    private readonly ILauncherStringLocalizer _stringLocalizer = null!;
    private readonly ColorsInfo _colors = null!;
    private readonly IStartupDialogService _startupDialogService = null!;

    public LauncherSetupWindow()
    {
        InitializeComponent();
    }

    public LauncherSetupWindow(
        LauncherSetupViewModel viewModel,
        ILauncherStringLocalizer stringLocalizer,
        ColorsInfo colors,
        IStartupDialogService startupDialogService,
        bool useSettingsActions = false)
        : this()
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _installations = _viewModel.Installations;
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _colors = colors ?? throw new ArgumentNullException(nameof(colors));
        _startupDialogService = startupDialogService ?? throw new ArgumentNullException(nameof(startupDialogService));

        DataContext = _viewModel;
        if (useSettingsActions)
        {
            CancelActionButton.Content = _stringLocalizer["Cancel"];
            PrimaryActionButton.Content = _stringLocalizer["Save"];
        }

        LauncherThemeResourceApplier.Apply(this, _colors, includeBackgroundImage: false);
        // Setup uses the inherited theme's border accent consistently instead of its content accent.
        Resources["GenLauncherActiveColor"] = Resources["GenLauncherBorderColor"];
        _viewModel.Completed += ViewModel_Completed;
        _viewModel.CancelRequested += ViewModel_CancelRequested;
        _viewModel.SaveFailed += ViewModel_SaveFailedAsync;
        _installations.RegistryDetectionFailed += Installations_RegistryDetectionFailedAsync;
        _installations.RegistryDetectionSucceeded += Installations_RegistryDetectionSucceeded;
    }

    public bool Accepted { get; private set; }

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
            _colors);
    }

    private async void Installations_RegistryDetectionFailedAsync(SupportedGame game)
    {
        await _startupDialogService.ShowThemedMessageAsync(
            _stringLocalizer["RegistryDetectionFailedTitle"],
            _stringLocalizer["RegistryInstallationNotFound"],
            _colors);
    }

    private void Installations_RegistryDetectionSucceeded(SupportedGame game)
    {
        TextBox pathTextBox = game == SupportedGame.Generals
            ? GeneralsPathTextBox
            : ZeroHourPathTextBox;
        LauncherTextBoxFeedback.Flash(
            pathTextBox,
            Resources["GenLauncherBorderColor"] as IBrush);
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
        if (sender is not TextBox textBox || textBox.IsKeyboardFocusWithin)
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
