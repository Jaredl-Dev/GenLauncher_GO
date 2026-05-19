using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GenLauncherGO.Core.Launching;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Launcher.Support;
using GenLauncherGO.UI.Features.Launcher.ViewModels;
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Shared.Errors;

namespace GenLauncherGO.UI.Features.Launcher.Views;

/// <summary>
/// Displays the main launcher UI for managing modifications and starting the selected game client.
/// </summary>
internal partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = null!;
    private readonly LauncherDragDropController _dragDropController = null!;
    private readonly LauncherWindowListController _contentController = null!;
    private readonly LauncherWindowWorkflowCoordinator _workflowCoordinator = null!;
    private readonly IUiExceptionBoundary _exceptionBoundary = null!;
    private readonly WindowsTaskbarProgress _taskbarProgress = new();
    private readonly HashSet<Task<UiOperationOutcome>> _activeWindowOperations = new();
    private CancellationTokenSource _windowLifetime = new();
    private bool _closePreparationInProgress;
    private bool _closeApproved;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(
        MainWindowViewModel viewModel,
        LauncherDragDropController dragDropController,
        LauncherRuntimeContext runtimeContext,
        LauncherWindowWorkflowCoordinator workflowCoordinator,
        IUiExceptionBoundary exceptionBoundary)
        : this()
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _dragDropController = dragDropController ?? throw new ArgumentNullException(nameof(dragDropController));
        ArgumentNullException.ThrowIfNull(runtimeContext);
        _workflowCoordinator = workflowCoordinator ?? throw new ArgumentNullException(nameof(workflowCoordinator));
        _exceptionBoundary = exceptionBoundary ?? throw new ArgumentNullException(nameof(exceptionBoundary));

        DataContext = _viewModel;
        _contentController = new LauncherWindowListController(
            this,
            _viewModel,
            runtimeContext,
            ModsList,
            PatchesList,
            AddonsList);

        Closing += MainWindow_ClosingAsync;
        Opened += MainWindow_Opened;
        Activated += MainWindow_Activated;
        ModsList.AddHandler(
            InputElement.PointerPressedEvent,
            ModsList_PointerPressed,
            RoutingStrategies.Tunnel);
        ModsList.AddHandler(
            InputElement.PointerMovedEvent,
            ModsList_PointerMoved,
            RoutingStrategies.Tunnel);
        ModsList.AddHandler(
            InputElement.PointerReleasedEvent,
            ModsList_PointerReleased,
            RoutingStrategies.Tunnel);
        PatchesList.AddHandler(
            InputElement.PointerPressedEvent,
            PatchesList_PointerPressed,
            RoutingStrategies.Tunnel);
        PatchesList.AddHandler(
            InputElement.PointerReleasedEvent,
            PatchesList_PointerReleased,
            RoutingStrategies.Tunnel);
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        _viewModel.Initialize();
        _contentController.Initialize();
        UpdateContentViewVisibility();
    }

    private void MainWindow_Opened(object? sender, EventArgs eventArgs)
    {
        _taskbarProgress.Attach(this);
        UpdateTaskbarProgress();
    }

    private void MainWindow_Activated(object? sender, EventArgs eventArgs)
    {
        _viewModel.RefreshGameClientOptions();
        _viewModel.RefreshWorldBuilderOptions();
    }

    private void ModsList_PointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        _dragDropController.CapturePointerGesture(
            ModsList,
            this,
            eventArgs,
            canReorder: true);
    }

    private void ModsList_PointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        _dragDropController.HandlePointerMove(ModsList, eventArgs);
    }

    private void ModsList_PointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        CompleteContentPointerGesture(ModsList, eventArgs);
    }

    private void PatchesList_PointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        _dragDropController.CapturePointerGesture(
            PatchesList,
            this,
            eventArgs,
            canReorder: false);
    }

    private void PatchesList_PointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        CompleteContentPointerGesture(PatchesList, eventArgs);
    }

    private void CompleteContentPointerGesture(
        ListBox contentList,
        PointerReleasedEventArgs eventArgs)
    {
        if (_dragDropController.TryCompletePointerGesture(
                contentList,
                eventArgs,
                out ModificationViewModel? selectionToggleCandidate,
                out int sourceIndex,
                out int targetIndex))
        {
            _viewModel.MoveModInList(sourceIndex, targetIndex);
            eventArgs.Handled = true;
        }

        if (selectionToggleCandidate != null &&
            _viewModel.TryClearContentSelection(selectionToggleCandidate))
        {
            eventArgs.Handled = true;
        }
    }

    private void TileContextMenu_Opened(object? sender, RoutedEventArgs eventArgs)
    {
        _dragDropController?.CancelPointerGesture();
    }

    private async void ModsList_SelectionChangedAsync(object? sender, SelectionChangedEventArgs eventArgs)
    {
        await ExecuteWindowOperationAsync(
            "changing the selected modification",
            () => _contentController.HandleModsListSelectionChangedAsync(eventArgs));
    }

    private async void PatchesList_SelectionChangedAsync(object? sender, SelectionChangedEventArgs eventArgs)
    {
        await ExecuteSyncAsync(
            "changing the selected patch",
            () => _contentController.HandlePatchesListSelectionChanged(eventArgs));
    }

    private async void AddonsList_SelectionChangedAsync(object? sender, SelectionChangedEventArgs eventArgs)
    {
        await ExecuteSyncAsync(
            "changing a selected add-on",
            () => _contentController.HandleAddonsListSelectionChanged(eventArgs));
    }

    private async void VersionsList_SelectionChangedAsync(object? sender, SelectionChangedEventArgs eventArgs)
    {
        await ExecuteSyncAsync(
            "changing a selected content version",
            () => _contentController.HandleVersionsListSelectionChanged(sender!));
    }

    private async void MainWindow_ClosingAsync(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (_closeApproved)
        {
            return;
        }

        // Cancel the first close request so package cancellation and terminal cleanup finish before the
        // Avalonia desktop lifetime disposes application services.
        eventArgs.Cancel = true;
        if (_closePreparationInProgress)
        {
            return;
        }

        if (!await _workflowCoordinator.ConfirmCloseDuringActiveOperationsAsync(this))
        {
            return;
        }

        _closePreparationInProgress = true;
        IsEnabled = false;
        Task<UiOperationOutcome>[] activeWindowOperations = _activeWindowOperations
            .Where(operation => !operation.IsCompleted)
            .ToArray();
        UiOperationOutcome outcome = await _exceptionBoundary.ExecuteAsync(
            "preparing the launcher to close",
            async () =>
            {
                await Task.Yield();
                _windowLifetime.Cancel();
                try
                {
                    await _workflowCoordinator.PrepareForCloseAsync();
                }
                finally
                {
                    await Task.WhenAll(activeWindowOperations);
                }

                _viewModel.SaveLauncherData();
            },
            this);

        if (outcome != UiOperationOutcome.Succeeded)
        {
            _windowLifetime.Dispose();
            _windowLifetime = new CancellationTokenSource();
            _closePreparationInProgress = false;
            IsEnabled = true;
            return;
        }

        _dragDropController.CancelPointerGesture();
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.Dispose();
        _windowLifetime.Dispose();
        _closeApproved = true;
        Close();
    }

    private async void LaunchGame_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        await ExecuteWindowOperationAsync(
            "launching the selected game",
            () => _workflowCoordinator.LaunchAsync(
                GameLaunchTargetKind.GameClient,
                _viewModel,
                _contentController,
                this,
                _windowLifetime.Token));
    }

    private async void LaunchWorldBuilder_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        await ExecuteWindowOperationAsync(
            "launching World Builder",
            () => _workflowCoordinator.LaunchAsync(
                GameLaunchTargetKind.WorldBuilder,
                _viewModel,
                _contentController,
                this,
                _windowLifetime.Token));
    }

    private async void ToggleWindowedMode_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        await ExecuteSyncAsync(
            "changing the windowed-mode preference",
            () => _viewModel.ToggleGameArgument(LauncherGameArgumentService.WindowedArgument));
    }

    private async void ToggleQuickStart_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        await ExecuteSyncAsync(
            "changing the quick-start preference",
            () => _viewModel.ToggleGameArgument(LauncherGameArgumentService.QuickStartArgument));
    }

    private async void OpenOptions_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        await ExecuteWindowOperationAsync(
            "opening launcher settings",
            () => _workflowCoordinator.OpenOptionsAsync(
                _viewModel,
                _contentController,
                this,
                _windowLifetime.Token));
    }

    private void Close_Click(object? sender, RoutedEventArgs eventArgs)
    {
        Close();
    }

    private async void ShowModifications_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        await ShowContentViewAsync(LauncherContentViewKind.Modifications, "showing the modifications view");
    }

    private async void ShowPatches_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        await ShowContentViewAsync(LauncherContentViewKind.Patches, "showing the patches view");
    }

    private async void ShowAddons_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        await ShowContentViewAsync(LauncherContentViewKind.Addons, "showing the add-ons view");
    }

    private async Task ShowContentViewAsync(
        LauncherContentViewKind viewKind,
        string operationContext)
    {
        await ExecuteWindowOperationAsync(
            operationContext,
            () => _viewModel.ShowContentViewAsync(viewKind, _windowLifetime.Token));
    }

    private async void AddRepositoryModification_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        await ExecuteWindowOperationAsync(
            "adding a repository modification",
            () => _workflowCoordinator.AddRepositoryModificationAsync(
                _viewModel,
                _contentController,
                this,
                _windowLifetime.Token));
    }

    private async void ImportManualModification_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        await ImportManualContentAsync(ModificationType.Mod);
    }

    private async void ImportManualPatch_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        await ImportManualContentAsync(ModificationType.Patch);
    }

    private async void ImportManualAddon_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        await ImportManualContentAsync(ModificationType.Addon);
    }

    private async Task ImportManualContentAsync(ModificationType kind)
    {
        await ExecuteWindowOperationAsync(
            "importing manual content",
            () => _workflowCoordinator.ImportManualContentAsync(
                _viewModel,
                _contentController,
                this,
                kind,
                _windowLifetime.Token));
    }

    private async void UpdateModification_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        if (GetModification(sender) is not { } modification)
        {
            return;
        }

        await ExecuteWindowOperationAsync(
            "updating launcher content",
            () => _workflowCoordinator.UpdateModificationAsync(
                _viewModel,
                _contentController,
                this,
                modification));
    }

    private async void ChangeVersionImage_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        if (GetModification(sender) is not { } modification)
        {
            return;
        }

        await ExecuteWindowOperationAsync(
            "changing a modification image",
            () => _workflowCoordinator.ChangeVersionImageAsync(
                _viewModel,
                this,
                modification,
                _windowLifetime.Token));
    }

    private async void OpenChangeLog_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        await ApplyModificationActionAsync(sender, "opening a change log", _workflowCoordinator.OpenChangeLog);
    }

    private async void OpenNetworkInfo_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        await ApplyModificationActionAsync(
            sender,
            "opening network information",
            _workflowCoordinator.OpenNetworkInfo);
    }

    private async void OpenSupport_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        await ApplyModificationActionAsync(sender, "opening a support link", _workflowCoordinator.OpenSupport);
    }

    private async void OpenModDb_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        await ApplyModificationActionAsync(sender, "opening a Mod DB link", _workflowCoordinator.OpenModDb);
    }

    private async void OpenDiscord_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        await ApplyModificationActionAsync(sender, "opening a Discord link", _workflowCoordinator.OpenDiscord);
    }

    private async void DeleteVersion_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Control { DataContext: ModificationVersionSelection versionSelection })
        {
            await ExecuteWindowOperationAsync(
                "deleting a content version",
                () => _workflowCoordinator.DeleteVersionAsync(
                    _viewModel,
                    _contentController,
                    this,
                    versionSelection));
        }
    }

    private async void DeleteModification_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        if (GetModification(sender) is not { } modification)
        {
            return;
        }

        await ExecuteWindowOperationAsync(
            "deleting launcher content",
            () => _workflowCoordinator.DeleteModificationAsync(
                _viewModel,
                _contentController,
                this,
                modification));
    }

    private async void ForceQuitRunningProcess_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        await ExecuteWindowOperationAsync(
            "force closing the launched process",
            () => _workflowCoordinator.ForceCloseRunningProcessAsync(this));
    }

    private async Task ApplyModificationActionAsync(
        object? sender,
        string operationContext,
        Action<ModificationViewModel> action)
    {
        ModificationViewModel? modification = GetModification(sender);
        if (modification != null)
        {
            await ExecuteSyncAsync(operationContext, () => action(modification));
        }
    }

    private static ModificationViewModel? GetModification(object? sender)
    {
        return (sender as Control)?.DataContext as ModificationViewModel;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(MainWindowViewModel.TaskbarProgressState) or
            nameof(MainWindowViewModel.TaskbarProgressValue))
        {
            UpdateTaskbarProgress();
            return;
        }

        if (eventArgs.PropertyName == nameof(MainWindowViewModel.ActiveContentView))
        {
            UpdateContentViewVisibility();
            return;
        }

        if (eventArgs.PropertyName != nameof(MainWindowViewModel.ShouldHideLauncherWindow))
        {
            return;
        }

        if (_viewModel.ShouldHideLauncherWindow)
        {
            Hide();
        }
        else if (!_closePreparationInProgress)
        {
            Show();
        }
    }

    private void UpdateTaskbarProgress()
    {
        _taskbarProgress.Update(
            _viewModel.TaskbarProgressState,
            _viewModel.TaskbarProgressValue);
    }

    private void UpdateContentViewVisibility()
    {
        bool modificationsVisible =
            _viewModel.ActiveContentView == LauncherContentViewKind.Modifications;
        bool patchesVisible =
            _viewModel.ActiveContentView == LauncherContentViewKind.Patches;
        bool addonsVisible =
            _viewModel.ActiveContentView == LauncherContentViewKind.Addons;

        ModsList.IsVisible = modificationsVisible;
        PatchesList.IsVisible = patchesVisible;
        AddonsList.IsVisible = addonsVisible;
        ManualAddMod.IsVisible = modificationsVisible;
        ManualAddPatch.IsVisible = patchesVisible;
        ManualAddAddon.IsVisible = addonsVisible;
    }

    private async Task ExecuteWindowOperationAsync(
        string operationContext,
        Func<Task> operation)
    {
        if (_closePreparationInProgress)
        {
            return;
        }

        Task<UiOperationOutcome> operationTask = _exceptionBoundary.ExecuteAsync(
            operationContext,
            operation,
            this);
        _activeWindowOperations.Add(operationTask);
        try
        {
            await operationTask;
        }
        finally
        {
            _activeWindowOperations.Remove(operationTask);
        }
    }

    private async Task ExecuteSyncAsync(string operationContext, Action action)
    {
        await _exceptionBoundary.ExecuteAsync(
            operationContext,
            () =>
            {
                action();
                return Task.CompletedTask;
            },
            this);
    }
}
