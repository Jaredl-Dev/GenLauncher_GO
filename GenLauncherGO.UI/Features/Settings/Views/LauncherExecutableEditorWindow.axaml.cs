using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Launcher.Contracts;
using GenLauncherGO.UI.Features.Settings.ViewModels;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Shared.Localization;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.UI.Features.Settings.Views;

/// <summary>
/// Collects a custom executable's selector name and root-level file.
/// </summary>
internal partial class LauncherExecutableEditorWindow : Window
{
    private readonly LauncherExecutableManagementViewModel _viewModel = null!;
    private readonly ILauncherFilePicker _filePicker = null!;
    private readonly ILauncherDialogService _dialogService = null!;
    private readonly ILauncherStringLocalizer _stringLocalizer = null!;

    public LauncherExecutableEditorWindow()
    {
        InitializeComponent();
    }

    public LauncherExecutableEditorWindow(
        LauncherExecutableManagementViewModel viewModel,
        ILauncherFilePicker filePicker,
        ILauncherDialogService dialogService,
        ILauncherStringLocalizer stringLocalizer,
        LauncherRuntimeContext runtimeContext)
        : this()
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        ArgumentNullException.ThrowIfNull(runtimeContext);

        DataContext = _viewModel;
        LauncherThemeResourceApplier.Apply(this, runtimeContext.Colors, includeBackgroundImage: false);
    }

    private async void Browse_ClickAsync(object? sender, RoutedEventArgs e)
    {
        string? selectedPath = await _filePicker.PickGameExecutableFileAsync(
            this,
            _viewModel.GameDirectory);
        if (selectedPath != null)
        {
            _viewModel.SetSelectedExecutablePath(selectedPath);
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async void Save_ClickAsync(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.TrySaveEditor())
        {
            Close(true);
            return;
        }

        if (_viewModel.LastPersistenceSaveFailed)
        {
            await _dialogService.ShowErrorAsync(
                new LauncherInfoDialogRequest(
                    _stringLocalizer["SettingsSaveFailedTitle"],
                    _stringLocalizer["SettingsSaveFailedDetails"]),
                this);
        }
    }
}
