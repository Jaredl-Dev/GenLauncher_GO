using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
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
using GenLauncherGO.UI.Shared.Controls;
using GenLauncherGO.UI.Shared.Errors;

namespace GenLauncherGO.UI.Features.Launcher.Views;

/// <summary>
///     Displays the main launcher UI for managing modifications and starting the selected game client.
/// </summary>
internal partial class MainWindow : Window
{
    private readonly HashSet<Task<UiOperationOutcome>> _activeWindowOperations = [];
    private readonly LauncherWindowListController _contentController = null!;
    private readonly LauncherDragDropController _dragDropController = null!;
    private readonly IUiExceptionBoundary _exceptionBoundary = null!;
    private readonly LauncherContentActionCoordinator _contentActionCoordinator = null!;
    private readonly MainWindowViewModel _viewModel = null!;
    private readonly LauncherWindowContext _windowContext = null!;
    private readonly LauncherWindowWorkflowCoordinator _workflowCoordinator = null!;
    private bool _closeApproved;
    private bool _closePreparationInProgress;
    private CancellationTokenSource _windowLifetime = new();

    public MainWindow()
    {
        InitializeComponent();
        LauncherWindowScaling.Attach(this);
    }

    public MainWindow(
        MainWindowViewModel viewModel,
        LauncherDragDropController dragDropController,
        LauncherRuntimeContext runtimeContext,
        LauncherWindowWorkflowCoordinator workflowCoordinator,
        LauncherContentActionCoordinator contentActionCoordinator,
        IUiExceptionBoundary exceptionBoundary)
        : this()
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _dragDropController = dragDropController ?? throw new ArgumentNullException(nameof(dragDropController));
        ArgumentNullException.ThrowIfNull(runtimeContext);
        _workflowCoordinator = workflowCoordinator ?? throw new ArgumentNullException(nameof(workflowCoordinator));
        _contentActionCoordinator = contentActionCoordinator ??
                                    throw new ArgumentNullException(nameof(contentActionCoordinator));
        _exceptionBoundary = exceptionBoundary ?? throw new ArgumentNullException(nameof(exceptionBoundary));

        DataContext = _viewModel;
        _contentController = new LauncherWindowListController(
            _viewModel,
            runtimeContext,
            ModsList,
            PatchesList,
            AddonsList);
        _windowContext = new LauncherWindowContext(_viewModel, _contentController, this);
        Closing += MainWindow_ClosingAsync;
        Opened += MainWindow_OpenedAsync;
        Activated += MainWindow_Activated;
        Deactivated += MainWindow_Deactivated;
        ModsList.AddHandler(
            PointerPressedEvent,
            ModsList_PointerPressed,
            RoutingStrategies.Tunnel);
        ModsList.AddHandler(
            PointerMovedEvent,
            ModsList_PointerMoved,
            RoutingStrategies.Tunnel);
        ModsList.AddHandler(
            PointerReleasedEvent,
            ModsList_PointerReleased,
            RoutingStrategies.Tunnel);
        ModsList.PointerCaptureLost += ModsList_PointerCaptureLost;
        PatchesList.AddHandler(
            PointerPressedEvent,
            PatchesList_PointerPressed,
            RoutingStrategies.Tunnel);
        PatchesList.AddHandler(
            PointerReleasedEvent,
            PatchesList_PointerReleased,
            RoutingStrategies.Tunnel);
        _viewModel.PropertyChanged += ViewModel_PropertyChangedAsync;

        _viewModel.Initialize();
        _contentController.Initialize();
        UpdateContentViewVisibility();
    }

    private async void MainWindow_OpenedAsync(object? sender, EventArgs eventArgs)
    {
        LauncherDragDropController.RestoreVerticalScrollOffset(
            ModsList,
            _viewModel.ModsListVerticalOffset);
        await ShowRetailClientRecommendationIfNeededAsync();
    }

    private void MainWindow_Activated(object? sender, EventArgs eventArgs)
    {
        _viewModel.RefreshGameClientOptions();
        _viewModel.RefreshWorldBuilderOptions();
    }

    private void MainWindow_Deactivated(object? sender, EventArgs eventArgs)
    {
        CancelContentPointerGesture();
    }

    private void ModsList_PointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        _dragDropController.CapturePointerGesture(
            ModsList,
            this,
            eventArgs,
            true);
    }

    private void ModsList_PointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        if (_dragDropController.HandlePointerMove(ModsList, eventArgs))
        {
            UpdateDragPreview(eventArgs.GetPosition(DragLayer));
        }
    }

    private void ModsList_PointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        CompleteContentPointerGesture(ModsList, eventArgs);
    }

    private void ModsList_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs eventArgs)
    {
        CancelContentPointerGesture();
    }

    private void PatchesList_PointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        _dragDropController.CapturePointerGesture(
            PatchesList,
            this,
            eventArgs,
            false);
    }

    private void PatchesList_PointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        CompleteContentPointerGesture(PatchesList, eventArgs);
    }

    private void CompleteContentPointerGesture(
        ListBox contentList,
        PointerReleasedEventArgs eventArgs)
    {
        bool moved = _dragDropController.TryCompletePointerGesture(
            contentList,
            eventArgs,
            out bool selectionCleared,
            out int sourceIndex,
            out int targetIndex);
        if (moved)
        {
            _viewModel.MoveModInList(sourceIndex, targetIndex);
        }

        if (moved || selectionCleared)
        {
            eventArgs.Handled = true;
        }

        HideDragPreview();
    }

    private void TileContextMenu_Opened(object? sender, RoutedEventArgs eventArgs)
    {
        CancelContentPointerGesture();
    }

    private void CancelContentPointerGesture()
    {
        _dragDropController?.CancelPointerGesture();
        HideDragPreview();
    }

    private void UpdateDragPreview(Point pointerPosition)
    {
        if (_dragDropController.DraggedModification is not { } modification)
        {
            HideDragPreview();
            return;
        }

        const double PreviewWidth = 360;
        const double PreviewHeight = 72;
        const double PointerOffset = 18;
        double layerWidth = DragLayer.Bounds.Width;
        double layerHeight = DragLayer.Bounds.Height;
        double horizontalPosition = pointerPosition.X + PointerOffset;
        if (horizontalPosition + PreviewWidth > layerWidth)
        {
            horizontalPosition = pointerPosition.X - PreviewWidth - PointerOffset;
        }

        ModificationDragPreview.DataContext = modification;
        Canvas.SetLeft(
            ModificationDragPreview,
            Math.Clamp(horizontalPosition, 0, Math.Max(0, layerWidth - PreviewWidth)));
        Canvas.SetTop(
            ModificationDragPreview,
            Math.Clamp(
                pointerPosition.Y - PreviewHeight / 2,
                0,
                Math.Max(0, layerHeight - PreviewHeight)));
        ModificationDragPreview.IsVisible = true;
    }

    private void HideDragPreview()
    {
        ModificationDragPreview.IsVisible = false;
        ModificationDragPreview.DataContext = null;
    }

    private async void ModsList_SelectionChangedAsync(object? sender, SelectionChangedEventArgs eventArgs)
    {
        await ExecuteWindowOperationAsync(
            "changing the selected modification",
            () => _contentController.HandleModsListSelectionChangedAsync(eventArgs));
    }

    private async void ChildContentList_SelectionChangedAsync(object? sender, SelectionChangedEventArgs eventArgs)
    {
        await ExecuteSyncAsync(
            "changing selected child content",
            () => _contentController.HandleChildContentListSelectionChanged(eventArgs));
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

                _viewModel.SaveModsListVerticalOffset(
                    LauncherDragDropController.GetVerticalScrollOffset(ModsList));
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

        CancelContentPointerGesture();
        _viewModel.PropertyChanged -= ViewModel_PropertyChangedAsync;
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
                _windowContext,
                _windowLifetime.Token));
    }

    private async void LaunchWorldBuilder_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        await ExecuteWindowOperationAsync(
            "launching World Builder",
            () => _workflowCoordinator.LaunchAsync(
                GameLaunchTargetKind.WorldBuilder,
                _windowContext,
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
                _windowContext,
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
            () => _contentActionCoordinator.AddRepositoryModificationAsync(
                _windowContext,
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
            () => _contentActionCoordinator.ImportManualContentAsync(
                _windowContext,
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
            () => _contentActionCoordinator.UpdateModificationAsync(
                _windowContext,
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
            () => _contentActionCoordinator.ChangeVersionImageAsync(
                _windowContext,
                modification,
                _windowLifetime.Token));
    }

    /// <summary>
    ///     Opens the tile link named by the invoking control's <see cref="Control.Tag" />.
    /// </summary>
    private async void OpenTileLink_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Control { Tag: LauncherTileLinkKind kind })
        {
            return;
        }

        await ApplyModificationActionAsync(
            sender,
            $"opening a {kind} tile link",
            modification => _contentActionCoordinator.OpenTileLink(modification, kind));
    }

    private async void DeleteVersion_ClickAsync(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Control { DataContext: ModificationVersionSelection versionSelection })
        {
            await ExecuteWindowOperationAsync(
                "deleting a content version",
                () => _contentActionCoordinator.DeleteVersionAsync(
                    _windowContext,
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
            () => _contentActionCoordinator.DeleteModificationAsync(
                _windowContext,
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

    private async void ViewModel_PropertyChangedAsync(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(MainWindowViewModel.SelectedGameClientOption))
        {
            if (IsVisible)
            {
                await ShowRetailClientRecommendationIfNeededAsync();
            }

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

    private Task ShowRetailClientRecommendationIfNeededAsync()
    {
        return ExecuteWindowOperationAsync(
            "showing the retail client recommendation",
            () => _workflowCoordinator.ShowRetailClientRecommendationIfNeededAsync(
                _viewModel.SelectedGameClientOption,
                this));
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
