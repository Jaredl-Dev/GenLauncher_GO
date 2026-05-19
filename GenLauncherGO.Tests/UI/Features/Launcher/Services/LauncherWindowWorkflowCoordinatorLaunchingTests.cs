using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Launcher.ViewModels;
using GenLauncherGO.UI.Features.Mods;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Services;

public sealed partial class LauncherWindowWorkflowCoordinatorTests
{
    [Theory]
    [InlineData(GameLaunchTargetKind.GameClient)]
    [InlineData(GameLaunchTargetKind.WorldBuilder)]
    public void LaunchAsyncWhenExecutable_IsUnavailableShowsErrorAndDoesNotLaunch(
        GameLaunchTargetKind targetKind)
    {
        StaTestRunner.Run(async () =>
        {
            IGameExecutableDiscoveryService executableDiscovery = Substitute.For<IGameExecutableDiscoveryService>();
            executableDiscovery.IsExecutableAvailable(Arg.Any<string?>()).Returns(false);
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            IGameProcessLauncher processLauncher = Substitute.For<IGameProcessLauncher>();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                dialogService: dialogService,
                executableDiscovery: executableDiscovery,
                gameProcessLauncher: processLauncher);
            WorkflowFixture fixture = new();
            var unavailableExecutable = new ExecutableOption(
                "Generals",
                "generals.exe",
                false,
                true);
            if (targetKind == GameLaunchTargetKind.GameClient)
            {
                fixture.ViewModel.SelectedGameClientOption = unavailableExecutable;
            }
            else
            {
                fixture.ViewModel.SelectedWorldBuilderOption = unavailableExecutable;
            }

            await coordinator.LaunchAsync(
                targetKind,
                fixture.Context,
                CancellationToken.None);

            await dialogService.Received(1).ShowErrorAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Launch aborted" &&
                    request.DetailMessage == "Executable unavailable"),
                fixture.Owner);
            await processLauncher.DidNotReceive().StartAsync(
                Arg.Any<GameLaunchRequest>(),
                Arg.Any<CancellationToken>());
            fixture.ViewModel.MainControlsEnabled.Should().BeTrue();
        });
    }

    [Fact]
    public void LaunchAsyncForGameWhenProcessSucceeds_EnablesSelectedModificationSupport()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherContentVersion selectedVersion = CreateVersion("Shockwave", ContentSourceKind.Manual);
            FakeLauncherContentCatalog catalog = CreateCatalog();
            catalog.Data.AddOrUpdate(selectedVersion);
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(catalog: catalog);
            WorkflowFixture fixture = new(catalog);
            ModificationViewModel selectedTile = CreateTile(catalog.Data.GetSelectedMod()!);
            fixture.AddTile(selectedTile);
            selectedTile.IsSelected = true;
            fixture.ViewModel.SelectedGameClientOption = new ExecutableOption(
                "Generals",
                "generals.exe",
                true,
                true);

            await coordinator.LaunchAsync(
                GameLaunchTargetKind.GameClient,
                fixture.Context,
                CancellationToken.None);

            selectedTile.SupportButtonBlinking.Should().BeTrue();
            fixture.ViewModel.MainControlsEnabled.Should().BeTrue();
        });
    }

    [Fact]
    public void LaunchAsyncForWorldBuilder_RunsSelectedWorkflowThroughProcessBoundary()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPreferences persistedPreferences = new()
            {
                Games = new LauncherGamePreferencesSet
                {
                    ZeroHour = new LauncherGamePreferences
                    {
                        WorldBuilderArguments = "-map \"Tournament Desert\""
                    }
                }
            };
            var preferencesService = new RecordingLauncherPreferencesService(persistedPreferences);

            LauncherContentVersion selectedVersion = CreateVersion("Shockwave", ContentSourceKind.Manual);
            FakeLauncherContentCatalog catalog = CreateCatalog();
            catalog.Data.AddOrUpdate(selectedVersion);

            LaunchPreparationRequest? preparationRequest = null;
            ILaunchPreparationService preparationService = Substitute.For<ILaunchPreparationService>();
            preparationService.Prepare(
                    Arg.Any<LaunchPreparationRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    preparationRequest = call.ArgAt<LaunchPreparationRequest>(0);
                    return true;
                });
            preparationService.Cleanup(
                    Arg.Any<LauncherPaths>(),
                    Arg.Any<CancellationToken>())
                .Returns(true);

            GameLaunchRequest? processRequest = null;
            IGameProcessLauncher processLauncher = Substitute.For<IGameProcessLauncher>();
            processLauncher.StartAsync(
                    Arg.Any<GameLaunchRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    processRequest = call.ArgAt<GameLaunchRequest>(0);
                    return Task.FromResult<IGameProcessLaunchOperation>(
                        new CompletedGameProcessLaunchOperation(
                            true,
                            processRequest.ExecutableName));
                });

            LauncherPackageActivityService packageActivityService = new();
            ILauncherDialogService launchDialogService = Substitute.For<ILauncherDialogService>();
            LauncherLaunchCoordinator launchCoordinator = TestLauncherLaunchCoordinator.Create(
                packageActivityService,
                preferencesService,
                catalog,
                preparationService: preparationService,
                processLauncher: processLauncher,
                dialogService: launchDialogService);
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                packageActivityService,
                catalog: catalog,
                preferencesService: preferencesService,
                launchPreparationService: preparationService,
                gameProcessLauncher: processLauncher,
                launchCoordinator: launchCoordinator);
            WorkflowFixture fixture = new(
                catalog,
                packageActivityService,
                preferencesService,
                launchCoordinator);
            ModificationViewModel selectedTile = CreateTile(catalog.Data.GetSelectedMod()!);
            fixture.AddTile(selectedTile);
            selectedTile.IsSelected = true;
            fixture.ViewModel.SelectedWorldBuilderOption = new ExecutableOption(
                "World Builder",
                "worldbuilderzh.exe",
                true,
                true);
            List<bool> enabledStates = [];
            fixture.ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.MainControlsEnabled))
                {
                    enabledStates.Add(fixture.ViewModel.MainControlsEnabled);
                }
            };

            await coordinator.LaunchAsync(
                GameLaunchTargetKind.WorldBuilder,
                fixture.Context,
                CancellationToken.None);

            preferencesService.Current.Games.ZeroHour.SelectedWorldBuilder.Should().Be("worldbuilderzh.exe");
            preparationRequest.Should().NotBeNull();
            preparationRequest!.Versions.Should().ContainSingle().Which.Should().BeSameAs(selectedVersion);
            preparationRequest.DisableBaseGameScriptFiles.Should().BeTrue();
            processRequest.Should().BeEquivalentTo(GameLaunchRequest.ForWorldBuilder(
                TestLauncherPaths.Create().GameDirectory,
                "worldbuilderzh.exe",
                "-map \"Tournament Desert\""));
            enabledStates.Should().Equal(false, true);
            fixture.ViewModel.MainControlsEnabled.Should().BeTrue();
            fixture.ViewModel.IsRunningProcessOverlayVisible.Should().BeFalse();
            fixture.ViewModel.RunningProcessStatusText.Should().BeEmpty();
            preparationService.Received(1).Cleanup(
                Arg.Any<LauncherPaths>(),
                Arg.Any<CancellationToken>());
        });
    }
}
