using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Launching;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Exceptions;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Launcher.ViewModels;
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Features.Startup;

namespace GenLauncherGO.Tests.UI.Features.Launcher.ViewModels;

[Collection("Avalonia")]
public sealed class MainWindowViewModelTests
{
    [Fact]
    public void SetMainControlsEnabled_WhenDisabled_UpdatesComputedControlState()
    {
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create();

        viewModel.SetMainControlsEnabled(false);

        viewModel.MainControlsEnabled.Should().BeFalse();
        viewModel.StartGameButtonEnabled.Should().BeFalse();
        viewModel.WorldBuilderButtonEnabled.Should().BeFalse();
        viewModel.IsLoadingIndicatorVisible.Should().BeTrue();
    }

    [Fact]
    public void LaunchState_UpdatesBindableBusyAndProcessOverlayProperties()
    {
        StaTestRunner.Run(async () =>
        {
            var preparationService = new FakeLaunchPreparationService();
            var operation = new ControllableGameProcessLaunchOperation("generalsonlinezh.exe");
            LauncherLaunchCoordinator launchCoordinator = TestLauncherLaunchCoordinator.Create(
                preparationService: preparationService,
                processLauncher: CreateProcessLauncher(operation));
            MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
                launchCoordinator: launchCoordinator);
            Task processTracked = WaitForActiveProcessAsync(launchCoordinator);
            preparationService.Pause();

            Task<LauncherLaunchResult> launchTask = StartGameClientLaunchAsync(launchCoordinator);

            await preparationService.PrepareStarted.Task.WaitAsync(TestTimeouts.Wait);
            viewModel.MainControlsEnabled.Should().BeFalse();
            viewModel.IsLoadingIndicatorVisible.Should().BeTrue();

            preparationService.Resume();
            await processTracked.WaitAsync(TestTimeouts.Wait);

            viewModel.MainControlsEnabled.Should().BeFalse();
            viewModel.IsRunningProcessOverlayVisible.Should().BeTrue();
            viewModel.RunningProcessStatusText.Should().Be("Running generalsonlinezh.exe");

            operation.RaiseCurrentExecutableNameChanged("generalszh.exe");
            viewModel.RunningProcessStatusText.Should().Be("Running generalszh.exe");

            operation.RaiseCurrentExecutableNameChanged(" ");
            viewModel.RunningProcessStatusText.Should().Be("Running Unknown process");

            operation.Complete(true);
            (await launchTask).ProcessSucceeded.Should().BeTrue();
            viewModel.MainControlsEnabled.Should().BeTrue();
            viewModel.IsRunningProcessOverlayVisible.Should().BeFalse();
            viewModel.RunningProcessStatusText.Should().BeEmpty();
        });
    }

    [Fact]
    public void LaunchState_WhenTheLauncherHidesAfterGameStart_HidesTheRunningProcessOverlay()
    {
        StaTestRunner.Run(async () =>
        {
            var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences
            {
                Shared = new LauncherSharedPreferences { HideLauncherAfterGameStart = true }
            });
            var operation = new ControllableGameProcessLaunchOperation("generalszh.exe");
            LauncherLaunchCoordinator launchCoordinator = TestLauncherLaunchCoordinator.Create(
                preferencesService: preferencesService,
                processLauncher: CreateProcessLauncher(operation));
            MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
                preferencesService: preferencesService,
                launchCoordinator: launchCoordinator);
            Task processTracked = WaitForActiveProcessAsync(launchCoordinator);

            Task<LauncherLaunchResult> launchTask = StartGameClientLaunchAsync(launchCoordinator);
            await processTracked.WaitAsync(TestTimeouts.Wait);

            viewModel.ShouldHideLauncherWindow.Should().BeTrue();
            viewModel.IsRunningProcessOverlayVisible.Should().BeFalse();
            viewModel.RunningProcessStatusText.Should().BeEmpty();

            operation.Complete(true);
            await launchTask;

            viewModel.ShouldHideLauncherWindow.Should().BeFalse();
        });
    }

    [Fact]
    public void Dispose_UnsubscribesFromPreferenceAndLaunchCoordinatorChanges()
    {
        StaTestRunner.Run(async () =>
        {
            var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences());
            LauncherLaunchCoordinator launchCoordinator = TestLauncherLaunchCoordinator.Create(
                preferencesService: preferencesService);
            MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
                preferencesService: preferencesService,
                launchCoordinator: launchCoordinator);
            var changedProperties = new List<string?>();

            viewModel.Dispose();
            viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);
            LauncherPreferences current = preferencesService.Current;
            preferencesService.Update(current with
            {
                Games = current.Games.With(
                    SupportedGame.ZeroHour,
                    current.Games.ZeroHour with
                    {
                        GameArguments = LauncherGameArgumentService.WindowedArgument
                    })
            });
            await StartGameClientLaunchAsync(launchCoordinator);

            viewModel.WindowedModeButtonText.Should().BeEmpty();
            changedProperties.Should().BeEmpty();
        });
    }

    [Fact]
    public void ToggleGameArgument_WhenWindowedMissing_AddsArgumentAndRefreshesButtonText()
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences());
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            preferencesService: preferencesService);

        viewModel.ToggleGameArgument(LauncherGameArgumentService.WindowedArgument);

        preferencesService.Updates.Should().ContainSingle();
        preferencesService.Current.Games.ZeroHour.GameArguments
            .Should()
            .Be(LauncherGameArgumentService.WindowedArgument);
        viewModel.WindowedModeButtonText.Should().Be("Change to full screen");
    }

    [Fact]
    public void SelectedGameClientOption_WhenSet_UpdatesPreferences()
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences());
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            preferencesService: preferencesService);
        ExecutableOption option = new(
            "GenLauncherGO client",
            "generals.exe",
            true,
            true);

        viewModel.SelectedGameClientOption = option;

        preferencesService.Current.Games.ZeroHour.SelectedGameClient.Should().Be("generals.exe");
    }

    [Theory]
    [InlineData(
        SupportedGame.Generals,
        "GenLauncherGO - Generals",
        SupportedGame.ZeroHour,
        "GenLauncherGO - Zero Hour",
        "generalszh.exe")]
    [InlineData(
        SupportedGame.ZeroHour,
        "GenLauncherGO - Zero Hour",
        SupportedGame.Generals,
        "GenLauncherGO - Generals",
        "generals.exe")]
    public void ReloadForActiveGame_AfterRuntimeSwitch_UpdatesWindowTitleAndGameClientSelection(
        SupportedGame initialGame,
        string initialTitle,
        SupportedGame targetGame,
        string targetTitle,
        string targetGameClient)
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences
        {
            Games = new LauncherGamePreferencesSet
            {
                Generals = new LauncherGamePreferences { SelectedGameClient = "generals.exe" },
                ZeroHour = new LauncherGamePreferences { SelectedGameClient = "generalszh.exe" }
            }
        });
        IGameExecutableDiscoveryService executableDiscovery = Substitute.For<IGameExecutableDiscoveryService>();
        executableDiscovery.GetGameClients().Returns(new[]
        {
            new BuiltInExecutable("generals.exe", true),
            new BuiltInExecutable("generalszh.exe", true)
        });
        LauncherRuntimeContext runtimeContext = TestLauncherRuntimeContext.Create(
            TestLauncherPaths.Create(game: initialGame));
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            preferencesService: preferencesService,
            runtimeContext: runtimeContext,
            executableDiscovery: executableDiscovery);

        viewModel.Initialize();
        viewModel.WindowTitle.Should().Be(initialTitle);

        runtimeContext.RuntimePaths.SwitchActive(TestLauncherPaths.Create(game: targetGame));
        viewModel.ReloadForActiveGame();

        viewModel.WindowTitle.Should().Be(targetTitle);
        viewModel.SelectedGameClientOption!.ExecutableName.Should().Be(targetGameClient);
        preferencesService.Current.Games.Get(targetGame).SelectedGameClient.Should().Be(targetGameClient);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedWorldBuilderOption_PersistsSelectionRegardlessOfAvailability(bool isAvailable)
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences());
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            preferencesService: preferencesService);

        viewModel.SelectedWorldBuilderOption = new ExecutableOption(
            "World Builder",
            "worldbuilder.exe",
            isAvailable,
            isAvailable);

        preferencesService.Current.Games.ZeroHour.SelectedWorldBuilder
            .Should().Be("worldbuilder.exe");
    }

    [Fact]
    public void SaveModsListVerticalOffset_PersistsPositionForActiveGame()
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences());
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            TestLauncherContent.Catalog().WithMod("Shockwave").Build(),
            preferencesService);
        viewModel.RefreshModsList();

        viewModel.SaveModsListVerticalOffset(123.5);

        preferencesService.Current.Games.ZeroHour.ModsListVerticalOffset.Should().Be(123.5);
        preferencesService.Current.Games.Generals.ModsListVerticalOffset.Should().Be(0);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-50)]
    public void SaveModsListVerticalOffset_WhenPositionIsNotUsable_ResetsPositionToTop(double verticalOffset)
    {
        var preferencesService = new RecordingLauncherPreferencesService(
            CreatePreferences(new LauncherGamePreferences { ModsListVerticalOffset = 300 }));
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            TestLauncherContent.Catalog().WithMod("Shockwave").Build(),
            preferencesService);
        viewModel.RefreshModsList();

        viewModel.SaveModsListVerticalOffset(verticalOffset);

        preferencesService.Current.Games.ZeroHour.ModsListVerticalOffset.Should().Be(0);
    }

    [Fact]
    public void SaveModsListVerticalOffset_WhenPositionCannotBePersisted_KeepsShutdownUnblocked()
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences())
        {
            UpdateFailure = new LauncherPreferencesPersistenceException(new IOException("locked"))
        };
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            TestLauncherContent.Catalog().WithMod("Shockwave").Build(),
            preferencesService);
        viewModel.RefreshModsList();

        Action act = () => viewModel.SaveModsListVerticalOffset(123.5);

        act.Should().NotThrow();
        preferencesService.Current.Games.ZeroHour.ModsListVerticalOffset.Should().Be(0);
    }

    [Fact]
    public void SaveModsListVerticalOffset_WhenModsWereDeleted_ResetsPositionToTop()
    {
        var preferencesService = new RecordingLauncherPreferencesService(
            CreatePreferences(new LauncherGamePreferences { ModsListVerticalOffset = 300 }));
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            preferencesService: preferencesService);

        viewModel.ModsListVerticalOffset.Should().Be(0);
        viewModel.SaveModsListVerticalOffset(300);

        preferencesService.Current.Games.ZeroHour.ModsListVerticalOffset.Should().Be(0);
    }

    [Fact]
    public void Initialize_WhenPersistedLaunchCountIsNegative_RefreshesStateAndUsesTheAdvertisingThreshold()
    {
        var preferencesService = new RecordingLauncherPreferencesService(
            CreatePreferences(new LauncherGamePreferences { LaunchesCount = -1 }));
        IGameExecutableDiscoveryService executableDiscovery = Substitute.For<IGameExecutableDiscoveryService>();
        executableDiscovery.GetGameClients().Returns(new[]
        {
            new BuiltInExecutable("generals.exe", true)
        });
        executableDiscovery.GetWorldBuilders().Returns(new[]
        {
            new BuiltInExecutable("worldbuilder.exe", true)
        });
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            TestLauncherContent.Catalog().WithMod("Shockwave").Build(),
            preferencesService,
            executableDiscovery: executableDiscovery);

        viewModel.Initialize();

        viewModel.SupportedGameClients.Should().ContainSingle();
        viewModel.SupportedWorldBuilders.Should().ContainSingle();
        viewModel.ModsListSource.Should().ContainSingle();
        viewModel.CurrentLauncherVersionText.Should()
            .Be($"Current version: {TestLauncherRuntimeContext.LauncherVersion}");
        preferencesService.Current.Games.ZeroHour.LaunchesCount.Should()
            .Be(LauncherApplicationDefaults.LaunchesCountForUpdateAdvertising + 1);
    }

    [Fact]
    public void Initialize_WhenLaunchCountExceedsAdvertisingThreshold_ResetsCounterAndKeepsAdvertisingVisible()
    {
        var preferencesService = new RecordingLauncherPreferencesService(
            CreatePreferences(new LauncherGamePreferences
            {
                LaunchesCount = LauncherApplicationDefaults.LaunchesCountForUpdateAdvertising + 1
            }));
        LauncherContentVersion advertising = TestLauncherContent.Version(
            "Featured",
            type: ModificationType.Advertising);
        FakeLauncherContentCatalog catalog = TestLauncherContent.Catalog()
            .WithMod("First")
            .WithMod("Second")
            .WithMod("Third")
            .Build();
        catalog.Advertising = advertising;
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            catalog,
            preferencesService,
            TestLauncherRuntimeContext.Create(connected: true));

        viewModel.Initialize();

        preferencesService.Current.Games.ZeroHour.LaunchesCount.Should().Be(1);
        viewModel.ModsListSource[0].ContainerModification.LatestVersion.Should().BeSameAs(advertising);
        catalog.Data.Modifications.Should().HaveCount(3);
        catalog.UninstalledVersions.Should().BeEmpty();
    }

    [Fact]
    public void Initialize_WhenLaunchCounterCannotBePersisted_ContinuesWithPreviousCount()
    {
        var preferencesService = new RecordingLauncherPreferencesService(
            CreatePreferences(new LauncherGamePreferences { LaunchesCount = 4 }))
        {
            UpdateFailure = new LauncherPreferencesPersistenceException(new IOException("locked"))
        };
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            preferencesService: preferencesService);

        Action act = () => viewModel.Initialize();

        act.Should().NotThrow();
        preferencesService.Current.Games.ZeroHour.LaunchesCount.Should().Be(4);
    }

    [Fact]
    public void RefreshGameClientOptions_WhenClientsExist_SelectsPreferredClientAndEnablesControls()
    {
        var preferencesService = new RecordingLauncherPreferencesService(
            CreatePreferences(new LauncherGamePreferences { SelectedGameClient = "genlauncher.exe" }));
        IGameExecutableDiscoveryService executableDiscovery = Substitute.For<IGameExecutableDiscoveryService>();
        executableDiscovery.GetGameClients().Returns(new[]
        {
            new BuiltInExecutable("generals.exe", true),
            new BuiltInExecutable("genlauncher.exe", true)
        });
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            preferencesService: preferencesService,
            executableDiscovery: executableDiscovery);

        viewModel.RefreshGameClientOptions();

        viewModel.SupportedGameClients.Select(option => option.ExecutableName)
            .Should()
            .Equal("generals.exe", "genlauncher.exe");
        viewModel.SelectedGameClientOption!.ExecutableName.Should().Be("genlauncher.exe");
        viewModel.GameClientSelectorEnabled.Should().BeTrue();
        viewModel.StartGameButtonEnabled.Should().BeTrue();
    }

    [Fact]
    public void RefreshGameClientOptions_SelectedFileDeleted_KeepsLaunchActionEnabledForFeedback()
    {
        bool isAvailable = true;
        var preferencesService = new RecordingLauncherPreferencesService(
            CreatePreferences(new LauncherGamePreferences { SelectedGameClient = "custom.exe" }));
        IGameExecutableDiscoveryService executableDiscovery = Substitute.For<IGameExecutableDiscoveryService>();
        executableDiscovery.GetGameClients().Returns(_ => new[]
        {
            new BuiltInExecutable("custom.exe", isAvailable)
        });
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            preferencesService: preferencesService,
            executableDiscovery: executableDiscovery);

        viewModel.RefreshGameClientOptions();
        viewModel.StartGameButtonEnabled.Should().BeTrue();

        isAvailable = false;
        viewModel.RefreshGameClientOptions();
        viewModel.SelectedGameClientOption!.ExecutableName.Should().Be("custom.exe");
        viewModel.SelectedGameClientOption.IsUnavailable.Should().BeTrue();
        viewModel.GameClientSelectorEnabled.Should().BeTrue();
        viewModel.StartGameButtonEnabled.Should().BeTrue();

        isAvailable = true;
        viewModel.RefreshGameClientOptions();
        viewModel.SelectedGameClientOption!.IsUnavailable.Should().BeFalse();
    }

    [Fact]
    public void RefreshWorldBuilderOptions_SelectedFileMissing_KeepsLaunchActionEnabledForFeedback()
    {
        IGameExecutableDiscoveryService executableDiscovery = Substitute.For<IGameExecutableDiscoveryService>();
        executableDiscovery.GetWorldBuilders().Returns(new[]
        {
            new BuiltInExecutable("custom-world-builder.exe", false)
        });
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            executableDiscovery: executableDiscovery);

        viewModel.RefreshWorldBuilderOptions();

        viewModel.SelectedWorldBuilderOption.Should().NotBeNull();
        viewModel.SelectedWorldBuilderOption!.IsUnavailable.Should().BeTrue();
        viewModel.WorldBuilderSelectorEnabled.Should().BeTrue();
        viewModel.WorldBuilderButtonEnabled.Should().BeTrue();
    }

    [Fact]
    public void RefreshWorldBuilderOptions_WhenNoneExist_DisablesControls()
    {
        IGameExecutableDiscoveryService executableDiscovery = Substitute.For<IGameExecutableDiscoveryService>();
        executableDiscovery.GetWorldBuilders()
            .Returns(Array.Empty<BuiltInExecutable>());
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            executableDiscovery: executableDiscovery);

        viewModel.RefreshWorldBuilderOptions();

        viewModel.SupportedWorldBuilders.Should().BeEmpty();
        viewModel.SelectedWorldBuilderOption.Should().BeNull();
        viewModel.WorldBuilderSelectorEnabled.Should().BeFalse();
        viewModel.WorldBuilderButtonEnabled.Should().BeFalse();
    }

    [Fact]
    public void UpdateAddonAndPatchTabLabels_ProjectsTheSoleActiveChildDownload()
    {
        var packageActivityService = new LauncherPackageActivityService();
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            TestLauncherContent.Catalog()
                .WithPatch(LauncherContentKey.OriginalGame.Name, "Patch")
                .WithAddon(LauncherContentKey.OriginalGame.Name, "Addon One")
                .WithAddon(LauncherContentKey.OriginalGame.Name, "Addon Two")
                .Build(),
            packageActivityService: packageActivityService);

        viewModel.RefreshPatchesList();
        viewModel.RefreshAddonsList();

        BeginActiveDownload(packageActivityService, viewModel.AddonsListSource[0]);
        viewModel.AddonsListSource[0].ReportPackageProgress("Downloading", 20);

        viewModel.UpdateAddonAndPatchTabLabels();

        viewModel.IsPatchesTabDownloadIndicatorVisible.Should().BeFalse();
        viewModel.IsAddonsTabDownloadIndicatorVisible.Should().BeTrue();
    }

    [Fact]
    public async Task ShowContentViewAsync_WhenOriginalGameContentIsRequired_LoadsContentAndShowsPatchViewAsync()
    {
        var catalog = new FakeLauncherContentCatalog();
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(catalog);
        bool controlsEnabledDuringFetch = true;
        bool loadingIndicatorVisibleDuringFetch = false;
        catalog.OriginalGameChildManifestReadHandler = _ =>
        {
            controlsEnabledDuringFetch = viewModel.MainControlsEnabled;
            loadingIndicatorVisibleDuringFetch = viewModel.IsLoadingIndicatorVisible;
            return Task.CompletedTask;
        };

        await viewModel.ShowContentViewAsync(LauncherContentViewKind.Patches);

        catalog.OriginalGameChildManifestReadCount.Should().Be(1);
        controlsEnabledDuringFetch.Should().BeFalse();
        loadingIndicatorVisibleDuringFetch.Should().BeTrue();
        viewModel.MainControlsEnabled.Should().BeTrue();
        viewModel.ActiveContentView.Should().Be(LauncherContentViewKind.Patches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShowContentViewAsync_WhenModificationIsSelected_DoesNotReloadOriginalGameContentAsync(
        bool showAddons)
    {
        LauncherContentViewKind viewKind = showAddons
            ? LauncherContentViewKind.Addons
            : LauncherContentViewKind.Patches;
        FakeLauncherContentCatalog catalog = TestLauncherContent.Catalog()
            .WithMod("Shockwave")
            .Selected("Shockwave")
            .Build();
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(catalog);
        viewModel.RefreshModsList();
        viewModel.ModsListSource[0].IsSelected = true;

        await viewModel.ShowContentViewAsync(viewKind);

        catalog.OriginalGameChildManifestReadCount.Should().Be(0);
        viewModel.ActiveContentView.Should().Be(viewKind);
    }

    [Fact]
    public void ReloadForActiveGame_RefreshesRepositoryAddVisibility()
    {
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create();
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.ReloadForActiveGame();

        changedProperties.Should().Contain(nameof(MainWindowViewModel.CanAddRepositoryModification));
        changedProperties.Should().Contain(nameof(MainWindowViewModel.IsAddModButtonVisible));
    }

    [Fact]
    public void ReloadForActiveGame_DoesNotReplayStartupAddModPromptForAnotherEmptyGame()
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences());
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            preferencesService: preferencesService);
        viewModel.Initialize(false);
        viewModel.AddModButtonBlinking.Should().BeTrue();

        viewModel.ReloadForActiveGame();

        viewModel.ModsListSource.Should().BeEmpty();
        viewModel.AddModButtonBlinking.Should().BeFalse();
        preferencesService.Updates.Should().NotContain(preferences =>
            preferences.Games.ZeroHour.LaunchesCount != 0 ||
            preferences.Games.Generals.LaunchesCount != 0);
    }

    [Fact]
    public void ReloadForActiveGame_DoesNotStartAddModPromptWhenStartupGameHadMods()
    {
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            TestLauncherContent.Catalog().WithMod("Shockwave").Build());
        viewModel.Initialize(false);
        viewModel.AddModButtonBlinking.Should().BeFalse();

        viewModel.ReloadForActiveGame();

        viewModel.AddModButtonBlinking.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepositoryAddVisibility_TracksConnectivityAndActiveContentViewAsync(bool connected)
    {
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            runtimeContext: TestLauncherRuntimeContext.Create(connected: connected));
        viewModel.Initialize();

        viewModel.IsAddModButtonVisible.Should().Be(connected);

        await viewModel.ShowContentViewAsync(LauncherContentViewKind.Patches);
        bool visibleOnPatches = viewModel.IsAddModButtonVisible;
        await viewModel.ShowContentViewAsync(LauncherContentViewKind.Modifications);

        visibleOnPatches.Should().BeFalse();
        viewModel.IsAddModButtonVisible.Should().Be(connected);
    }

    [Fact]
    public async Task ShowContentViewAsync_WhenFirstCatalogLoadFails_RestoresViewAndAllowsRetryAsync()
    {
        int readCount = 0;
        var catalog = new FakeLauncherContentCatalog
        {
            OriginalGameChildManifestReadHandler = _ =>
            {
                readCount++;
                return readCount == 1
                    ? Task.FromException(new IOException("catalog unavailable"))
                    : Task.CompletedTask;
            }
        };
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(catalog);

        Func<Task> firstAttempt = () => viewModel.ShowContentViewAsync(LauncherContentViewKind.Patches);
        await firstAttempt.Should().ThrowAsync<IOException>();
        LauncherContentViewKind viewAfterFailure = viewModel.ActiveContentView;
        bool controlsEnabledAfterFailure = viewModel.MainControlsEnabled;
        await viewModel.ShowContentViewAsync(LauncherContentViewKind.Patches);

        viewAfterFailure.Should().Be(LauncherContentViewKind.Modifications);
        controlsEnabledAfterFailure.Should().BeTrue();
        viewModel.ActiveContentView.Should().Be(LauncherContentViewKind.Patches);
        catalog.OriginalGameChildManifestReadCount.Should().Be(2);
    }

    [Fact]
    public void UpdateAddonAndPatchTabLabels_WhenChildIntegrityRepairIsActive_UpdatesTabDownloadIndicators()
    {
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            TestLauncherContent.Catalog()
                .WithPatch(LauncherContentKey.OriginalGame.Name, "Patch")
                .Build());

        viewModel.RefreshPatchesList();

        viewModel.PatchesListSource[0].BeginIntegrityProgress("Preparing");

        viewModel.IsPatchesTabDownloadIndicatorVisible.Should().BeTrue();
    }

    [Fact]
    public void ChildPackageActivity_WhenParentModIsDisplayed_ForwardsProgressToParentModTile()
    {
        const string ParentName = "Shockwave";
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            TestLauncherContent.Catalog()
                .WithMod(ParentName)
                .WithPatch(ParentName, "Patch")
                .Build());

        viewModel.RefreshModsList();
        viewModel.ModsListSource[0].IsSelected = true;
        viewModel.RefreshPatchesList();

        viewModel.PatchesListSource[0].BeginIntegrityProgress("Repairing patch");
        viewModel.PatchesListSource[0].ReportIntegrityProgress("Repairing patch", 35);

        viewModel.ModsListSource[0].HasActivePackageActivity.Should().BeTrue();
        viewModel.ModsListSource[0].ProgressMessage.Should().Be("Repairing patch");
        viewModel.ModsListSource[0].ProgressValue.Should().Be(35);
    }

    [Fact]
    public void ChildPackageActivity_WhenSeveralChildrenAreActive_ForwardsTheAverageAndTheLastMessage()
    {
        const string ParentName = "Shockwave";
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            TestLauncherContent.Catalog()
                .WithMod(ParentName)
                .WithPatch(ParentName, "First Patch")
                .WithPatch(ParentName, "Second Patch")
                .Build());

        viewModel.RefreshModsList();
        viewModel.ModsListSource[0].IsSelected = true;
        viewModel.RefreshPatchesList();

        viewModel.PatchesListSource[0].BeginIntegrityProgress("Repairing first patch");
        viewModel.PatchesListSource[1].BeginIntegrityProgress("Repairing second patch");
        viewModel.PatchesListSource[0].ReportIntegrityProgress("Repairing first patch", 20);
        viewModel.PatchesListSource[1].ReportIntegrityProgress("Repairing second patch", 60);

        viewModel.ModsListSource[0].HasActivePackageActivity.Should().BeTrue();
        viewModel.ModsListSource[0].ProgressValue.Should().Be(40);
        viewModel.ModsListSource[0].ProgressMessage.Should().Be("Repairing second patch");
    }

    [Fact]
    public void ChildPackageActivity_WhenTheActiveChildHasNoMessage_ForwardsThePreparingFallback()
    {
        const string ParentName = "Shockwave";
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            TestLauncherContent.Catalog()
                .WithMod(ParentName)
                .WithPatch(ParentName, "Patch")
                .Build());

        viewModel.RefreshModsList();
        viewModel.ModsListSource[0].IsSelected = true;
        viewModel.RefreshPatchesList();

        viewModel.PatchesListSource[0].BeginIntegrityProgress(string.Empty);

        viewModel.ModsListSource[0].HasActivePackageActivity.Should().BeTrue();
        viewModel.ModsListSource[0].ProgressMessage.Should().Be("Preparing");
    }

    [Fact]
    public void ChildPackageActivity_WhenCompleted_ClearsForwardedParentModProgress()
    {
        const string ParentName = "Shockwave";
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            TestLauncherContent.Catalog()
                .WithMod(ParentName)
                .WithPatch(ParentName, "Patch")
                .Build());

        viewModel.RefreshModsList();
        viewModel.ModsListSource[0].IsSelected = true;
        viewModel.RefreshPatchesList();
        viewModel.PatchesListSource[0].BeginIntegrityProgress("Repairing patch");

        viewModel.PatchesListSource[0].CompleteIntegrityProgress();

        viewModel.ModsListSource[0].HasActivePackageActivity.Should().BeFalse();
        viewModel.ModsListSource[0].ProgressValue.Should().Be(0);
    }

    [Fact]
    public void RefreshPatchesList_WhenActiveChildActivityExists_ReusesTileAndKeepsTabIndicator()
    {
        const string ParentName = "Shockwave";
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            TestLauncherContent.Catalog()
                .WithMod(ParentName)
                .WithPatch(ParentName, "Patch")
                .Build());

        viewModel.RefreshModsList();
        viewModel.ModsListSource[0].IsSelected = true;
        viewModel.RefreshPatchesList();
        ModificationViewModel activePatch = viewModel.PatchesListSource[0];
        activePatch.BeginIntegrityProgress("Repairing patch");
        activePatch.ReportIntegrityProgress("Repairing patch", 55);

        viewModel.RefreshPatchesList();

        viewModel.PatchesListSource[0].Should().BeSameAs(activePatch);
        viewModel.PatchesListSource[0].HasActivePackageActivity.Should().BeTrue();
        viewModel.PatchesListSource[0].ProgressValue.Should().Be(55);
        viewModel.IsPatchesTabDownloadIndicatorVisible.Should().BeTrue();
    }

    [Fact]
    public void RefreshAddonsList_WhenActiveDownloadExists_ReusesTileAndKeepsVisibleProgress()
    {
        const string ParentName = "Shockwave";
        var packageActivityService = new LauncherPackageActivityService();
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            TestLauncherContent.Catalog()
                .WithMod(ParentName)
                .WithAddon(ParentName, "Addon")
                .Build(),
            packageActivityService: packageActivityService);

        viewModel.RefreshModsList();
        viewModel.ModsListSource[0].IsSelected = true;
        viewModel.RefreshAddonsList();
        ModificationViewModel activeAddon = viewModel.AddonsListSource[0];
        BeginActiveDownload(packageActivityService, activeAddon);
        activeAddon.ReportPackageProgress("Downloading", 72);

        viewModel.RefreshAddonsList();

        viewModel.AddonsListSource[0].Should().BeSameAs(activeAddon);
        viewModel.AddonsListSource[0].HasActivePackageActivity.Should().BeTrue();
        viewModel.AddonsListSource[0].ProgressValue.Should().Be(72);
        viewModel.IsAddonsTabDownloadIndicatorVisible.Should().BeTrue();
    }

    [Fact]
    public void RefreshModsList_OrdersCardsByPersistedListNumber()
    {
        FakeLauncherContentCatalog catalog = TestLauncherContent.Catalog()
            .WithMod("Second")
            .WithMod("First")
            .Build();
        SetListOrder(catalog, "First", "Second");
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(catalog);

        viewModel.RefreshModsList();

        viewModel.ModsListSource
            .Select(modification => modification.ContainerModification.Name)
            .Should()
            .Equal("First", "Second");
    }

    [Fact]
    public void RefreshModsList_AddsAdvertisingFirstWhenConnectedAndThresholdIsMet()
    {
        LauncherContentVersion advertising = TestLauncherContent.Version(
            "Advertising",
            type: ModificationType.Advertising);
        FakeLauncherContentCatalog catalog = CreateAdvertisingCatalog(advertising);
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            catalog,
            runtimeContext: TestLauncherRuntimeContext.Create(connected: true));

        viewModel.RefreshModsList();

        viewModel.ModsListSource
            .Select(modification => modification.ContainerModification.Name)
            .Should()
            .Equal("Advertising", "First", "Second", "Third");
        catalog.Data.Modifications.Should().HaveCount(3);
        catalog.Data.FindContent(advertising.ContentKey).Should().BeNull();
    }

    [Fact]
    public void RefreshModsList_WhenFewerModificationsThanTheAdvertisingThreshold_ShowsOnlyTheModifications()
    {
        LauncherContentVersion advertising = TestLauncherContent.Version(
            "Advertising",
            type: ModificationType.Advertising);
        FakeLauncherContentCatalog catalog = TestLauncherContent.Catalog()
            .WithMod("First")
            .WithMod("Second")
            .Build();
        catalog.Advertising = advertising;
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            catalog,
            runtimeContext: TestLauncherRuntimeContext.Create(connected: true));

        viewModel.RefreshModsList();

        viewModel.ModsListSource
            .Select(modification => modification.ContainerModification.Name)
            .Should()
            .Equal("First", "Second");
    }

    /// <summary>
    ///     A row past the end is a normal state: modifications the tile sat below can be deleted between sessions.
    /// </summary>
    [Theory]
    [InlineData(2, "First", "Second", "Advertising", "Third")]
    [InlineData(9, "First", "Second", "Third", "Advertising")]
    public void RefreshModsList_PlacesAdvertisingAtItsPersistedRow(
        int persistedRow,
        params string[] expectedOrder)
    {
        MainWindowViewModel viewModel = CreateAdvertisingViewModel(
            new RecordingLauncherPreferencesService(
                CreatePreferences(new LauncherGamePreferences { AdvertisingPositionInList = persistedRow })));

        viewModel.RefreshModsList();

        viewModel.ModsListSource
            .Select(modification => modification.ContainerModification.Name)
            .Should()
            .Equal(expectedOrder);
    }

    [Fact]
    public void MoveModInList_WhenAdvertisingIsDragged_PersistsItsNewRowForTheActiveGame()
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences());
        MainWindowViewModel viewModel = CreateAdvertisingViewModel(preferencesService);
        viewModel.RefreshModsList();

        viewModel.MoveModInList(0, 2);

        preferencesService.Current.Games.ZeroHour.AdvertisingPositionInList.Should().Be(2);
        preferencesService.Current.Games.Generals.AdvertisingPositionInList.Should().Be(0);
    }

    /// <summary>
    ///     Going offline, or dropping below the threshold that shows the tile, must not quietly reset the row the
    ///     user chose for it.
    /// </summary>
    [Fact]
    public void RefreshModsList_WhenTheListHasNoAdvertisingTile_KeepsTheStoredRow()
    {
        var preferencesService = new RecordingLauncherPreferencesService(
            CreatePreferences(new LauncherGamePreferences { AdvertisingPositionInList = 2 }));
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            TestLauncherContent.Catalog().WithMod("First").WithMod("Second").Build(),
            preferencesService,
            TestLauncherRuntimeContext.Create(connected: true));

        viewModel.RefreshModsList();

        preferencesService.Current.Games.ZeroHour.AdvertisingPositionInList.Should().Be(2);
    }

    [Fact]
    public void MoveModInList_WhenTheAdvertisingRowCannotBePersisted_KeepsReorderingUsable()
    {
        MainWindowViewModel viewModel = CreateAdvertisingViewModel(
            new RecordingLauncherPreferencesService(new LauncherPreferences())
            {
                UpdateFailure = new LauncherPreferencesPersistenceException(new IOException("locked"))
            });
        viewModel.RefreshModsList();

        Action act = () => viewModel.MoveModInList(0, 2);

        act.Should().NotThrow();
        viewModel.ModsListSource
            .Select(modification => modification.ContainerModification.Name)
            .Should()
            .Equal("First", "Second", "Advertising", "Third");
    }

    [Fact]
    public void RefreshModsList_UsesCurrentCatalogAdvertisingWithoutEmbeddingIt()
    {
        LauncherContentVersion firstAdvertising = TestLauncherContent.Version(
            "Advertising",
            type: ModificationType.Advertising);
        LauncherContentVersion updatedAdvertising = TestLauncherContent.Version(
            "Advertising",
            "2.0",
            ModificationType.Advertising);
        FakeLauncherContentCatalog catalog = CreateAdvertisingCatalog(firstAdvertising);
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            catalog,
            runtimeContext: TestLauncherRuntimeContext.Create(connected: true));

        viewModel.RefreshModsList();
        LauncherContent firstCard = viewModel.ModsListSource[0].ContainerModification;
        catalog.Advertising = updatedAdvertising;
        viewModel.RefreshModsList();

        LauncherContent currentCard = viewModel.ModsListSource[0].ContainerModification;
        currentCard.Should().NotBeSameAs(firstCard);
        currentCard.LatestVersion.Should().BeSameAs(updatedAdvertising);
        catalog.Data.FindContent(updatedAdvertising.ContentKey).Should().BeNull();
    }

    [Fact]
    public void RefreshModsList_DoesNotAddAdvertisingWhenDisconnected()
    {
        LauncherContentVersion advertising = TestLauncherContent.Version(
            "Advertising",
            type: ModificationType.Advertising);
        FakeLauncherContentCatalog catalog = TestLauncherContent.Catalog()
            .WithMod("First")
            .WithMod("Second")
            .WithMod("Third")
            .Build();
        SetListOrder(catalog, "First", "Second", "Third");
        catalog.Advertising = advertising;
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(catalog);

        viewModel.RefreshModsList();

        viewModel.ModsListSource
            .Select(modification => modification.ContainerModification.Name)
            .Should()
            .Equal("First", "Second", "Third");
        catalog.Data.FindContent(advertising.ContentKey).Should().BeNull();
    }

    [Fact]
    public async Task AddModToListAsync_DownloadsRepositoryDataAddsTileAndMovesItToTopAsync()
    {
        LauncherContentVersion downloadedVersion = TestLauncherContent.Version("New Mod", "2.0");
        FakeLauncherContentCatalog catalog = TestLauncherContent.Catalog()
            .WithMod("First")
            .WithMod("Second")
            .Build();
        SetListOrder(catalog, "First", "Second");
        catalog.DownloadHandler = (_, _) => Task.FromResult(downloadedVersion);
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(catalog);
        viewModel.RefreshModsList();
        using CancellationTokenSource cancellation = new();

        await viewModel.AddModToListAsync("New Mod", cancellation.Token);

        catalog.DownloadRequests.Should().Equal("New Mod");
        catalog.ChildManifestRequests.Should().Equal(downloadedVersion.ContentKey);
        catalog.Data.Modifications.Should().HaveCount(3);
        viewModel.ModsListSource
            .Select(modification => modification.ContainerModification.Name)
            .Should()
            .Equal("New Mod", "First", "Second");
        viewModel.ModsListSource
            .Select(modification => modification.ContainerModification.NumberInList)
            .Should()
            .Equal(0, 1, 2);
        viewModel.ModsListSource[0].UpdateButtonBlinking.Should().BeTrue();
    }

    [Fact]
    public void GetNotAddedRepositoryModificationNames_OmitsModificationsAlreadyInTheCatalog()
    {
        FakeLauncherContentCatalog catalog = TestLauncherContent.Catalog()
            .WithMod("ShockWave")
            .Build();
        catalog.RepositoryModificationNames = ["shockwave", "Contra"];
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(catalog);

        IReadOnlyList<string> offeredNames = viewModel.GetNotAddedRepositoryModificationNames();

        offeredNames.Should().Equal("Contra");
    }

    [Fact]
    public void UpdateAddonAndPatchTabLabels_UsesSelectedContentAndInstalledCounts()
    {
        const string ParentName = "Shockwave";
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            TestLauncherContent.Catalog()
                .WithMod(ParentName)
                .WithPatch(ParentName, "Patch")
                .WithAddon(ParentName, "Addon 1")
                .WithAddon(ParentName, "Addon 2", installed: false)
                .WithAddon(ParentName, "Addon 3")
                .Build());
        viewModel.RefreshModsList();
        viewModel.ModsListSource[0].IsSelected = true;
        viewModel.RefreshPatchesList();
        viewModel.RefreshAddonsList();
        viewModel.PatchesListSource[0].IsSelected = true;
        foreach (ModificationViewModel addon in viewModel.AddonsListSource)
        {
            addon.IsSelected = true;
        }

        viewModel.UpdateAddonAndPatchTabLabels();

        viewModel.PatchesTabText.Should().Be("Patches for Shockwave (1)");
        viewModel.AddonsTabText.Should().Be("Add-ons for Shockwave (2)");
        viewModel.ManualAddPatchText.Should().Be("Add patch for Shockwave");
        viewModel.ManualAddAddonText.Should().Be("Add addon for Shockwave");
        viewModel.IsPatchesButtonVisible.Should().BeTrue();
        viewModel.IsAddonsButtonVisible.Should().BeTrue();
    }

    [Fact]
    public void RefreshTabs_UsesManagedGameWhenNoModificationIsSelected()
    {
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            runtimeContext: TestLauncherRuntimeContext.Create(SupportedGame.Generals));

        bool showChildContent = viewModel.RefreshTabs();

        showChildContent.Should().BeTrue();
        viewModel.PatchesTabText.Should().Be("Patches for Generals");
        viewModel.AddonsTabText.Should().Be("Add-ons for Generals");
        viewModel.ManualAddPatchText.Should().Be("Add patch for Generals");
        viewModel.ManualAddAddonText.Should().Be("Add addon for Generals");
    }

    [Fact]
    public void RefreshTabs_HidesChildContentForAdvertising()
    {
        LauncherContentVersion advertising = TestLauncherContent.Version(
            "Sponsor",
            type: ModificationType.Advertising);
        FakeLauncherContentCatalog catalog = TestLauncherContent.Catalog()
            .WithMod("First")
            .WithMod("Second")
            .WithMod("Third")
            .Build();
        catalog.Advertising = advertising;
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            catalog,
            runtimeContext: TestLauncherRuntimeContext.Create(connected: true));
        viewModel.RefreshModsList();
        viewModel.ModsListSource[0].IsSelected = true;

        bool showChildContent = viewModel.RefreshTabs();

        showChildContent.Should().BeFalse();
        viewModel.IsPatchesButtonVisible.Should().BeFalse();
        viewModel.IsAddonsButtonVisible.Should().BeFalse();
    }

    [Fact]
    public void UpdateAddonAndPatchTabLabels_WhenChildDownloadsAreInactive_HidesTabDownloadIndicators()
    {
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            TestLauncherContent.Catalog()
                .WithPatch(LauncherContentKey.OriginalGame.Name, "Patch")
                .WithAddon(LauncherContentKey.OriginalGame.Name, "Addon")
                .Build());

        viewModel.RefreshPatchesList();
        viewModel.RefreshAddonsList();

        viewModel.UpdateAddonAndPatchTabLabels();

        viewModel.IsPatchesTabDownloadIndicatorVisible.Should().BeFalse();
        viewModel.IsAddonsTabDownloadIndicatorVisible.Should().BeFalse();
    }

    [Fact]
    public void RemoveContentFromList_WhenRemovingModification_RemovesTileAndRenumbersRemainingMods()
    {
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(
            TestLauncherContent.Catalog()
                .WithMod("First")
                .WithMod("Second")
                .Build());
        viewModel.RefreshModsList();
        ModificationViewModel removed = viewModel.ModsListSource[0];

        viewModel.RemoveContentFromList(removed);

        viewModel.ModsListSource.Select(modification => modification.ContainerModification.Name)
            .Should()
            .Equal("Second");
        viewModel.ModsListSource[0].ContainerModification.NumberInList.Should().Be(0);
        removed.ContainerModification.IsSelected.Should().BeFalse();
    }

    [Theory]
    [InlineData(ModificationType.Mod)]
    [InlineData(ModificationType.Patch)]
    [InlineData(ModificationType.Addon)]
    public void AddImportedContentToList_AddsContentToTheTopOfTheMatchingList(ModificationType kind)
    {
        string parentContentName = kind == ModificationType.Mod
            ? string.Empty
            : LauncherContentKey.OriginalGame.Name;
        FakeLauncherContentCatalog catalog = kind switch
        {
            ModificationType.Patch => TestLauncherContent.Catalog()
                .WithPatch(parentContentName, "First")
                .WithPatch(parentContentName, "Second")
                .Build(),
            ModificationType.Addon => TestLauncherContent.Catalog()
                .WithAddon(parentContentName, "First")
                .WithAddon(parentContentName, "Second")
                .Build(),
            _ => TestLauncherContent.Catalog()
                .WithMod("First")
                .WithMod("Second")
                .Build()
        };
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(catalog);
        RefreshListFor(viewModel, kind);
        LauncherContent importedModification = TestLauncherContent.From(TestLauncherContent.Version(
            "Imported",
            type: kind,
            parentContentName: parentContentName));
        viewModel.AddImportedContentToList(importedModification);

        GetListFor(viewModel, kind)
            .Select(modification => modification.ContainerModification.Name)
            .Should()
            .Equal("Imported", "First", "Second");
    }

    [Fact]
    public void SaveLauncherData_WhenOnlyOriginalGameContentIsSelected_ProjectsAndSaves()
    {
        FakeLauncherContentCatalog catalog = TestLauncherContent.Catalog()
            .WithPatch(LauncherContentKey.OriginalGame.Name, "Original Patch")
            .Selected("Original Patch", ModificationType.Patch, LauncherContentKey.OriginalGame.Name)
            .Build();
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(catalog);
        viewModel.Initialize();

        viewModel.SaveLauncherData();

        catalog.Data.Patches.Should().ContainSingle().Which.IsSelected.Should().BeTrue();
        catalog.SaveCount.Should().Be(1);
    }

    [Theory]
    [InlineData(ModificationType.Addon)]
    [InlineData(ModificationType.Patch)]
    public void RemoveContentFromList_WhenRemovingChildContent_RemovesTileFromMatchingList(
        ModificationType modificationType)
    {
        FakeLauncherContentCatalog catalog = modificationType == ModificationType.Addon
            ? TestLauncherContent.Catalog()
                .WithAddon(LauncherContentKey.OriginalGame.Name, "Addon")
                .Build()
            : TestLauncherContent.Catalog()
                .WithPatch(LauncherContentKey.OriginalGame.Name, "Patch")
                .Build();
        MainWindowViewModel viewModel = TestMainWindowViewModel.Create(catalog);
        RefreshListFor(viewModel, modificationType);

        IReadOnlyList<ModificationViewModel> childList = GetListFor(viewModel, modificationType);
        ModificationViewModel child = childList[0];
        child.IsSelected = true;

        viewModel.RemoveContentFromList(child);

        childList.Should().BeEmpty();
        child.IsSelected.Should().BeFalse();
    }

    private static void RefreshListFor(MainWindowViewModel viewModel, ModificationType kind)
    {
        switch (kind)
        {
            case ModificationType.Patch:
                viewModel.RefreshPatchesList();
                break;
            case ModificationType.Addon:
                viewModel.RefreshAddonsList();
                break;
            default:
                viewModel.RefreshModsList();
                break;
        }
    }

    private static IReadOnlyList<ModificationViewModel> GetListFor(
        MainWindowViewModel viewModel,
        ModificationType kind)
    {
        return kind switch
        {
            ModificationType.Patch => viewModel.PatchesListSource,
            ModificationType.Addon => viewModel.AddonsListSource,
            _ => viewModel.ModsListSource
        };
    }

    /// <summary>
    ///     Builds the smallest catalog that shows the advertising tile. The modifications are added in a different
    ///     order than their stored rows, so a test sees the persisted order rather than the catalog's.
    /// </summary>
    private static FakeLauncherContentCatalog CreateAdvertisingCatalog(LauncherContentVersion advertising)
    {
        FakeLauncherContentCatalog catalog = TestLauncherContent.Catalog()
            .WithMod("Third")
            .WithMod("First")
            .WithMod("Second")
            .Build();
        SetListOrder(catalog, "First", "Second", "Third");
        catalog.Advertising = advertising;
        return catalog;
    }

    private static MainWindowViewModel CreateAdvertisingViewModel(
        ILauncherPreferencesService preferencesService)
    {
        return TestMainWindowViewModel.Create(
            CreateAdvertisingCatalog(
                TestLauncherContent.Version("Advertising", type: ModificationType.Advertising)),
            preferencesService,
            TestLauncherRuntimeContext.Create(connected: true));
    }

    private static void SetListOrder(FakeLauncherContentCatalog catalog, params string[] namesInOrder)
    {
        for (int index = 0; index < namesInOrder.Length; index++)
        {
            catalog.Data.Modifications
                .Single(modification => string.Equals(
                    modification.Name,
                    namesInOrder[index],
                    StringComparison.Ordinal))
                .NumberInList = index;
        }
    }

    private static IGameProcessLauncher CreateProcessLauncher(IGameProcessLaunchOperation operation)
    {
        IGameProcessLauncher processLauncher = Substitute.For<IGameProcessLauncher>();
        processLauncher.StartAsync(
                Arg.Any<GameLaunchRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(operation));
        return processLauncher;
    }

    private static Task WaitForActiveProcessAsync(LauncherLaunchCoordinator launchCoordinator)
    {
        var processTracked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        launchCoordinator.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(LauncherLaunchCoordinator.HasActiveProcess) &&
                launchCoordinator.HasActiveProcess)
            {
                processTracked.TrySetResult();
            }
        };

        return processTracked.Task;
    }

    private static Task<LauncherLaunchResult> StartGameClientLaunchAsync(
        LauncherLaunchCoordinator launchCoordinator)
    {
        return launchCoordinator.LaunchAsync(
            new LauncherLaunchRequest(
                GameLaunchTargetKind.GameClient,
                "generalsonlinezh.exe",
                true,
                Array.Empty<LauncherContentVersion>()),
            Array.Empty<ILaunchContentIntegrityProgressTarget>(),
            new Window(),
            CancellationToken.None);
    }

    private static LauncherPreferences CreatePreferences(LauncherGamePreferences zeroHour)
    {
        return new LauncherPreferences
        {
            Games = new LauncherGamePreferencesSet { ZeroHour = zeroHour }
        };
    }

    private static void BeginActiveDownload(
        LauncherPackageActivityService packageActivityService,
        ModificationViewModel viewModel)
    {
        var completion = new TaskCompletionSource<PackageDownloadResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        packageActivityService.TryStartDownload(
                viewModel,
                viewModel.ContainerModification.Name,
                (_, _, _) => completion.Task,
                viewModel.BeginPackageActivityPresentation,
                _ => { },
                () => { },
                viewModel.CompletePackageActivityPresentation,
                out _)
            .Should()
            .BeTrue();
    }
}
