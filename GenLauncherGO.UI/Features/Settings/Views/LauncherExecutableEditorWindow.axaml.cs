using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Launcher.Contracts;
using GenLauncherGO.UI.Features.Settings.ViewModels;
using GenLauncherGO.UI.Shared.Controls;
using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.UI.Features.Settings.Views;

/// <summary>
///     Collects a custom executable's selector name and root-level file.
/// </summary>
internal partial class LauncherExecutableEditorWindow : Window
{
    private readonly ILauncherDialogService _dialogService = null!;
    private readonly ILauncherFilePicker _filePicker = null!;
    private readonly ILauncherStringLocalizer _stringLocalizer = null!;
    private readonly LauncherExecutableManagementViewModel _viewModel = null!;

    public LauncherExecutableEditorWindow()
    {
        InitializeComponent();
        LauncherWindowScaling.Attach(this);
    }

    public LauncherExecutableEditorWindow(
        LauncherExecutableManagementViewModel viewModel,
        ILauncherFilePicker filePicker,
        ILauncherDialogService dialogService,
        ILauncherStringLocalizer stringLocalizer)
        : this()
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));

        DataContext = _viewModel;
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
                LauncherInfoDialogRequest.CreateSettingsSaveFailure(_stringLocalizer),
                this);
        }
    }
}
