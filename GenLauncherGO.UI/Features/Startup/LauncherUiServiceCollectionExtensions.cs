using System;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Services;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Contracts;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Launcher.Support;
using GenLauncherGO.UI.Features.Launcher.ViewModels;
using GenLauncherGO.UI.Features.Launcher.Views;
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Features.Settings.ViewModels;
using GenLauncherGO.UI.Features.Settings.Views;
using GenLauncherGO.UI.Features.Startup.Contracts;
using GenLauncherGO.UI.Features.Startup.Services;
using GenLauncherGO.UI.Features.Startup.ViewModels;
using GenLauncherGO.UI.Features.Startup.Views;
using GenLauncherGO.UI.Shared.Errors;
using GenLauncherGO.UI.Shared.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace GenLauncherGO.UI.Features.Startup;

internal static class LauncherUiServiceCollectionExtensions
{
    public static IServiceCollection AddGenLauncherGoUi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ModificationImageSourceFactory>();
        services.AddSingleton<LaunchContentIntegrityCoordinator>();
        services.AddSingleton<LauncherPackageActivityService>();
        services.AddSingleton<LauncherLaunchCoordinator>();
        services.AddSingleton<LauncherLaunchReadinessCoordinator>();
        services.AddSingleton<LauncherTileActionService>();
        services.AddSingleton<LauncherExecutableSelectionService>();
        services.AddSingleton<LauncherGameSessionCoordinator>();
        services.AddSingleton<ILauncherFilePicker, AvaloniaLauncherFilePicker>();
        services.AddSingleton<LauncherManualImportCoordinator>();
        services.AddSingleton<LauncherModificationDownloadCoordinator>();
        services.AddSingleton<LauncherCloseGuard>();
        services.AddSingleton<LauncherWindowWorkflowCoordinator>();
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
        services.AddTransient<InitWindowViewModel>();
        services.AddTransient<InitWindow>();
        services.AddTransient<MainWindow>();

        return services;
    }
}
