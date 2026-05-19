using System;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
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
///     Boots the standalone Avalonia launcher and owns runtime service composition.
/// </summary>
internal sealed class LauncherApplicationHost : IDisposable
{
    private readonly IStartupDialogService _bootstrapStartupDialogService;
    private readonly ILauncherHostEnvironmentService _hostEnvironmentService;
    private readonly ILauncherPathResolver _launcherPathResolver;
    private readonly IStandaloneStartupWorkflow? _standaloneStartupWorkflow;
    private readonly ILauncherStringLocalizer _stringLocalizer;
    private IClassicDesktopStyleApplicationLifetime? _desktopLifetime;
    private bool _dispatcherExceptionHandlerAttached;
    private ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;
    private LauncherRuntimeContext? _runtimeContext;
    private ServiceProvider? _serviceProvider;
    private bool _shutdownRequested;
    private ILauncherSingleInstanceGuard? _singleInstanceGuard;
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

    public void Dispose()
    {
        if (_dispatcherExceptionHandlerAttached)
        {
            Dispatcher.UIThread.UnhandledException -= HandleDispatcherUnhandledExceptionAsync;
            _dispatcherExceptionHandlerAttached = false;
        }

        _desktopLifetime = null;
        ReleaseSingleInstance();
        _serviceProvider?.Dispose();
        _serviceProvider = null;
        _loggerFactory = NullLoggerFactory.Instance;
    }

    /// <summary>
    ///     Starts the launcher application using the initialized Avalonia desktop lifetime.
    /// </summary>
    public Task<bool> StartAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        return StartCoreAsync(desktop);
    }

    /// <summary>
    ///     Runs startup without a desktop lifetime for focused host tests that stop before opening a window.
    /// </summary>
    internal Task RunAsync()
    {
        return StartCoreAsync(null);
    }

    /// <summary>
    ///     Restores transient launch state and performs a requested restart before the desktop lifetime exits.
    /// </summary>
    public void Shutdown()
    {
        if (_shutdownRequested)
        {
            return;
        }

        _shutdownRequested = true;
        if (_serviceProvider == null || _runtimeContext == null)
        {
            return;
        }

        GetRequiredService<LauncherShutdownCoordinator>().Shutdown(
            _runtimeContext.RuntimePaths.ActivePaths,
            GetRequiredService<LauncherRestartCoordinator>().IsRestartRequested,
            ReleaseSingleInstance);
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

            LauncherStoragePaths storagePaths = ResolveStandaloneStorage();
            using ServiceProvider bootstrapProvider = CreateBootstrapServiceProvider(storagePaths);
            ILauncherPreferencesService preferencesService =
                bootstrapProvider.GetRequiredService<ILauncherPreferencesService>();
            IStandaloneStartupWorkflow startupWorkflow =
                bootstrapProvider.GetRequiredService<IStandaloneStartupWorkflow>();

            ApplyPersistedCulture(preferencesService);
            if (await startupWorkflow.ShowBlockingLauncherLocationAsync(storagePaths))
            {
                return false;
            }

            _launcherPathResolver.PrepareLauncherDirectories(storagePaths);

            if (!OtherInstanceCanStart())
            {
                _hostEnvironmentService.ActivateCurrentProcessWindow();
                return false;
            }

            StandaloneStartupResult startup = await startupWorkflow.RunAsync(
                storagePaths,
                preferencesService);
            if (!startup.CanStart || string.IsNullOrWhiteSpace(startup.GameDirectory))
            {
                return false;
            }

            InitializeRuntimeContext(storagePaths, startup);
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

    private static string GetCurrentLauncherVersion()
    {
        Assembly assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? LauncherApplicationDefaults.UnavailableLauncherVersion;
    }

    private LauncherStoragePaths ResolveStandaloneStorage()
    {
        string executableDirectory = _hostEnvironmentService.GetExecutableDirectory();
        return _launcherPathResolver.Resolve(executableDirectory);
    }

    private ServiceProvider CreateBootstrapServiceProvider(LauncherStoragePaths storagePaths)
    {
        ServiceCollection services = new();

        // Bootstrap logging must not create storage before the launcher location has been accepted.
        // Rolling file logging starts with the runtime provider after setup selects the active game paths.
        services.AddLogging();
        services.AddGenLauncherGoSettingsInfrastructure(storagePaths.PreferencesFilePath);
        services.AddSingleton<IGameInstallationService, WindowsGameInstallationService>();
        services.AddSingleton<ILauncherFilePicker, AvaloniaLauncherFilePicker>();
        services.AddSingleton<ILauncherHostEnvironmentService>(_hostEnvironmentService);
        services.AddSingleton(_stringLocalizer);
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

    private void InitializeRuntimeContext(
        LauncherStoragePaths storagePaths,
        StandaloneStartupResult startup)
    {
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
    ///     Creates the authoritative application service collection used by the launcher host and composition tests.
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
        await GetStartupDialogService().ShowMessageAsync(string.Format(CultureInfo.CurrentCulture,
            _stringLocalizer["ErrorMsg"],
            exception.Message,
            exception.StackTrace,
            _runtimeContext?.CurrentLauncherVersion ?? GetCurrentLauncherVersion(),
            @"https://discord.playgenerals.online/"));
    }

    private bool OtherInstanceCanStart()
    {
        ReleaseSingleInstance();
        _singleInstanceGuard = _hostEnvironmentService.TryAcquireSingleInstance(
            "GenLauncherGO",
            TimeSpan.FromSeconds(5));
        return _singleInstanceGuard.IsAcquired;
    }

    private void ReleaseSingleInstance()
    {
        _singleInstanceGuard?.Dispose();
        _singleInstanceGuard = null;
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
