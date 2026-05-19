using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using GenLauncherGO.UI.Features.Launcher.Views;
using GenLauncherGO.UI.Features.Startup.ViewModels;
using GenLauncherGO.UI.Shared.Controls;
using GenLauncherGO.UI.Shared.Errors;

namespace GenLauncherGO.UI.Features.Startup.Views;

/// <summary>
///     Runs startup preparation before the main launcher window is shown.
/// </summary>
internal partial class InitWindow : Window
{
    private readonly IUiExceptionBoundary _exceptionBoundary = null!;

    private readonly Func<MainWindow> _mainWindowFactory = null!;
    private readonly InitWindowViewModel _viewModel = null!;

    public InitWindow()
    {
        InitializeComponent();
        LauncherWindowScaling.Attach(this);
    }

    public InitWindow(
        InitWindowViewModel viewModel,
        Func<MainWindow> mainWindowFactory,
        IUiExceptionBoundary exceptionBoundary)
        : this()
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _mainWindowFactory = mainWindowFactory ?? throw new ArgumentNullException(nameof(mainWindowFactory));
        _exceptionBoundary = exceptionBoundary ?? throw new ArgumentNullException(nameof(exceptionBoundary));

        DataContext = _viewModel;
        Loaded += Window_LoadedAsync;
        _viewModel.StartupCompleted += ViewModel_StartupCompleted;
        _viewModel.ShutdownRequested += ViewModel_ShutdownRequested;
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        Loaded -= Window_LoadedAsync;
        if (_viewModel != null)
        {
            _viewModel.StartupCompleted -= ViewModel_StartupCompleted;
            _viewModel.ShutdownRequested -= ViewModel_ShutdownRequested;
        }

        base.OnClosed(e);
    }

    /// <summary>
    ///     Awaits startup from the loaded-event boundary and routes failures through the shared exception boundary.
    /// </summary>
    private async void Window_LoadedAsync(object? sender, RoutedEventArgs e)
    {
        UiOperationOutcome outcome = await _exceptionBoundary.ExecuteAsync(
            "initializing the launcher",
            _viewModel.StartAsync,
            this);
        if (outcome == UiOperationOutcome.Failed)
        {
            try
            {
                ShutdownApplication();
            }
            catch (Exception exception)
            {
                await _exceptionBoundary.HandleUnexpectedAsync(
                    exception,
                    "shutting down after launcher initialization failed",
                    this);
            }
        }
    }

    /// <summary>
    ///     Shows the main window after startup completes.
    /// </summary>
    private void ViewModel_StartupCompleted(object? sender, InitWindowStartupCompletedEventArgs e)
    {
        try
        {
            Hide();
            MainWindow mainWindow = _mainWindowFactory();
            mainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            IClassicDesktopStyleApplicationLifetime desktop = GetDesktopLifetime();
            desktop.MainWindow = mainWindow;
            mainWindow.Closed += MainWindow_Closed;
            mainWindow.Show();
            Close();
        }
        catch
        {
            ShutdownApplication();
            throw;
        }
    }

    /// <summary>
    ///     Shuts down the Avalonia application when startup is canceled.
    /// </summary>
    private void ViewModel_ShutdownRequested(object? sender, EventArgs e)
    {
        ShutdownApplication();
    }

    private static IClassicDesktopStyleApplicationLifetime GetDesktopLifetime()
    {
        return Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime ??
               throw new InvalidOperationException("The Avalonia desktop lifetime is not available.");
    }

    private static void ShutdownApplication()
    {
        GetDesktopLifetime().Shutdown();
    }

    private static void MainWindow_Closed(object? sender, EventArgs e)
    {
        if (sender is Window mainWindow)
        {
            mainWindow.Closed -= MainWindow_Closed;
        }

        ShutdownApplication();
    }
}
