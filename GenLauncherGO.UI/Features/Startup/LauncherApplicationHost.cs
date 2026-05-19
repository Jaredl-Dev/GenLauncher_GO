using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Core.Startup.Models;
using GenLauncherGO.Infrastructure;
using GenLauncherGO.Infrastructure.Logging;
using GenLauncherGO.Infrastructure.Settings.Composition;
using GenLauncherGO.Infrastructure.Startup;
using GenLauncherGO.UI.Features.Launcher.Contracts;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Startup.Contracts;
using GenLauncherGO.UI.Features.Startup.Models;
using GenLauncherGO.UI.Features.Startup.Services;
using GenLauncherGO.UI.Features.Startup.Views;
using GenLauncherGO.UI.Shared.Errors;
using GenLauncherGO.UI.Shared.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.UI.Features.Startup;

/// <summary>
/// Boots the standalone Avalonia launcher and owns runtime service composition.
/// </summary>
internal sealed class LauncherApplicationHost : IDisposable
{
    private readonly ILauncherPathResolver _launcherPathResolver;
    private readonly ILauncherHostEnvironmentService _hostEnvironmentService;
    private readonly ILauncherStringLocalizer _stringLocalizer;
    private readonly IStartupDialogService _bootstrapStartupDialogService;
    private readonly IStandaloneStartupWorkflow? _standaloneStartupWorkflow;
    private ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;
    private LauncherRuntimeContext? _runtimeContext;
    private LauncherStoragePaths? _storagePaths;
    private ServiceProvider? _serviceProvider;
    private ILauncherSingleInstanceGuard? _singleInstanceGuard;
    private IClassicDesktopStyleApplicationLifetime? _desktopLifetime;
    private bool _dispatcherExceptionHandlerAttached;
    private bool _shutdownCompleted;

    public LauncherApplicationHost()
        : this(
            new FileSystemLauncherPathResolver(),
            new WindowsLauncherHostEnvironmentService(),
            new AvaloniaLauncherStringLocalizer(),
            new AvaloniaStartupDialogService(
                new AvaloniaLauncherStringLocalizer(),
                NullLogger<AvaloniaStartupDialogService>.Instance))
    {
    }

    internal LauncherApplicationHost(
        ILauncherPathResolver launcherPathResolver,
        ILauncherHostEnvironmentService hostEnvironmentService,
        ILauncherStringLocalizer stringLocalizer,
        IStartupDialogService bootstrapStartupDialogService,
        IStandaloneStartupWorkflow? standaloneStartupWorkflow = null)
    {
        _launcherPathResolver = launcherPathResolver ?? throw new ArgumentNullException(nameof(launcherPathResolver));
        _hostEnvironmentService = hostEnvironmentService ??
                                  throw new ArgumentNullException(nameof(hostEnvironmentService));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _bootstrapStartupDialogService = bootstrapStartupDialogService ??
                                         throw new ArgumentNullException(nameof(bootstrapStartupDialogService));
        _standaloneStartupWorkflow = standaloneStartupWorkflow;
    }

    /// <summary>
    /// Starts the launcher application using the initialized Avalonia desktop lifetime.
    /// </summary>
    public Task<bool> StartAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        return StartCoreAsync(desktop);
    }

    /// <summary>
    /// Runs startup without a desktop lifetime for focused host tests that stop before opening a window.
    /// </summary>
    internal Task RunAsync()
    {
        return StartCoreAsync(desktop: null);
    }

    /// <summary>
    /// Restores transient launch state and performs a requested restart before the desktop lifetime exits.
    /// </summary>
    public void Shutdown()
    {
        if (_shutdownCompleted)
        {
            return;
        }

        _shutdownCompleted = true;
        ReturnGameFolderToOriginalState();
        RestartWhenRequested();
    }

    private async Task<bool> StartCoreAsync(IClassicDesktopStyleApplicationLifetime? desktop)
    {
        if (desktop != null)
        {
            AttachDispatcherExceptionBoundary(desktop);
        }

        try
        {
            // The executable manifest is authoritative. This fail-closed check protects unsupported entry paths
            // that host the managed DLL without honoring that manifest.
            if (!_hostEnvironmentService.IsCurrentProcessElevated())
            {
                await _bootstrapStartupDialogService.ShowMessageAsync(
                    _stringLocalizer["AdministratorPermissionRequired"]);
                return false;
            }

            ResolveStandaloneStorage();
            using ServiceProvider bootstrapProvider = CreateBootstrapServiceProvider();
            ILauncherPreferencesService preferencesService =
                bootstrapProvider.GetRequiredService<ILauncherPreferencesService>();
            IStandaloneStartupWorkflow startupWorkflow =
                bootstrapProvider.GetRequiredService<IStandaloneStartupWorkflow>();

            ApplyPersistedCulture(preferencesService);
            if (await ShowBlockingLauncherLocationAsync(startupWorkflow))
            {
                return false;
            }

            PrepareStandaloneStorage();

            if (!OtherInstanceCanStart())
            {
                _hostEnvironmentService.ActivateCurrentProcessWindow();
                return false;
            }

            StandaloneStartupResult startup = await RunStandaloneSetupAsync(
                startupWorkflow,
                preferencesService);
            if (!startup.CanStart || string.IsNullOrWhiteSpace(startup.GameDirectory))
            {
                return false;
            }

            InitializeRuntimeContext(startup);
            InitializeServices();
            ApplyConfiguredCulture();

            if (desktop == null)
            {
                throw new InvalidOperationException(
                    "The Avalonia desktop lifetime is required to show the launcher window.");
            }

            InitWindow initWindow = GetRequiredService<InitWindow>();
            initWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            desktop.MainWindow = initWindow;
            initWindow.Show();
            return true;
        }
        catch (Exception exception)
        {
            await ShowStartupFailureAsync(exception);
            return false;
        }
    }

    public void Dispose()
    {
        if (_dispatcherExceptionHandlerAttached)
        {
            Dispatcher.UIThread.UnhandledException -= HandleDispatcherUnhandledExceptionAsync;
            _dispatcherExceptionHandlerAttached = false;
        }

        _desktopLifetime = null;
        _singleInstanceGuard?.Dispose();
        _singleInstanceGuard = null;
        _serviceProvider?.Dispose();
        _serviceProvider = null;
        _loggerFactory = NullLoggerFactory.Instance;
    }

    private static string GetCurrentLauncherVersion()
    {
        Assembly assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "Failed to retrieve";
    }

    private void ResolveStandaloneStorage()
    {
        string executableDirectory = _hostEnvironmentService.GetExecutableDirectory();
        _storagePaths = _launcherPathResolver.Resolve(executableDirectory);
    }

    private void PrepareStandaloneStorage()
    {
        LauncherStoragePaths storagePaths = _storagePaths ??
                                            throw new InvalidOperationException(
                                                "Standalone storage paths have not been resolved.");
        _launcherPathResolver.PrepareLauncherDirectories(storagePaths);
    }

    private ServiceProvider CreateBootstrapServiceProvider()
    {
        LauncherStoragePaths storagePaths = _storagePaths ??
                                            throw new InvalidOperationException(
                                                "Standalone storage paths have not been resolved.");
        ServiceCollection services = new();

        // Bootstrap logging must not create storage before the launcher location has been accepted.
        // Rolling file logging starts with the runtime provider after setup selects the active game paths.
        services.AddLogging();
        services.AddGenLauncherGoSettingsInfrastructure(storagePaths.PreferencesFilePath);
        services.AddSingleton<IGameInstallationService, WindowsGameInstallationService>();
        services.AddSingleton<ILauncherFilePicker, AvaloniaLauncherFilePicker>();
        services.AddSingleton<ILauncherHostEnvironmentService>(_hostEnvironmentService);
        services.AddSingleton<ILauncherStringLocalizer>(_stringLocalizer);
        services.AddSingleton<IStartupDialogService>(_bootstrapStartupDialogService);
        if (_standaloneStartupWorkflow != null)
        {
            services.AddSingleton(_standaloneStartupWorkflow);
        }
        else
        {
            services.AddSingleton<IStandaloneStartupWorkflow, AvaloniaStandaloneStartupWorkflow>();
        }

        return services.BuildServiceProvider();
    }

    private static void ApplyPersistedCulture(ILauncherPreferencesService preferencesService)
    {
        LauncherStartupCulture.Apply(
            preferencesService.Current.Shared.UseEnglishLanguage);
    }

    private Task<bool> ShowBlockingLauncherLocationAsync(IStandaloneStartupWorkflow startupWorkflow)
    {
        LauncherStoragePaths storagePaths = _storagePaths ??
                                            throw new InvalidOperationException(
                                                "Standalone storage paths have not been resolved.");
        return startupWorkflow.ShowBlockingLauncherLocationAsync(storagePaths);
    }

    private Task<StandaloneStartupResult> RunStandaloneSetupAsync(
        IStandaloneStartupWorkflow startupWorkflow,
        ILauncherPreferencesService preferencesService)
    {
        LauncherStoragePaths storagePaths = _storagePaths ??
                                            throw new InvalidOperationException(
                                                "Standalone storage paths have not been initialized.");
        return startupWorkflow.RunAsync(
            storagePaths,
            preferencesService);
    }

    private void InitializeRuntimeContext(StandaloneStartupResult startup)
    {
        LauncherStoragePaths storagePaths = _storagePaths ??
                                            throw new InvalidOperationException(
                                                "Standalone storage paths have not been initialized.");
        LauncherPaths activePaths = storagePaths.CreateGamePaths(
            startup.Game,
            startup.GameDirectory ??
            throw new InvalidOperationException("A validated game installation was not selected."));
        LauncherRuntimePathContext runtimePaths = new(storagePaths, activePaths);
        _runtimeContext = new LauncherRuntimeContext(runtimePaths, GetCurrentLauncherVersion());
    }

    private void InitializeServices()
    {
        _serviceProvider?.Dispose();
        if (_runtimeContext == null)
        {
            throw new InvalidOperationException("Launcher runtime paths have not been initialized.");
        }

        ServiceCollection services = CreateServiceCollection(
            _runtimeContext,
            _launcherPathResolver,
            _hostEnvironmentService,
            _stringLocalizer);
        _serviceProvider = services.BuildServiceProvider();
        _loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
        GetLogger<LauncherApplicationHost>().LogInformation(
            "Standalone launcher services initialized for {Game}.",
            _runtimeContext.CurrentlyManagedGame);
    }

    /// <summary>
    /// Creates the authoritative application service collection used by the launcher host and composition tests.
    /// </summary>
    internal static ServiceCollection CreateServiceCollection(
        LauncherRuntimeContext runtimeContext,
        ILauncherPathResolver launcherPathResolver,
        ILauncherHostEnvironmentService hostEnvironmentService,
        ILauncherStringLocalizer stringLocalizer)
    {
        ArgumentNullException.ThrowIfNull(runtimeContext);
        ArgumentNullException.ThrowIfNull(launcherPathResolver);
        ArgumentNullException.ThrowIfNull(hostEnvironmentService);
        ArgumentNullException.ThrowIfNull(stringLocalizer);

        ServiceCollection services = new();
        services.AddGenLauncherGoLogging(runtimeContext.StoragePaths.LogsDirectory);
        services.AddGenLauncherGoSettingsInfrastructure(runtimeContext.StoragePaths.PreferencesFilePath);
        services.AddSingleton(runtimeContext.RuntimePaths);
        services.AddSingleton(runtimeContext.StoragePaths);
        services.AddGenLauncherGoInfrastructure();
        services.AddSingleton(launcherPathResolver);
        services.AddSingleton(hostEnvironmentService);
        services.AddSingleton<IGameInstallationService, WindowsGameInstallationService>();
        services.AddSingleton(runtimeContext);
        services.AddSingleton(stringLocalizer);
        services.AddGenLauncherGoUi();
        return services;
    }

    private void ApplyConfiguredCulture()
    {
        LauncherStartupCulture.Apply(
            GetRequiredService<ILauncherPreferencesService>().Current.Shared.UseEnglishLanguage);
    }

    private void AttachDispatcherExceptionBoundary(
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        _desktopLifetime = desktop;
        if (_dispatcherExceptionHandlerAttached)
        {
            return;
        }

        Dispatcher.UIThread.UnhandledException += HandleDispatcherUnhandledExceptionAsync;
        _dispatcherExceptionHandlerAttached = true;
    }

    private async void HandleDispatcherUnhandledExceptionAsync(
        object? sender,
        DispatcherUnhandledExceptionEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        IUiExceptionBoundary? exceptionBoundary =
            _serviceProvider?.GetService<IUiExceptionBoundary>();
        try
        {
            if (exceptionBoundary != null)
            {
                await exceptionBoundary.HandleUnexpectedAsync(
                    eventArgs.Exception,
                    "handling an Avalonia UI event",
                    _desktopLifetime?.MainWindow);
            }
            else
            {
                await ShowStartupFailureAsync(eventArgs.Exception);
            }
        }
        catch (Exception exception)
        {
            GetLogger<LauncherApplicationHost>().LogError(
                exception,
                "The global Avalonia exception boundary failed.");
        }
    }

    private async Task ShowStartupFailureAsync(Exception exception)
    {
        GetLogger<LauncherApplicationHost>().LogError(exception, "Launcher startup failed.");
        await GetStartupDialogService().ShowMessageAsync(string.Format(
            _stringLocalizer["ErrorMsg"],
            exception.Message,
            exception.StackTrace,
            _runtimeContext?.CurrentLauncherVersion ?? GetCurrentLauncherVersion(),
            @"https://discord.playgenerals.online/"));
    }

    private void ReturnGameFolderToOriginalState()
    {
        if (_serviceProvider == null || _runtimeContext == null)
        {
            return;
        }

        LauncherPaths paths = _runtimeContext.RuntimePaths.ActivePaths;
        try
        {
            bool succeeded = GetRequiredService<ILaunchPreparationService>()
                .Cleanup(paths, CancellationToken.None);
            if (!succeeded)
            {
                GetLogger<LauncherApplicationHost>()
                    .LogWarning("Deployment cleanup did not complete successfully on shutdown.");
            }
        }
        catch (Exception exception)
        {
            GetLogger<LauncherApplicationHost>().LogWarning(
                exception,
                "Could not return the game folder to its original state on shutdown.");
        }
    }

    private void RestartWhenRequested()
    {
        if (_serviceProvider == null ||
            !GetRequiredService<LauncherRestartCoordinator>().IsRestartRequested)
        {
            return;
        }

        _singleInstanceGuard?.Dispose();
        _singleInstanceGuard = null;
        LauncherRestartResult result = _hostEnvironmentService.TryRestartCurrentProcess();
        if (result.Succeeded)
        {
            return;
        }

        GetLogger<LauncherApplicationHost>().LogError(
            "The requested launcher restart could not start a replacement process. {RestartError}",
            result.ErrorMessage);
    }

    private bool OtherInstanceCanStart()
    {
        _singleInstanceGuard?.Dispose();
        _singleInstanceGuard = _hostEnvironmentService.TryAcquireSingleInstance(
            "GenLauncherGO",
            TimeSpan.FromSeconds(5));
        return _singleInstanceGuard.IsAcquired;
    }

    private ILogger<T> GetLogger<T>()
    {
        return _loggerFactory.CreateLogger<T>();
    }

    private IStartupDialogService GetStartupDialogService()
    {
        return _serviceProvider?.GetService<IStartupDialogService>() ?? _bootstrapStartupDialogService;
    }

    private T GetRequiredService<T>()
        where T : notnull
    {
        if (_serviceProvider == null)
        {
            throw new InvalidOperationException("Launcher services have not been initialized.");
        }

        return _serviceProvider.GetRequiredService<T>();
    }
}
