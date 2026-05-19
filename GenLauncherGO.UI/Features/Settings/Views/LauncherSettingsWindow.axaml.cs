using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Launcher.Contracts;
using GenLauncherGO.UI.Features.Settings.Models;
using GenLauncherGO.UI.Features.Settings.ViewModels;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Features.Startup.Contracts;
using GenLauncherGO.UI.Features.Startup.ViewModels;
using GenLauncherGO.UI.Features.Startup.Views;
using GenLauncherGO.UI.Shared.Localization;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.UI.Features.Settings.Views;

internal partial class LauncherSettingsWindow : Window
{
    private readonly LauncherSettingsViewModel _viewModel = null!;

    private readonly ILauncherStringLocalizer _stringLocalizer = null!;

    private readonly ILauncherPreferencesService _preferencesService = null!;

    private readonly LauncherRuntimeContext _runtimeContext = null!;
    private readonly IStartupDialogService _startupDialogService = null!;

    private readonly LauncherExecutableManagementViewModel _executableManagementViewModel = null!;

    private readonly ILauncherFilePicker _filePicker = null!;

    private readonly ILauncherDialogService _dialogService = null!;

    private Func<LauncherInstallations, SupportedGame?, Task<bool>>? _applyInstallationChangesAsync;

    private Func<SupportedGame, Task<bool>>? _switchGameAsync;

    private bool _gameManagementBusy;

    public LauncherSettingsWindow()
    {
        InitializeComponent();
    }

    public LauncherSettingsWindow(
        LauncherSettingsViewModel viewModel,
        ILauncherStringLocalizer stringLocalizer,
        ILauncherPreferencesService preferencesService,
        LauncherRuntimeContext runtimeContext,
        IStartupDialogService startupDialogService,
        LauncherExecutableManagementViewModel executableManagementViewModel,
        ILauncherFilePicker filePicker,
        ILauncherDialogService dialogService)
        : this()
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        _runtimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
        _startupDialogService = startupDialogService ?? throw new ArgumentNullException(nameof(startupDialogService));
        _executableManagementViewModel = executableManagementViewModel ??
                                         throw new ArgumentNullException(nameof(executableManagementViewModel));
        _filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        DataContext = _viewModel;
        _viewModel.CloseRequested += ViewModel_CloseRequested;
        _viewModel.LanguagePreferenceChanged += ViewModel_LanguagePreferenceChangedAsync;
        _viewModel.PreferencesSaveFailed += ViewModel_PreferencesSaveFailedAsync;
        GameManagementSection.IsEnabled = false;
    }

    /// <summary>
    /// Gets a value indicating whether the user requested an immediate launcher restart.
    /// </summary>
    public bool RestartRequested { get; private set; }

    /// <summary>
    /// Configures the live game-management operations owned by the main-window workflow.
    /// </summary>
    public void ConfigureGameManagement(
        Func<LauncherInstallations, SupportedGame?, Task<bool>> applyInstallationChangesAsync,
        Func<SupportedGame, Task<bool>> switchGameAsync)
    {
        _applyInstallationChangesAsync = applyInstallationChangesAsync ??
                                         throw new ArgumentNullException(nameof(applyInstallationChangesAsync));
        _switchGameAsync = switchGameAsync ?? throw new ArgumentNullException(nameof(switchGameAsync));
        GameManagementSection.IsEnabled = true;
    }

    /// <inheritdoc />
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // Settings use the active theme's border accent consistently instead of the green content accent.
        Resources["GenLauncherActiveColor"] = Resources["GenLauncherBorderColor"];
    }

    private void ViewModel_CloseRequested(object? sender, EventArgs e)
    {
        Close(true);
    }

    /// <summary>
    /// Prompts for an immediate or deferred restart after the startup-only language preference changes.
    /// </summary>
    private async void ViewModel_LanguagePreferenceChangedAsync(object? sender, EventArgs e)
    {
        if (!await _dialogService.ShowWarningConfirmationAsync(
                new LauncherInfoDialogRequest(
                    _stringLocalizer["LanguageRestartPromptTitle"],
                    _stringLocalizer["LanguageRestartPromptDetails"],
                    cancelText: _stringLocalizer["RestartLater"]),
                _stringLocalizer["RestartNow"],
                this))
        {
            return;
        }

        RestartRequested = true;
        Close(true);
    }

    private async void ViewModel_PreferencesSaveFailedAsync(object? sender, EventArgs e)
    {
        await _dialogService.ShowErrorAsync(
            new LauncherInfoDialogRequest(
                _stringLocalizer["SettingsSaveFailedTitle"],
                _stringLocalizer["SettingsSaveFailedDetails"]),
            this);
    }

    private async void ManageInstallations_ClickAsync(object sender, RoutedEventArgs e)
    {
        LauncherInstallations previousInstallations = _preferencesService.Current.Installations;
        SupportedGame? previousSelectedGame = _preferencesService.Current.LastSelectedGame;
        LauncherSetupWindow setupWindow = new(
            new LauncherSetupViewModel(
                _preferencesService,
                _viewModel.Installations,
                detectInstallationsOnOpen: false),
            _stringLocalizer,
            _runtimeContext.Colors,
            _startupDialogService,
            useSettingsActions: true)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        if (!await setupWindow.ShowDialog<bool>(this))
        {
            RestorePersistedInstallationDrafts();
            return;
        }

        Func<LauncherInstallations, SupportedGame?, Task<bool>> applyChanges =
            _applyInstallationChangesAsync ??
            throw new InvalidOperationException("Settings game management has not been configured.");
        await RunGameManagementOperationAsync(
            () => applyChanges(previousInstallations, previousSelectedGame));
        _viewModel.RefreshAfterGameManagementChange();
        RestorePersistedInstallationDrafts();
    }

    private async void SwitchGame_ClickAsync(object sender, RoutedEventArgs e)
    {
        Func<SupportedGame, Task<bool>> switchGame = _switchGameAsync ??
                                                     throw new InvalidOperationException(
                                                         "Settings game management has not been configured.");
        await RunGameManagementOperationAsync(() => switchGame(_viewModel.SwitchTargetGame));
        _viewModel.RefreshAfterGameManagementChange();
        RestorePersistedInstallationDrafts();
    }

    private async void ManageGameClients_ClickAsync(object? sender, RoutedEventArgs e)
    {
        await OpenExecutableManagerAsync(LauncherExecutableManagementKind.GameClient);
    }

    private async void ManageWorldBuilders_ClickAsync(object? sender, RoutedEventArgs e)
    {
        await OpenExecutableManagerAsync(LauncherExecutableManagementKind.WorldBuilder);
    }

    private async Task OpenExecutableManagerAsync(LauncherExecutableManagementKind kind)
    {
        var managerWindow = new LauncherExecutableManagementWindow(
            _executableManagementViewModel,
            kind,
            _filePicker,
            _dialogService,
            _stringLocalizer,
            _runtimeContext);
        await managerWindow.ShowDialog<bool>(this);
    }

    private async Task RunGameManagementOperationAsync(Func<Task<bool>> operation)
    {
        _gameManagementBusy = true;
        GameManagementSection.IsEnabled = false;
        try
        {
            bool succeeded = await operation();
            if (succeeded)
            {
                // The game-session switch updates the built-in theme while this window stays open.
                LauncherThemeResourceApplier.Apply(this, _runtimeContext.Colors);
                Resources["GenLauncherActiveColor"] = Resources["GenLauncherBorderColor"];
            }

        }
        finally
        {
            _gameManagementBusy = false;
            GameManagementSection.IsEnabled = true;
        }
    }

    private void RestorePersistedInstallationDrafts()
    {
        LauncherInstallations persisted = _preferencesService.Current.Installations;
        _viewModel.Installations.GeneralsPath = persisted.Generals ?? string.Empty;
        _viewModel.Installations.ZeroHourPath = persisted.ZeroHour ?? string.Empty;
    }

    /// <inheritdoc />
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_gameManagementBusy)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.CloseRequested -= ViewModel_CloseRequested;
            _viewModel.LanguagePreferenceChanged -= ViewModel_LanguagePreferenceChangedAsync;
            _viewModel.PreferencesSaveFailed -= ViewModel_PreferencesSaveFailedAsync;
        }

        base.OnClosed(e);
    }
}
