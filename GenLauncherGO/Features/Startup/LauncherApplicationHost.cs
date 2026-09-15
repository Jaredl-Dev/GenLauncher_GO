using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using GenLauncherGO.Features.Integrity;
using GenLauncherGO.Features.Launcher;
using GenLauncherGO.Features.Launcher.Views;
using GenLauncherGO.Features.Launching;
using GenLauncherGO.Features.Mods;
using GenLauncherGO.Features.Settings;
using GenLauncherGO.Features.Settings.Views;
using GenLauncherGO.Features.Startup.Views;
using GenLauncherGO.Features.Updating;
using GenLauncherGO.Shared.Archives;
using GenLauncherGO.Shared.Dialogs;
using GenLauncherGO.Shared.Errors;
using GenLauncherGO.Shared.Localization;
using GenLauncherGO.Shared.Logging;
using GenLauncherGO.Shared.Persistence;
using GenLauncherGO.Shared.Remote;
using GenLauncherGO.Shared.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Features.Startup;

/// <summary>
///     Boots the standalone Avalonia launcher and owns runtime service composition.
/// </summary>
internal sealed class LauncherApplicationHost : IDisposable
{
    private const string VelopackUpdateRepositoryUrlMetadataName = "VelopackUpdateRepositoryUrl";

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
        : this(new AvaloniaLauncherStringLocalizer())
    {
    }

    private LauncherApplicationHost(ILauncherStringLocalizer stringLocalizer)
        : this(
            new FileSystemLauncherPathResolver(),
            new WindowsLauncherHostEnvironmentService(),
            stringLocalizer,
            new AvaloniaStartupDialogService(
                stringLocalizer,
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
            GetRequiredService<LauncherRestartCoordinator>().RestartKind,
            ReleaseSingleInstance);
    }

    private Task<bool> StartCoreAsync(IClassicDesktopStyleApplicationLifetime? desktop)
    {
        if (desktop != null)
        {
            AttachDispatcherExceptionBoundary(desktop);
        }

        ServiceProvider? bootstrapProvider = null;
        try
        {
            // The executable manifest is authoritative. This fail-closed check protects unsupported entry paths
            // that host the managed DLL without honoring that manifest.
            if (!_hostEnvironmentService.IsCurrentProcessElevated())
            {
                return ShowStartupMessageAndStopAsync(
                    _stringLocalizer["AdministratorPermissionRequired"]);
            }

            LauncherStoragePaths storagePaths = ResolveStandaloneStorage();
            bootstrapProvider = CreateBootstrapServiceProvider(storagePaths);
            ILauncherPreferencesService preferencesService =
                bootstrapProvider.GetRequiredService<ILauncherPreferencesService>();
            IStandaloneStartupWorkflow startupWorkflow =
                bootstrapProvider.GetRequiredService<IStandaloneStartupWorkflow>();

            // This method intentionally remains synchronous through culture selection. CurrentUICulture flows with
            // the caller's execution context, so applying it inside an awaited child method would be restored when
            // that child returned and later Avalonia views would fall back to the Windows language.
            LauncherStartupCulture.Apply(preferencesService.Current.Shared.UseEnglishLanguage);
            return ContinueStartupAsync(
                desktop,
                storagePaths,
                bootstrapProvider,
                preferencesService,
                startupWorkflow);
        }
        catch (Exception exception)
        {
            bootstrapProvider?.Dispose();
            return ShowStartupFailureAndStopAsync(exception);
        }
    }

    private async Task<bool> ContinueStartupAsync(
        IClassicDesktopStyleApplicationLifetime? desktop,
        LauncherStoragePaths storagePaths,
        ServiceProvider bootstrapProvider,
        ILauncherPreferencesService preferencesService,
        IStandaloneStartupWorkflow startupWorkflow)
    {
        using (bootstrapProvider)
        {
            try
            {
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
                InitializeServices(preferencesService.Current.Shared.EnableDiagnosticLogging);

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
        string launcherRootDirectory = _hostEnvironmentService.GetLauncherRootDirectory();
        return _launcherPathResolver.Resolve(launcherRootDirectory);
    }

    private ServiceProvider CreateBootstrapServiceProvider(LauncherStoragePaths storagePaths)
    {
        ServiceCollection services = [];

        // Bootstrap logging must not create storage before the launcher location has been accepted.
        // Rolling file logging starts with the runtime provider after setup selects the active game paths.
        services.AddLogging();
        AddPreferencesServices(services, storagePaths.PreferencesFilePath);
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

    private void InitializeServices(bool enableDiagnosticLogging)
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
            _stringLocalizer,
            enableDiagnosticLogging);
        _serviceProvider = services.BuildServiceProvider();
        _loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
        GetLogger<LauncherApplicationHost>().LogInformation(
            "Standalone launcher services initialized. Version: {LauncherVersion}; game: {Game}; diagnostic logging: {DiagnosticLoggingEnabled}.",
            _runtimeContext.CurrentLauncherVersion,
            _runtimeContext.CurrentlyManagedGame,
            enableDiagnosticLogging);
    }

    /// <summary>
    ///     Creates the authoritative application service collection used by the launcher host and composition tests.
    /// </summary>
    internal static ServiceCollection CreateServiceCollection(
        LauncherRuntimeContext runtimeContext,
        ILauncherPathResolver launcherPathResolver,
        ILauncherHostEnvironmentService hostEnvironmentService,
        ILauncherStringLocalizer stringLocalizer,
        bool enableDiagnosticLogging = false)
    {
        ArgumentNullException.ThrowIfNull(runtimeContext);
        ArgumentNullException.ThrowIfNull(launcherPathResolver);
        ArgumentNullException.ThrowIfNull(hostEnvironmentService);
        ArgumentNullException.ThrowIfNull(stringLocalizer);

        ServiceCollection services = [];
        services.AddGenLauncherGoLogging(
            runtimeContext.StoragePaths.LogsDirectory,
            enableDiagnosticLogging);
        AddPreferencesServices(services, runtimeContext.StoragePaths.PreferencesFilePath);
        services.AddSingleton(runtimeContext.RuntimePaths);
        services.AddSingleton(runtimeContext.StoragePaths);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ILauncherApplicationUpdateService>(serviceProvider =>
            new VelopackLauncherApplicationUpdateService(
                serviceProvider.GetRequiredService<LauncherStoragePaths>(),
                serviceProvider.GetRequiredService<IAtomicFileWriter>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                GetApplicationUpdateRepositoryUrl(),
                serviceProvider.GetRequiredService<ILogger<VelopackLauncherApplicationUpdateService>>()));
        services.AddSingleton<IArchiveExtractor, ArchiveExtractor>();
        services.AddSingleton<IResumableFileDownloader, ResumableHttpFileDownloader>();
        services.AddSingleton<IRemoteAssetDownloader, HttpRemoteAssetDownloader>();
        services.AddSingleton<IRemoteYamlDocumentReader, HttpRemoteYamlDocumentReader>();

        services.AddSingleton<IDownloadFileMetadataReader, HttpDownloadFileMetadataReader>();
        services.AddSingleton<IS3ObjectManifestReader, MinioS3ObjectManifestReader>();
        services.AddSingleton<ManagedPackageSourceResolver>();
        services.AddSingleton<IRemotePackageSizeResolver>(serviceProvider =>
            serviceProvider.GetRequiredService<ManagedPackageSourceResolver>());
        services.AddSingleton<ISingleFilePackageUpdater, SingleFilePackageUpdater>();
        services.AddSingleton<IS3PackageUpdater, S3PackageUpdater>();
        services.AddSingleton<IPackageDownloadService, PackageDownloadService>();
        services.AddSingleton<IRemoteConnectionProbe, HttpRemoteConnectionProbe>();
        services.AddSingleton<IFileHashService, Md5FileHashService>();

        services.AddSingleton<IContentIntegrityService, FileSystemContentIntegrityService>();

        services.AddSingleton<IHardLinkCreator, WindowsHardLinkCreator>();
        services.AddSingleton<FileSystemDeploymentService>();
        services.AddSingleton<ILaunchPreparationService, DeploymentLaunchPreparationService>();
        services.AddSingleton<FileSystemLaunchContentIntegrityTargetBuilder>();
        services.AddSingleton<ILaunchContentIntegrityResolutionService,
            FileSystemLaunchContentIntegrityResolutionService>();
        services.AddSingleton<IProcessFamilyLauncher, WindowsProcessFamilyLauncher>();
        services.AddSingleton<IGameProcessLauncher, WindowsGameProcessLauncher>();
        services.AddSingleton<IGameExecutableDiscoveryService, WindowsGameExecutableDiscoveryService>();

        services.AddSingleton<ILauncherShellService, WindowsLauncherShellService>();

        services.AddSingleton<ILauncherContentStateStore, YamlLauncherContentStateStore>();
        services.AddSingleton<ILocalLauncherContentService, FileSystemLocalLauncherContentService>();
        services.AddSingleton<IManualModificationImporter, FileSystemManualModificationImporter>();
        services.AddSingleton<IModificationImageFileService, FileSystemModificationImageFileService>();
        services.AddSingleton<IModificationThemeCache, FileSystemModificationThemeCache>();
        services.AddSingleton<RemoteLauncherCatalogClient>();
        services.AddSingleton<LauncherCatalogImageCache>();
        services.AddSingleton<LauncherLocalContentReconciler>();
        services.AddSingleton<ILauncherContentCatalog, LauncherContentCatalogService>();
        services.AddSingleton(launcherPathResolver);
        services.AddSingleton(hostEnvironmentService);
        services.AddSingleton<IGameInstallationService, WindowsGameInstallationService>();
        services.AddSingleton(runtimeContext);
        services.AddSingleton(stringLocalizer);
        services.AddSingleton<ModificationImageSourceFactory>();
        services.AddSingleton<LaunchContentIntegrityCoordinator>();
        services.AddSingleton<LauncherPackageActivityService>();
        services.AddSingleton<LauncherLaunchCoordinator>();
        services.AddSingleton<LauncherLaunchReadinessCoordinator>();
        services.AddSingleton<LauncherExecutableSelectionService>();
        services.AddSingleton<LauncherGameSessionCoordinator>();
        services.AddSingleton<ILauncherFilePicker, AvaloniaLauncherFilePicker>();
        services.AddSingleton<LauncherManualImportCoordinator>();
        services.AddSingleton<LauncherPackageActivityAdmissionService>();
        services.AddSingleton<LauncherModificationDownloadCoordinator>();
        services.AddSingleton<LauncherContentActionCoordinator>();
        services.AddSingleton<LauncherCloseGuard>();
        services.AddSingleton<LauncherWindowWorkflowCoordinator>();
        services.AddSingleton<LauncherApplicationUpdateCoordinator>();
        services.AddTransient<LauncherDragDropController>();
        services.AddTransient<MainWindowViewModel>();

        services.AddSingleton<ILauncherDialogService, AvaloniaLauncherDialogService>();

        services.AddTransient(serviceProvider => new LauncherInstallationsViewModel(
            serviceProvider.GetRequiredService<ILauncherPreferencesService>().Current.Installations,
            serviceProvider.GetRequiredService<LauncherRuntimeContext>().StoragePaths,
            serviceProvider.GetRequiredService<IGameInstallationService>(),
            serviceProvider.GetRequiredService<ILauncherHostEnvironmentService>(),
            serviceProvider.GetRequiredService<ILauncherFilePicker>(),
            serviceProvider.GetRequiredService<ILauncherStringLocalizer>()));
        services.AddTransient<LauncherSettingsViewModel>();
        services.AddTransient<LauncherExecutableManagementViewModel>();
        services.AddTransient<LauncherSettingsWindow>();
        services.AddTransient<Func<LauncherSettingsWindow>>(serviceProvider =>
            () => serviceProvider.GetRequiredService<LauncherSettingsWindow>());

        services.AddTransient<Func<MainWindow>>(serviceProvider =>
            () => serviceProvider.GetRequiredService<MainWindow>());
        services.AddSingleton<IUiExceptionBoundary, AvaloniaUiExceptionBoundary>();
        services.AddSingleton<IStartupDialogService, AvaloniaStartupDialogService>();
        services.AddSingleton<LauncherRestartCoordinator>();
        services.AddSingleton<LauncherShutdownCoordinator>();
        services.AddTransient<InitWindowViewModel>();
        services.AddTransient<InitWindow>();
        services.AddTransient<MainWindow>();
        return services;
    }

    private static void AddPreferencesServices(IServiceCollection services, string preferencesFilePath)
    {
        services.TryAddSingleton<IAtomicFileWriter, AtomicFileWriter>();
        services.AddSingleton<IYamlDocumentStore<LauncherPreferencesSchemaDocument>>(serviceProvider =>
            new YamlDocumentStore<LauncherPreferencesSchemaDocument>(
                preferencesFilePath,
                serviceProvider.GetRequiredService<IAtomicFileWriter>(),
                serviceProvider.GetRequiredService<ILogger<YamlDocumentStore<LauncherPreferencesSchemaDocument>>>()));
        services.AddSingleton<IYamlDocumentStore<LauncherPreferencesDocument>>(serviceProvider =>
            new YamlDocumentStore<LauncherPreferencesDocument>(
                preferencesFilePath,
                serviceProvider.GetRequiredService<IAtomicFileWriter>(),
                serviceProvider.GetRequiredService<ILogger<YamlDocumentStore<LauncherPreferencesDocument>>>()));
        services.AddSingleton<IYamlDocumentStore<LegacyLauncherPreferencesDocument>>(serviceProvider =>
            new YamlDocumentStore<LegacyLauncherPreferencesDocument>(
                preferencesFilePath,
                serviceProvider.GetRequiredService<IAtomicFileWriter>(),
                serviceProvider.GetRequiredService<ILogger<YamlDocumentStore<LegacyLauncherPreferencesDocument>>>()));
        services.AddSingleton<ILauncherPreferencesService, PreferencesService>();
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

    private static string GetApplicationUpdateRepositoryUrl()
    {
        Assembly assembly = typeof(LauncherApplicationHost).Assembly;
        return assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                   .Single(attribute =>
                       string.Equals(
                           attribute.Key,
                           VelopackUpdateRepositoryUrlMetadataName,
                           StringComparison.Ordinal))
                   .Value
               ?? throw new InvalidOperationException(
                   "The application-update repository URL assembly metadata is missing.");
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
            LauncherApplicationDefaults.GeneralsOnlineDiscordUrl));
    }

    private async Task<bool> ShowStartupFailureAndStopAsync(Exception exception)
    {
        await ShowStartupFailureAsync(exception);
        return false;
    }

    private async Task<bool> ShowStartupMessageAndStopAsync(string message)
    {
        try
        {
            await _bootstrapStartupDialogService.ShowMessageAsync(message);
            return false;
        }
        catch (Exception exception)
        {
            return await ShowStartupFailureAndStopAsync(exception);
        }
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
