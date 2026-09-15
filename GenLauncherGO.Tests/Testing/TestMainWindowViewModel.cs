using GenLauncherGO.Features.Launcher;
using GenLauncherGO.Features.Launching;
using GenLauncherGO.Features.Mods;
using GenLauncherGO.Features.Settings;
using GenLauncherGO.Features.Startup;
using GenLauncherGO.Features.Updating;
using GenLauncherGO.Shared.Localization;
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
            stringLocalizer ?? new FakeStringLocalizer();
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
