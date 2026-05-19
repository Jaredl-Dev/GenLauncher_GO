using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Launcher.ViewModels;
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Shared.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Owns the eleven collaborators the main window view model takes, so a test names only the ones it asserts on.
/// </summary>
internal static class TestMainWindowViewModel
{
    public static MainWindowViewModel Create(
        ILauncherContentCatalog? catalog = null,
        ILauncherPreferencesService? preferencesService = null,
        LauncherRuntimeContext? runtimeContext = null,
        IGameExecutableDiscoveryService? executableDiscovery = null,
        LauncherPackageActivityService? packageActivityService = null,
        LauncherLaunchCoordinator? launchCoordinator = null,
        ILauncherStringLocalizer? stringLocalizer = null)
    {
        ILauncherContentCatalog resolvedCatalog = catalog ?? new FakeLauncherContentCatalog();
        ILauncherPreferencesService resolvedPreferencesService =
            preferencesService ?? new RecordingLauncherPreferencesService(new LauncherPreferences());
        LauncherRuntimeContext resolvedRuntimeContext = runtimeContext ?? TestLauncherRuntimeContext.Create();
        ILauncherStringLocalizer resolvedStringLocalizer =
            stringLocalizer ?? FakeStringLocalizer.Create(TestLocalizedStrings.Launcher);
        LauncherPackageActivityService resolvedPackageActivityService =
            packageActivityService ?? new LauncherPackageActivityService();

        return new MainWindowViewModel(
            resolvedPreferencesService,
            new LauncherExecutableSelectionService(
                executableDiscovery ?? Substitute.For<IGameExecutableDiscoveryService>(),
                resolvedRuntimeContext,
                resolvedPreferencesService,
                resolvedStringLocalizer),
            resolvedCatalog,
            resolvedRuntimeContext,
            resolvedStringLocalizer,
            new ModificationImageSourceFactory(NullLogger<ModificationImageSourceFactory>.Instance),
            Substitute.For<IModificationImageFileService>(),
            resolvedPackageActivityService,
            NullLogger<ModificationViewModel>.Instance,
            launchCoordinator ?? TestLauncherLaunchCoordinator.Create(
                resolvedPackageActivityService,
                resolvedPreferencesService,
                resolvedCatalog,
                resolvedStringLocalizer),
            NullLogger<MainWindowViewModel>.Instance);
    }
}
