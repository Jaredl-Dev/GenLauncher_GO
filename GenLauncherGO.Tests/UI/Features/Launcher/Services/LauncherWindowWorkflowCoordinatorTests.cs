using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Exceptions;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Shell.Contracts;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Launcher.ViewModels;
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Features.Settings.Views;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Services;

[Collection("Avalonia")]
public sealed partial class LauncherWindowWorkflowCoordinatorTests
{
    [Fact]
    public void RetailClientRecommendation_ShowsOnceGloballyPersistsAndOpensDownloadPage()
    {
        StaTestRunner.Run(async () =>
        {
            var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences());
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowInfoActionAsync(
                    Arg.Any<LauncherInfoDialogRequest>(),
                    Arg.Any<string>(),
                    Arg.Any<Window?>())
                .Returns(true);
            ILauncherShellService shellService = Substitute.For<ILauncherShellService>();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                dialogService: dialogService,
                preferencesService: preferencesService,
                launcherShellService: shellService);
            Window owner = new();
            ExecutableOption retailClient = new(
                "Retail",
                "generals.exe",
                true,
                true,
                isRetail: true);

            await coordinator.ShowRetailClientRecommendationIfNeededAsync(retailClient, owner);
            await coordinator.ShowRetailClientRecommendationIfNeededAsync(retailClient, owner);

            await dialogService.Received(1).ShowInfoActionAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request.MainMessage == "GenPatcher recommendation" &&
                    request.DetailMessage ==
                    "It is strongly recommended to use GenPatcher when playing the retail game."),
                "Visit GenPatcher download page",
                owner);
            preferencesService.Current.Shared.HasShownRetailGenPatcherRecommendation.Should().BeTrue();
            shellService.Received(1).OpenUri("https://legi.cc/genpatcher/");
        });
    }

    [Fact]
    public void RetailClientRecommendation_WhenAlreadyShownOrClientIsNotRetail_DoesNothing()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherPreferencesService preferencesService = Substitute.For<ILauncherPreferencesService>();
            preferencesService.Current.Returns(new LauncherPreferences
            {
                Shared = new LauncherSharedPreferences
                {
                    HasShownRetailGenPatcherRecommendation = true
                }
            });
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                dialogService: dialogService,
                preferencesService: preferencesService);
            Window owner = new();

            await coordinator.ShowRetailClientRecommendationIfNeededAsync(
                new ExecutableOption("Community", "generalszh.exe", true, true),
                owner);
            await coordinator.ShowRetailClientRecommendationIfNeededAsync(
                new ExecutableOption("Retail", "generals.exe", true, true, isRetail: true),
                owner);

            await dialogService.DidNotReceive().ShowInfoActionAsync(
                Arg.Any<LauncherInfoDialogRequest>(),
                Arg.Any<string>(),
                Arg.Any<Window?>());
        });
    }

    [Fact]
    public void RetailClientRecommendation_WhenPersistenceFails_RemainsSuppressedForSession()
    {
        StaTestRunner.Run(async () =>
        {
            var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences())
            {
                UpdateFailure = new LauncherPreferencesPersistenceException(new IOException("locked"))
            };
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowInfoActionAsync(
                    Arg.Any<LauncherInfoDialogRequest>(),
                    Arg.Any<string>(),
                    Arg.Any<Window?>())
                .Returns(false);
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                dialogService: dialogService,
                preferencesService: preferencesService);
            Window owner = new();
            ExecutableOption retailClient = new(
                "Retail",
                "generals.exe",
                true,
                true,
                isRetail: true);

            await coordinator.ShowRetailClientRecommendationIfNeededAsync(retailClient, owner);
            await coordinator.ShowRetailClientRecommendationIfNeededAsync(retailClient, owner);

            await dialogService.Received(1).ShowInfoActionAsync(
                Arg.Any<LauncherInfoDialogRequest>(),
                Arg.Any<string>(),
                owner);
        });
    }

    [Fact]
    public void LinkActions_OpenTheirConfiguredUris()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherShellService shellService = Substitute.For<ILauncherShellService>();
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(
                launcherShellService: shellService);
            LauncherContent modification = CreateModification(
                "Shockwave",
                ModificationType.Mod,
                CreateVersion("Shockwave", ContentSourceKind.Manual),
                newsLink: "https://example.test/news",
                networkInfo: "https://example.test/network",
                modDbLink: "https://example.test/moddb",
                discordLink: "https://example.test/discord");
            ModificationViewModel viewModel = CreateTile(modification);

            coordinator.OpenTileLink(viewModel, LauncherTileLinkKind.ChangeLog);
            coordinator.OpenTileLink(viewModel, LauncherTileLinkKind.NetworkInfo);
            coordinator.OpenTileLink(viewModel, LauncherTileLinkKind.ModDb);
            coordinator.OpenTileLink(viewModel, LauncherTileLinkKind.Discord);

            shellService.Received(1).OpenUri("https://example.test/news");
            shellService.Received(1).OpenUri("https://example.test/network");
            shellService.Received(1).OpenUri("https://example.test/moddb");
            shellService.Received(1).OpenUri("https://example.test/discord");
        });
    }

    [Fact]
    public void OpenTileLink_SupportLink_ShowsThankYouAndOpensLink()
    {
        StaTestRunner.Run(() =>
        {
            ILauncherShellService shellService = Substitute.For<ILauncherShellService>();
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(
                launcherShellService: shellService);
            ModificationViewModel viewModel = CreateTile(CreateModification(
                "Shockwave",
                ModificationType.Mod,
                CreateVersion("Shockwave", ContentSourceKind.Manual),
                supportLink: "https://example.test/support"));

            coordinator.OpenTileLink(viewModel, LauncherTileLinkKind.Support);

            viewModel.ProgressMessage.Should().Be("Thank you");
            shellService.Received(1).OpenUri("https://example.test/support");
        });
    }

    [Fact]
    public void UpdateModificationAsyncForAdvertising_OpensAdvertisingLinkWithoutSelectingContent()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherShellService shellService = Substitute.For<ILauncherShellService>();
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(
                launcherShellService: shellService);
            ModificationViewModel viewModel = CreateTile(CreateModification(
                "Donate",
                ModificationType.Advertising,
                CreateVersion("Donate", ContentSourceKind.UnknownLegacy),
                "https://example.test/donate"));
            WorkflowFixture fixture = new();
            fixture.AddTile(viewModel);

            await UpdateModificationAsync(coordinator, fixture, viewModel);

            viewModel.ProgressMessage.Should().Be("Thank you");
            shellService.Received(1).OpenUri("https://example.test/donate");
            fixture.ViewModel.SelectedModifications.Should().BeEmpty();
        });
    }

    [Fact]
    public void ChangeVersionImageAsyncForManualContent_ReplacesImageAndRestoresControls()
    {
        StaTestRunner.Run(async () =>
        {
            IModificationImageFileService imageFileService = Substitute.For<IModificationImageFileService>();
            imageFileService.ReplaceImageAsync(
                    Arg.Any<ModificationImageReplacementRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(@"C:\Cache\Shockwave\1.0.png"));
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(
                filePicker: new StubLauncherFilePicker
                {
                    ModificationImageFileResult = @"C:\Pictures\custom.png"
                },
                modificationImageFileService: imageFileService);
            ModificationViewModel viewModel = CreateTile(CreateModification(
                "Shockwave",
                ModificationType.Mod,
                CreateVersion("Shockwave", ContentSourceKind.Manual)));
            WorkflowFixture fixture = new();
            List<bool> enabledStates = [];
            fixture.ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.MainControlsEnabled))
                {
                    enabledStates.Add(fixture.ViewModel.MainControlsEnabled);
                }
            };

            await coordinator.ChangeVersionImageAsync(
                fixture.Context,
                viewModel,
                CancellationToken.None);

            enabledStates.Should().Equal(false, true);
            fixture.ViewModel.MainControlsEnabled.Should().BeTrue();
            await imageFileService.Received(1).ReplaceImageAsync(
                Arg.Is<ModificationImageReplacementRequest>(request =>
                    request != null &&
                    request.ModificationName == "Shockwave" &&
                    request.ImageBaseName == "1.0" &&
                    request.SourceImagePath == @"C:\Pictures\custom.png"),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void ChangeVersionImageAsync_NonManualContent_DoesNothing()
    {
        StaTestRunner.Run(async () =>
        {
            IModificationImageFileService imageFileService = Substitute.For<IModificationImageFileService>();
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(
                filePicker: new StubLauncherFilePicker
                {
                    ModificationImageFileResult = @"C:\Pictures\custom.png"
                },
                modificationImageFileService: imageFileService);
            ModificationViewModel viewModel = CreateTile(CreateModification(
                "Shockwave",
                ModificationType.Mod,
                CreateVersion("Shockwave", ContentSourceKind.ManagedSingleFile)));
            WorkflowFixture fixture = new();
            List<bool> enabledStates = [];
            fixture.ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.MainControlsEnabled))
                {
                    enabledStates.Add(fixture.ViewModel.MainControlsEnabled);
                }
            };

            await coordinator.ChangeVersionImageAsync(
                fixture.Context,
                viewModel,
                CancellationToken.None);

            enabledStates.Should().BeEmpty();
            await imageFileService.DidNotReceive().ReplaceImageAsync(
                Arg.Any<ModificationImageReplacementRequest>(),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void AddRepositoryModificationAsyncWhenSelection_IsReturnedAddsModificationAndRestoresFocus()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowModificationSelectionAsync(
                    Arg.Any<IReadOnlyList<string>>(),
                    Arg.Any<Window?>())
                .Returns("Shockwave");
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherContentVersion repositoryVersion =
                CreateVersion("Shockwave", ContentSourceKind.ManagedSingleFile);
            catalog.DownloadHandler = (_, _) => Task.FromResult(repositoryVersion);
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(dialogService: dialogService);
            WorkflowFixture fixture = new(catalog);

            await coordinator.AddRepositoryModificationAsync(
                fixture.Context);

            fixture.ViewModel.ModsListSource.Should().ContainSingle()
                .Which.ContainerModification.Should().BeSameAs(catalog.Data.Modifications.Single());
            fixture.ViewModel.MainControlsEnabled.Should().BeTrue();
        });
    }

    [Fact]
    public void AddRepositoryModificationAsyncWhenPackageActivity_IsActiveShowsInfoAndRestoresFocus()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPackageActivityService packageActivityService = new();
            packageActivityService.TryBegin(
                    "Download",
                    out LauncherPackageActivityService.LauncherPackageActivityLease? lease)
                .Should()
                .BeTrue();
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(
                packageActivityService,
                dialogService);
            WorkflowFixture fixture = new(packageActivityService: packageActivityService);

            try
            {
                await coordinator.AddRepositoryModificationAsync(
                    fixture.Context);

                fixture.ViewModel.ModsListSource.Should().BeEmpty();
                fixture.ViewModel.MainControlsEnabled.Should().BeTrue();
                await dialogService.Received(1).ShowInfoAsync(
                    Arg.Is<LauncherInfoDialogRequest>(request =>
                        request != null &&
                        request.MainMessage == "Package activity" &&
                        request.DetailMessage == "Package activity details"),
                    fixture.Owner);
            }
            finally
            {
                lease?.Dispose();
            }
        });
    }

    /// <summary>
    ///     A paused transfer is stopped as far as the user is concerned, so it must not stand in the way of the rest
    ///     of the launcher. Adding a modification suspends it instead of refusing, and its progress survives.
    /// </summary>
    [Fact]
    public void AddRepositoryModificationAsyncWhenADownloadIsPaused_SuspendsItAndAddsTheModification()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPackageActivityService packageActivityService = new();
            Task<PackageDownloadResult> pausedDownload = TestPackageDownload.StartPaused(packageActivityService);
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowModificationSelectionAsync(
                    Arg.Any<IReadOnlyList<string>>(),
                    Arg.Any<Window?>())
                .Returns("Shockwave");
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherContentVersion repositoryVersion =
                CreateVersion("Shockwave", ContentSourceKind.ManagedSingleFile);
            catalog.DownloadHandler = (_, _) => Task.FromResult(repositoryVersion);
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(
                packageActivityService,
                dialogService);
            WorkflowFixture fixture = new(catalog, packageActivityService);

            await coordinator.AddRepositoryModificationAsync(fixture.Context);

            (await pausedDownload).Status.Should().Be(PackageDownloadStatus.Suspended);
            fixture.ViewModel.ModsListSource.Should().ContainSingle()
                .Which.ContainerModification.Should().BeSameAs(catalog.Data.Modifications.Single());
            await dialogService.DidNotReceive().ShowInfoAsync(
                Arg.Any<LauncherInfoDialogRequest>(),
                Arg.Any<Window?>());
        });
    }

    [Fact]
    public void ImportManualContentAsyncWhenADownloadIsPaused_SuspendsItAndRunsTheImport()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPackageActivityService packageActivityService = new();
            Task<PackageDownloadResult> pausedDownload = TestPackageDownload.StartPaused(packageActivityService);
            LauncherContent importedContent = CreateModification(
                "Manual Mod",
                ModificationType.Mod,
                CreateVersion("Manual Mod", ContentSourceKind.Manual));
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(
                packageActivityService,
                manualImportContent: importedContent);
            WorkflowFixture fixture = new(packageActivityService: packageActivityService);

            await coordinator.ImportManualContentAsync(
                fixture.Context,
                ModificationType.Mod,
                CancellationToken.None);

            (await pausedDownload).Status.Should().Be(PackageDownloadStatus.Suspended);
            fixture.ViewModel.ModsListSource.Should().ContainSingle();
        });
    }

    [Fact]
    public void ImportManualContentAsyncWhenImportSucceeds_DisablesUiAddsResultAndEnablesUi()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherContent importedContent = CreateModification(
                "Manual Mod",
                ModificationType.Mod,
                CreateVersion("Manual Mod", ContentSourceKind.Manual));
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(
                manualImportContent: importedContent);
            WorkflowFixture fixture = new();
            List<bool> enabledStates = [];
            fixture.ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.MainControlsEnabled))
                {
                    enabledStates.Add(fixture.ViewModel.MainControlsEnabled);
                }
            };

            await coordinator.ImportManualContentAsync(
                fixture.Context,
                ModificationType.Mod,
                CancellationToken.None);

            enabledStates.Should().Equal(false, true);
            fixture.ViewModel.MainControlsEnabled.Should().BeTrue();
            LauncherContent addedModification = fixture.ViewModel.ModsListSource.Should().ContainSingle()
                .Which.ContainerModification;
            addedModification.Name.Should().Be("Manual Mod");
            addedModification.ModificationType.Should().Be(ModificationType.Mod);
            addedModification.Versions.Should().ContainSingle()
                .Which.Version.Should().Be("1.0");
        });
    }

    [Fact]
    public void ImportManualContentAsyncWhenContentType_IsNotImportableThrows()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator();
            WorkflowFixture fixture = new();

            Func<Task> act = () => coordinator.ImportManualContentAsync(
                fixture.Context,
                ModificationType.Advertising,
                CancellationToken.None);

            await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
                .WithParameterName("kind");
            fixture.ViewModel.MainControlsEnabled.Should().BeTrue();
        });
    }

    [Fact]
    public void AddRepositoryModificationAsyncWhenSelection_IsCanceledRestoresFocusWithoutAdding()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowModificationSelectionAsync(
                    Arg.Any<IReadOnlyList<string>>(),
                    Arg.Any<Window?>())
                .Returns((string?)null);
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(dialogService: dialogService);
            WorkflowFixture fixture = new();

            await coordinator.AddRepositoryModificationAsync(
                fixture.Context);

            fixture.ViewModel.ModsListSource.Should().BeEmpty();
            fixture.Catalog.DownloadRequests.Should().BeEmpty();
        });
    }

    [Fact]
    public void OpenOptionsAsync_WhileALaunchedProcessIsRunning_ShowsAnErrorAndOpensNoSettingsWindow()
    {
        StaTestRunner.Run(async () =>
        {
            var operation = new ControllableGameProcessLaunchOperation("generalszh.exe");
            IGameProcessLauncher processLauncher = Substitute.For<IGameProcessLauncher>();
            processLauncher.StartAsync(Arg.Any<GameLaunchRequest>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IGameProcessLaunchOperation>(operation));
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherPackageActivityService packageActivityService = new();
            LauncherLaunchCoordinator launchCoordinator = TestLauncherLaunchCoordinator.Create(
                packageActivityService,
                processLauncher: processLauncher,
                dialogService: dialogService);
            int settingsWindowsCreated = 0;
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                packageActivityService,
                dialogService,
                launchCoordinator: launchCoordinator,
                launcherSettingsWindowFactory: () =>
                {
                    settingsWindowsCreated++;
                    return CreateAutoClosingSettingsWindow();
                });
            WorkflowFixture fixture = new(
                packageActivityService: packageActivityService,
                launchCoordinator: launchCoordinator);
            var processExposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            launchCoordinator.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(LauncherLaunchCoordinator.HasActiveProcess) &&
                    launchCoordinator.HasActiveProcess)
                {
                    processExposed.TrySetResult();
                }
            };
            Task<LauncherLaunchResult> launchTask = launchCoordinator.LaunchAsync(
                new LauncherLaunchRequest(
                    GameLaunchTargetKind.GameClient,
                    "generalszh.exe",
                    false,
                    Array.Empty<LauncherContentVersion>()),
                Array.Empty<ILaunchContentIntegrityProgressTarget>(),
                fixture.Owner,
                CancellationToken.None);
            await processExposed.Task.WaitAsync(TestTimeouts.Wait);

            await coordinator.OpenOptionsAsync(
                fixture.Context,
                CancellationToken.None);

            settingsWindowsCreated.Should().Be(0);
            await dialogService.Received(1).ShowErrorAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Game running" &&
                    request.DetailMessage == "Finish process"),
                fixture.Owner);

            operation.Complete(true);
            await launchTask.WaitAsync(TestTimeouts.Wait);
        });
    }

    [Fact]
    public void OpenOptionsAsync_WhenTheSettingsWindowClosesNormally_RefreshesTabsAndContainerData()
    {
        StaTestRunner.Run(async () =>
        {
            using ApplicationThemeScope themeScope = new();
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                catalog: catalog,
                launcherSettingsWindowFactory: CreateAutoClosingSettingsWindow);
            WorkflowFixture fixture = new(catalog);
            ModificationViewModel tile = AddSelectedShockwaveTile(fixture, catalog);
            AddNewerSelectedShockwaveVersionAndPatch(catalog);
            fixture.Owner.Show();

            await coordinator.OpenOptionsAsync(
                fixture.Context,
                CancellationToken.None);

            tile.SelectedVersion!.Version.Should().Be("2.0");
            fixture.ViewModel.PatchesListSource.Should().ContainSingle()
                .Which.ContainerModification.Name.Should().Be("Balance");
            fixture.Owner.Close();
        });
    }

    [Fact]
    public void OpenOptionsAsync_WhenTheSettingsWindowRequestsARestart_ClosesTheOwnerWithoutRefreshing()
    {
        StaTestRunner.Run(async () =>
        {
            using ApplicationThemeScope themeScope = new();
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                catalog: catalog,
                launcherSettingsWindowFactory: CreateRestartRequestingSettingsWindow);
            WorkflowFixture fixture = new(catalog);
            ModificationViewModel tile = AddSelectedShockwaveTile(fixture, catalog);
            AddNewerSelectedShockwaveVersionAndPatch(catalog);
            var ownerClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Owner.Closed += (_, _) => ownerClosed.TrySetResult();
            fixture.Owner.Show();

            await coordinator.OpenOptionsAsync(
                fixture.Context,
                CancellationToken.None);

            await ownerClosed.Task.WaitAsync(TestTimeouts.Wait);
            tile.SelectedVersion!.Version.Should().Be("1.0");
            fixture.ViewModel.PatchesListSource.Should().BeEmpty();
        });
    }

    [Fact]
    public void OpenOptionsAsync_WhenTheSettingsGameSwitchIsApplied_ReloadsTheLauncherBetweenControlLocks()
    {
        StaTestRunner.Run(async () =>
        {
            using ApplicationThemeScope themeScope = new();
            var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences
            {
                Installations = new LauncherInstallations
                {
                    Generals = GeneralsDirectory,
                    ZeroHour = ZeroHourDirectory
                },
                LastSelectedGame = SupportedGame.ZeroHour
            });
            FakeLauncherContentCatalog catalog = CreateCatalog();
            List<LauncherSettingsWindow> settingsWindows = [];
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                catalog: catalog,
                preferencesService: preferencesService,
                launcherSettingsWindowFactory: () => CaptureSettingsWindow(settingsWindows));
            WorkflowFixture fixture = new(catalog, preferencesService: preferencesService);
            ModificationViewModel tile = AddSelectedShockwaveTile(fixture, catalog);

            List<bool> enabledStates = await RunSettingsGameSwitchAsync(coordinator, fixture, settingsWindows);

            enabledStates.Should().Equal(false, true);
            preferencesService.Current.LastSelectedGame.Should().Be(SupportedGame.Generals);
            fixture.ViewModel.ModsListSource.Should().NotContain(tile);
        });
    }

    [Fact]
    public void OpenOptionsAsync_WhenTheSettingsGameSwitchIsRefused_KeepsTheLauncherOnTheActiveGame()
    {
        StaTestRunner.Run(async () =>
        {
            using ApplicationThemeScope themeScope = new();
            FakeLauncherContentCatalog catalog = CreateCatalog();
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            List<LauncherSettingsWindow> settingsWindows = [];
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                dialogService: dialogService,
                catalog: catalog,
                launcherSettingsWindowFactory: () => CaptureSettingsWindow(settingsWindows));
            WorkflowFixture fixture = new(catalog);
            ModificationViewModel tile = AddSelectedShockwaveTile(fixture, catalog);

            List<bool> enabledStates = await RunSettingsGameSwitchAsync(coordinator, fixture, settingsWindows);

            enabledStates.Should().Equal(false, true);
            fixture.ViewModel.ModsListSource.Should().ContainSingle().Which.Should().BeSameAs(tile);
            await dialogService.ReceivedWithAnyArgs(1).ShowErrorAsync(default!, default);
        });
    }

    [Fact]
    public void ImportManualContentAsyncWhenPackageActivity_IsActiveShowsInfoWithoutChangingUi()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPackageActivityService packageActivityService = new();
            packageActivityService.TryBegin(
                    "Download",
                    out LauncherPackageActivityService.LauncherPackageActivityLease? lease)
                .Should()
                .BeTrue();
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(
                packageActivityService,
                dialogService);
            WorkflowFixture fixture = new(packageActivityService: packageActivityService);
            List<bool> enabledStates = [];
            fixture.ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.MainControlsEnabled))
                {
                    enabledStates.Add(fixture.ViewModel.MainControlsEnabled);
                }
            };

            try
            {
                await coordinator.ImportManualContentAsync(
                    fixture.Context,
                    ModificationType.Mod,
                    CancellationToken.None);

                enabledStates.Should().BeEmpty();
                fixture.ViewModel.ModsListSource.Should().BeEmpty();
                fixture.ViewModel.MainControlsEnabled.Should().BeTrue();
                await dialogService.Received(1).ShowInfoAsync(
                    Arg.Is<LauncherInfoDialogRequest>(request =>
                        request != null &&
                        request.MainMessage == "Package activity" &&
                        request.DetailMessage == "Package activity details"),
                    fixture.Owner);
            }
            finally
            {
                lease?.Dispose();
            }
        });
    }
}
