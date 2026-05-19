using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Shared.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Testing;

internal static class TestLauncherLaunchCoordinator
{
    public static LauncherLaunchCoordinator Create(
        LauncherPackageActivityService? packageActivityService = null,
        ILauncherPreferencesService? preferencesService = null,
        ILauncherContentCatalog? catalog = null,
        ILauncherStringLocalizer? stringLocalizer = null,
        ILaunchPreparationService? preparationService = null,
        IGameProcessLauncher? processLauncher = null,
        ILauncherDialogService? dialogService = null,
        ILaunchContentIntegrityResolutionService? integrityResolutionService = null,
        LauncherRuntimePathContext? runtimePaths = null)
    {
        LauncherPackageActivityService resolvedPackageActivityService =
            packageActivityService ?? new LauncherPackageActivityService();
        ILauncherPreferencesService resolvedPreferencesService =
            preferencesService ?? new RecordingLauncherPreferencesService(new LauncherPreferences());
        ILauncherStringLocalizer resolvedStringLocalizer =
            stringLocalizer ?? FakeStringLocalizer.Create(TestLocalizedStrings.Launch);
        ILauncherDialogService resolvedDialogService =
            dialogService ?? Substitute.For<ILauncherDialogService>();
        ILaunchPreparationService resolvedPreparationService =
            preparationService ?? CreateSuccessfulPreparationService();
        IGameProcessLauncher resolvedProcessLauncher =
            processLauncher ?? CreateSuccessfulProcessLauncher();
        LauncherRuntimePathContext resolvedRuntimePaths =
            runtimePaths ?? TestLauncherPaths.CreateRuntimePathContext(TestLauncherPaths.Create());

        return new LauncherLaunchCoordinator(
            resolvedPreferencesService,
            resolvedPreparationService,
            resolvedProcessLauncher,
            TestLaunchContentIntegrityCoordinator.Create(
                integrityResolutionService,
                catalog,
                resolvedRuntimePaths,
                resolvedPackageActivityService,
                resolvedDialogService,
                resolvedStringLocalizer),
            resolvedPackageActivityService,
            resolvedRuntimePaths,
            resolvedStringLocalizer,
            resolvedDialogService,
            NullLogger<LauncherLaunchCoordinator>.Instance);
    }

    private static ILaunchPreparationService CreateSuccessfulPreparationService()
    {
        ILaunchPreparationService preparationService = Substitute.For<ILaunchPreparationService>();
        preparationService.Prepare(
                Arg.Any<LaunchPreparationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        preparationService.Cleanup(
                Arg.Any<LauncherPaths>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        return preparationService;
    }

    private static IGameProcessLauncher CreateSuccessfulProcessLauncher()
    {
        IGameProcessLauncher processLauncher = Substitute.For<IGameProcessLauncher>();
        processLauncher.StartAsync(
                Arg.Any<GameLaunchRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IGameProcessLaunchOperation>(
                new CompletedGameProcessLaunchOperation(true, "generals.exe")));
        return processLauncher;
    }
}
