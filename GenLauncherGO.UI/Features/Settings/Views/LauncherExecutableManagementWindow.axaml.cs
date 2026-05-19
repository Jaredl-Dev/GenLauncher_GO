using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Launcher.Contracts;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Settings.ViewModels;
using GenLauncherGO.UI.Shared.Controls;
using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.UI.Features.Settings.Views;

internal partial class LauncherExecutableManagementWindow : Window
{
    private readonly ILauncherDialogService _dialogService = null!;
    private readonly ILauncherFilePicker _filePicker = null!;
    private readonly ILauncherStringLocalizer _stringLocalizer = null!;
    private readonly LauncherExecutableManagementViewModel _viewModel = null!;

    public LauncherExecutableManagementWindow()
    {
        InitializeComponent();
        LauncherWindowScaling.Attach(this);
    }

    public LauncherExecutableManagementWindow(
        LauncherExecutableManagementViewModel viewModel,
        GameLaunchTargetKind kind,
        ILauncherFilePicker filePicker,
        ILauncherDialogService dialogService,
        ILauncherStringLocalizer stringLocalizer)
        : this()
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));

        _viewModel.Initialize(kind);
        DataContext = _viewModel;
        _viewModel.CloseRequested += ViewModel_CloseRequested;
        _viewModel.RemoveRequested += ViewModel_RemoveRequestedAsync;
        Closed += Window_Closed;
    }

    private async void Add_ClickAsync(object? sender, RoutedEventArgs e)
    {
        _viewModel.BeginAdd();
        await OpenEditorAsync();
    }

    private async void Edit_ClickAsync(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ExecutableOption option })
        {
            _viewModel.BeginEdit(option);
            await OpenEditorAsync();
        }
    }

    private async Task OpenEditorAsync()
    {
        var editorWindow = new LauncherExecutableEditorWindow(
            _viewModel,
            _filePicker,
            _dialogService,
            _stringLocalizer);
        await editorWindow.ShowDialog<bool>(this);
    }

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ExecutableOption option })
        {
            _viewModel.RequestRemoveCommand.Execute(option);
        }
    }

    private void ViewModel_CloseRequested(object? sender, EventArgs e)
    {
        Close(true);
    }

    private async void ViewModel_RemoveRequestedAsync(ExecutableOption option)
    {
        bool confirmed = await _dialogService.ShowWarningConfirmationAsync(
            new LauncherInfoDialogRequest(
                _stringLocalizer["RemoveCustomExecutable"],
                string.Format(CultureInfo.CurrentCulture,
                    _stringLocalizer["RemoveCustomExecutableDetails"],
                    option.DisplayName)),
            _stringLocalizer["Remove"],
            this);
        if (confirmed)
        {
            bool removed = _viewModel.Remove(option);
            if (!removed && _viewModel.LastPersistenceSaveFailed)
            {
                await _dialogService.ShowErrorAsync(
                    LauncherInfoDialogRequest.CreateSettingsSaveFailure(_stringLocalizer),
                    this);
            }
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _viewModel.CloseRequested -= ViewModel_CloseRequested;
        _viewModel.RemoveRequested -= ViewModel_RemoveRequestedAsync;
        Closed -= Window_Closed;
    }
}
