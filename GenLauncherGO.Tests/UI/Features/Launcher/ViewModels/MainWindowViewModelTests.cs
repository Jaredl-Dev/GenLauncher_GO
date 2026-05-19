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
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Exceptions;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Launcher.ViewModels;
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Shared.Themes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Launcher.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void SetMainControlsEnabled_WhenDisabled_UpdatesComputedControlState()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        viewModel.SetMainControlsEnabled(false);

        viewModel.MainControlsEnabled.Should().BeFalse();
        viewModel.StartGameButtonEnabled.Should().BeFalse();
        viewModel.WorldBuilderButtonEnabled.Should().BeFalse();
        viewModel.IsLoadingIndicatorVisible.Should().BeTrue();
    }

    [Fact]
    public void LaunchStateUpdatesBindableBusyAndProcessOverlayProperties()
    {
        StaTestRunner.Run(async () =>
        {
            var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences());
            LauncherPackageActivityService packageActivityService = new();
            ILaunchPreparationService preparationService = Substitute.For<ILaunchPreparationService>();
            var preparationStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var releasePreparation = new ManualResetEventSlim();
            preparationService.Prepare(
                    Arg.Any<LaunchPreparationRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    preparationStarted.TrySetResult();
                    if (!releasePreparation.Wait(TimeSpan.FromSeconds(5)))
                    {
                        throw new TimeoutException("Timed out waiting to release launch preparation.");
                    }

                    return true;
                });
            preparationService.Cleanup(
                    Arg.Any<LauncherPaths>(),
                    Arg.Any<CancellationToken>())
                .Returns(true);

            IGameProcessLauncher processLauncher = Substitute.For<IGameProcessLauncher>();
            IGameProcessLaunchOperation operation = Substitute.For<IGameProcessLaunchOperation>();
            var processStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var processCompletion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            string currentProcessName = "generalsonlinezh.exe";
            operation.CurrentExecutableName.Returns(_ => currentProcessName);
            operation.Completion.Returns(processCompletion.Task);
            processLauncher.StartAsync(
                    Arg.Any<GameLaunchRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    processStarted.TrySetResult();
                    return Task.FromResult(operation);
                });

            LauncherLaunchCoordinator launchCoordinator = TestLauncherLaunchCoordinator.Create(
                packageActivityService,
                preferencesService,
                preparationService: preparationService,
                processLauncher: processLauncher);
            MainWindowViewModel viewModel = CreateViewModel(
                preferencesService,
                packageActivityService: packageActivityService,
                launchCoordinator: launchCoordinator);

            Task<LauncherLaunchResult> launchTask = launchCoordinator.LaunchAsync(
                new LauncherLaunchRequest(
                    GameLaunchTargetKind.GameClient,
                    "generalsonlinezh.exe",
                    useGeneralsOnline: true,
                    Array.Empty<LauncherContentVersion>()),
                Array.Empty<ILaunchContentIntegrityProgressTarget>(),
                new Window(),
                CancellationToken.None);

            await preparationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            viewModel.MainControlsEnabled.Should().BeFalse();
            viewModel.IsLoadingIndicatorVisible.Should().BeTrue();

            releasePreparation.Set();
            await processStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            viewModel.MainControlsEnabled.Should().BeFalse();
            viewModel.IsRunningProcessOverlayVisible.Should().BeTrue();
            viewModel.RunningProcessStatusText.Should().Be("Running generalsonlinezh.exe");

            currentProcessName = "generalszh.exe";
            operation.CurrentExecutableNameChanged += Raise.Event();
            viewModel.RunningProcessStatusText.Should().Be("Running generalszh.exe");

            processCompletion.SetResult(true);
            (await launchTask).ProcessSucceeded.Should().BeTrue();
            viewModel.MainControlsEnabled.Should().BeTrue();
            viewModel.IsRunningProcessOverlayVisible.Should().BeFalse();
            viewModel.RunningProcessStatusText.Should().BeEmpty();
        });
    }

    [Fact]
    public void PackageActivityChanges_UpdateTaskbarProgressState()
    {
        var packageActivityService = new LauncherPackageActivityService();
        MainWindowViewModel viewModel = CreateViewModel(packageActivityService: packageActivityService);

        bool started = packageActivityService.TryBegin(
            "Shockwave",
            out LauncherPackageActivityService.LauncherPackageActivityLease? lease);
        packageActivityService.ReportProgress(42);

        started.Should().BeTrue();
        viewModel.TaskbarProgressState.Should().Be(LauncherTaskbarProgressState.Normal);
        viewModel.TaskbarProgressValue.Should().Be(0.42D);

        lease?.Dispose();

        viewModel.TaskbarProgressState.Should().Be(LauncherTaskbarProgressState.None);
        viewModel.TaskbarProgressValue.Should().Be(0D);
    }

    [Fact]
    public void PackageActivityChanges_WhenProgressIsUnknown_ShowIndeterminateTaskbarProgress()
    {
        var packageActivityService = new LauncherPackageActivityService();
        MainWindowViewModel viewModel = CreateViewModel(packageActivityService: packageActivityService);

        bool started = packageActivityService.TryBegin(
            "Shockwave",
            out LauncherPackageActivityService.LauncherPackageActivityLease? lease);

        started.Should().BeTrue();
        viewModel.TaskbarProgressState.Should().Be(LauncherTaskbarProgressState.Indeterminate);
        viewModel.TaskbarProgressValue.Should().Be(0D);

        lease?.Dispose();
    }

    [Fact]
    public void DisposeUnsubscribesFromPreferenceChanges()
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences());
        MainWindowViewModel viewModel = CreateViewModel(preferencesService);

        viewModel.Dispose();
        LauncherPreferences current = preferencesService.Current;
        preferencesService.Update(current with
        {
            Games = current.Games.With(
                SupportedGame.ZeroHour,
                current.Games.ZeroHour with
                {
                    GameArguments = LauncherGameArgumentService.WindowedArgument,
                }),
        });

        viewModel.WindowedModeButtonText.Should().BeEmpty();
    }

    [Fact]
    public void ToggleGameArgument_WhenWindowedMissing_AddsArgumentAndRefreshesButtonText()
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences());
        MainWindowViewModel viewModel = CreateViewModel(preferencesService);

        viewModel.ToggleGameArgument(LauncherGameArgumentService.WindowedArgument);

        preferencesService.Updates.Should().ContainSingle();
        preferencesService.Current.Games.ZeroHour.GameArguments
            .Should()
            .Be(LauncherGameArgumentService.WindowedArgument);
        viewModel.WindowedModeButtonText.Should().Be("Change to full screen");
    }

    [Fact]
    public void SelectedGameClientOption_WhenSet_UpdatesPreferencesAndWindowTitle()
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences());
        MainWindowViewModel viewModel = CreateViewModel(preferencesService);
        ExecutableOption option = new(
            "GenLauncherGO client",
            "generals.exe",
            isAvailable: true,
            isBuiltIn: true);

        viewModel.SelectedGameClientOption = option;

        preferencesService.Current.Games.ZeroHour.SelectedGameClient.Should().Be("generals.exe");
        viewModel.WindowTitle.Should().Be("GenLauncherGO - Zero Hour - GenLauncherGO client");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedWorldBuilderOption_PersistsSelectionRegardlessOfAvailability(bool isAvailable)
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences());
        MainWindowViewModel viewModel = CreateViewModel(preferencesService);

        viewModel.SelectedWorldBuilderOption = new ExecutableOption(
            "World Builder",
            "worldbuilder.exe",
            isAvailable,
            isBuiltIn: isAvailable);

        preferencesService.Current.Games.ZeroHour.SelectedWorldBuilder
            .Should().Be("worldbuilder.exe");
    }

    [Fact]
    public void InitializeRefreshesLauncherStateAndIncrementsLaunchCount()
    {
        var preferencesService = new RecordingLauncherPreferencesService(
            CreatePreferences(new LauncherGamePreferences { LaunchesCount = -1 }));
        IGameExecutableDiscoveryService executableDiscovery = Substitute.For<IGameExecutableDiscoveryService>();
        executableDiscovery.GetGameClients().Returns(new[]
        {
            new GameClientExecutable("generals.exe", GameClientExecutableKind.GeneralsOnline, true)
        });
        executableDiscovery.GetWorldBuilders().Returns(new[]
        {
            new WorldBuilderExecutable("worldbuilder.exe", WorldBuilderExecutableKind.Community, true)
        });
        MainWindowViewModel viewModel = CreateViewModel(
            preferencesService,
            mods: new[] { CreateModification("Shockwave", ModificationType.Mod) },
            executableDiscovery: executableDiscovery);

        viewModel.Initialize();

        viewModel.SupportedGameClients.Should().ContainSingle();
        viewModel.SupportedWorldBuilders.Should().ContainSingle();
        viewModel.ModsListSource.Should().ContainSingle();
        viewModel.CurrentLauncherVersionText.Should().Be("Current version: 1.2.3");
        preferencesService.Current.Games.ZeroHour.LaunchesCount.Should().Be(101);
    }

    [Fact]
    public void Initialize_WhenLaunchCountExceedsAdvertisingThreshold_ResetsCounterAndKeepsAdvertisingVisible()
    {
        var preferencesService = new RecordingLauncherPreferencesService(
            CreatePreferences(new LauncherGamePreferences { LaunchesCount = 101 }));
        LauncherContentVersion advertising = new()
        {
            Name = "Featured",
            Version = "1.0",
            ModificationType = ModificationType.Advertising
        };
        var catalog = new FakeLauncherContentCatalog
        {
            Advertising = advertising
        };
        MainWindowViewModel viewModel = CreateViewModel(
            preferencesService,
            mods:
            [
                CreateModification("First", ModificationType.Mod),
                CreateModification("Second", ModificationType.Mod),
                CreateModification("Third", ModificationType.Mod),
            ],
            catalog: catalog);

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
        MainWindowViewModel viewModel = CreateViewModel(preferencesService);

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
            new GameClientExecutable("generals.exe", GameClientExecutableKind.GeneralsOnline, true),
            new GameClientExecutable("genlauncher.exe", GameClientExecutableKind.Community, true)
        });
        MainWindowViewModel viewModel = CreateViewModel(
            preferencesService,
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
    public void RefreshGameClientOptions_WhenSelectedFileIsDeletedKeepsLaunchActionEnabledForFeedback()
    {
        bool isAvailable = true;
        var preferencesService = new RecordingLauncherPreferencesService(
            CreatePreferences(new LauncherGamePreferences { SelectedGameClient = "custom.exe" }));
        IGameExecutableDiscoveryService executableDiscovery = Substitute.For<IGameExecutableDiscoveryService>();
        executableDiscovery.GetGameClients().Returns(_ => new[]
        {
            new GameClientExecutable(
                "custom.exe",
                GameClientExecutableKind.Community,
                isAvailable),
        });
        MainWindowViewModel viewModel = CreateViewModel(
            preferencesService,
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
        viewModel.SelectedGameClientOption!.ExecutableName.Should().Be("custom.exe");
        viewModel.StartGameButtonEnabled.Should().BeTrue();
    }

    [Fact]
    public void RefreshWorldBuilderOptions_WhenSelectedFileIsMissingKeepsLaunchActionEnabledForFeedback()
    {
        IGameExecutableDiscoveryService executableDiscovery = Substitute.For<IGameExecutableDiscoveryService>();
        executableDiscovery.GetWorldBuilders().Returns(new[]
        {
            new WorldBuilderExecutable(
                "custom-world-builder.exe",
                WorldBuilderExecutableKind.Community,
                isAvailable: false),
        });
        MainWindowViewModel viewModel = CreateViewModel(executableDiscovery: executableDiscovery);

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
            .Returns(Array.Empty<WorldBuilderExecutable>());
        MainWindowViewModel viewModel = CreateViewModel(executableDiscovery: executableDiscovery);

        viewModel.RefreshWorldBuilderOptions();

        viewModel.SupportedWorldBuilders.Should().BeEmpty();
        viewModel.SelectedWorldBuilderOption.Should().BeNull();
        viewModel.WorldBuilderSelectorEnabled.Should().BeFalse();
        viewModel.WorldBuilderButtonEnabled.Should().BeFalse();
    }

    [Fact]
    public void UpdateAddonAndPatchTabLabelsProjectsTheSoleActiveChildDownload()
    {
        var packageActivityService = new LauncherPackageActivityService();
        MainWindowViewModel viewModel = CreateViewModel(
            patches: new[] { CreateModification("Patch", ModificationType.Patch) },
            addons: new[]
            {
                CreateModification("Addon One", ModificationType.Addon),
                CreateModification("Addon Two", ModificationType.Addon)
            },
            packageActivityService: packageActivityService);

        viewModel.RefreshPatchesList();
        viewModel.RefreshAddonsList();

        BeginActiveDownload(packageActivityService, viewModel.AddonsListSource[0]);
        viewModel.AddonsListSource[0].ReportPackageProgress("Downloading", 20);

        viewModel.UpdateAddonAndPatchTabLabels();

        viewModel.IsPatchesTabDownloadIndicatorVisible.Should().BeFalse();
        viewModel.IsAddonsTabDownloadIndicatorVisible.Should().BeTrue();
        viewModel.AddonsTabDownloadProgressValue.Should().Be(20);
    }

    [Fact]
    public async Task ShowContentViewAsync_WhenOriginalGameContentIsRequired_LoadsContentAndShowsPatchViewAsync()
    {
        var catalog = new FakeLauncherContentCatalog();
        MainWindowViewModel viewModel = CreateViewModel(catalog: catalog);

        await viewModel.ShowContentViewAsync(LauncherContentViewKind.Patches);

        catalog.OriginalGameChildManifestReadCount.Should().Be(1);
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
        var catalog = new FakeLauncherContentCatalog();
        AddModification(catalog.Data, CreateModification("Shockwave", ModificationType.Mod), isSelected: true);
        MainWindowViewModel viewModel = CreateViewModel(
            catalog: catalog);
        viewModel.RefreshModsList();
        viewModel.ModsListSource[0].IsSelected = true;

        await viewModel.ShowContentViewAsync(viewKind);

        catalog.OriginalGameChildManifestReadCount.Should().Be(0);
        viewModel.ActiveContentView.Should().Be(viewKind);
    }

    [Fact]
    public void ReloadForActiveGame_RefreshesRepositoryAddVisibility()
    {
        MainWindowViewModel viewModel = CreateViewModel(connected: false);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.ReloadForActiveGame();

        changedProperties.Should().Contain(nameof(MainWindowViewModel.CanAddRepositoryModification));
        changedProperties.Should().Contain(nameof(MainWindowViewModel.IsAddModButtonVisible));
    }

    [Fact]
    public void ReloadForActiveGame_DoesNotReplayStartupAddModPromptForAnotherEmptyGame()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.Initialize(countLauncherStart: false);
        viewModel.AddModButtonBlinking.Should().BeTrue();

        viewModel.ReloadForActiveGame();

        viewModel.ModsListSource.Should().BeEmpty();
        viewModel.AddModButtonBlinking.Should().BeFalse();
    }

    [Fact]
    public void ReloadForActiveGame_DoesNotStartAddModPromptWhenStartupGameHadMods()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            mods: new[] { CreateModification("Shockwave", ModificationType.Mod) });
        viewModel.Initialize(countLauncherStart: false);
        viewModel.AddModButtonBlinking.Should().BeFalse();

        viewModel.ReloadForActiveGame();

        viewModel.AddModButtonBlinking.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepositoryAddVisibilityTracksConnectivityAndActiveContentViewAsync(bool connected)
    {
        MainWindowViewModel viewModel = CreateViewModel(connected: connected);
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
        MainWindowViewModel viewModel = CreateViewModel(catalog: catalog);

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
        MainWindowViewModel viewModel = CreateViewModel(
            patches: new[] { CreateModification("Patch", ModificationType.Patch) });

        viewModel.RefreshPatchesList();

        viewModel.PatchesListSource[0].BeginIntegrityProgress("Preparing");

        viewModel.IsPatchesTabDownloadIndicatorVisible.Should().BeTrue();
        viewModel.PatchesTabDownloadProgressValue.Should().Be(0);
    }

    [Fact]
    public void ChildPackageActivity_WhenParentModIsDisplayed_ForwardsProgressToParentModTile()
    {
        const string parentName = "Shockwave";
        MainWindowViewModel viewModel = CreateViewModel(
            mods: new[] { CreateModification(parentName, ModificationType.Mod) },
            patches: new[] { CreateModification("Patch", ModificationType.Patch, parentName) });

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
    public void ChildPackageActivity_WhenCompleted_ClearsForwardedParentModProgress()
    {
        const string parentName = "Shockwave";
        MainWindowViewModel viewModel = CreateViewModel(
            mods: new[] { CreateModification(parentName, ModificationType.Mod) },
            patches: new[] { CreateModification("Patch", ModificationType.Patch, parentName) });

        viewModel.RefreshModsList();
        viewModel.ModsListSource[0].IsSelected = true;
        viewModel.RefreshPatchesList();
        viewModel.PatchesListSource[0].BeginIntegrityProgress("Repairing patch");

        viewModel.PatchesListSource[0].CompleteIntegrityProgress();

        viewModel.ModsListSource[0].HasActivePackageActivity.Should().BeFalse();
        viewModel.ModsListSource[0].ProgressValue.Should().Be(0);
    }

    [Fact]
    public void RefreshPatchesList_WhenActiveChildActivityExists_ReusesTileAndKeepsTabProgress()
    {
        const string parentName = "Shockwave";
        MainWindowViewModel viewModel = CreateViewModel(
            patches: new[] { CreateModification("Patch", ModificationType.Patch, parentName) });

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
        viewModel.PatchesTabDownloadProgressValue.Should().Be(55);
    }

    [Fact]
    public void RefreshAddonsList_WhenActiveDownloadExists_ReusesTileAndKeepsVisibleProgress()
    {
        const string parentName = "Shockwave";
        var packageActivityService = new LauncherPackageActivityService();
        MainWindowViewModel viewModel = CreateViewModel(
            addons: new[] { CreateModification("Addon", ModificationType.Addon, parentName) },
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
        viewModel.AddonsTabDownloadProgressValue.Should().Be(72);
    }

    [Fact]
    public void RefreshModsListOrdersCardsByPersistedListNumber()
    {
        LauncherContent second = CreateModification(
            "Second",
            ModificationType.Mod,
            numberInList: 2);
        LauncherContent first = CreateModification(
            "First",
            ModificationType.Mod,
            numberInList: 1);
        MainWindowViewModel viewModel = CreateViewModel(
            mods: new[] { second, first },
            connected: false);

        viewModel.RefreshModsList();

        viewModel.ModsListSource
            .Select(modification => modification.ContainerModification.Name)
            .Should()
            .Equal("First", "Second");
    }

    [Fact]
    public void RefreshModsListAddsAdvertisingFirstWhenConnectedAndThresholdIsMet()
    {
        LauncherContentVersion advertising = CreateModification(
            "Advertising",
            ModificationType.Advertising).LatestVersion;
        var catalog = new FakeLauncherContentCatalog { Advertising = advertising };
        MainWindowViewModel viewModel = CreateViewModel(
            mods: new[]
            {
                CreateModification("Third", ModificationType.Mod, numberInList: 3),
                CreateModification("First", ModificationType.Mod, numberInList: 1),
                CreateModification("Second", ModificationType.Mod, numberInList: 2),
            },
            catalog: catalog);

        viewModel.RefreshModsList();

        viewModel.ModsListSource
            .Select(modification => modification.ContainerModification.Name)
            .Should()
            .Equal("Advertising", "First", "Second", "Third");
        catalog.Data.Modifications.Should().HaveCount(3);
        catalog.Data.FindContent(advertising.ContentKey).Should().BeNull();
    }

    [Fact]
    public void RefreshModsListUsesCurrentCatalogAdvertisingWithoutEmbeddingIt()
    {
        LauncherContentVersion firstAdvertising = CreateModification(
            "Advertising",
            ModificationType.Advertising).LatestVersion;
        LauncherContentVersion updatedAdvertising = CreateModification(
            "Advertising",
            ModificationType.Advertising,
            versionName: "2.0").LatestVersion;
        var catalog = new FakeLauncherContentCatalog { Advertising = firstAdvertising };
        MainWindowViewModel viewModel = CreateViewModel(
            mods: new[]
            {
                CreateModification("First", ModificationType.Mod, numberInList: 1),
                CreateModification("Second", ModificationType.Mod, numberInList: 2),
                CreateModification("Third", ModificationType.Mod, numberInList: 3),
            },
            catalog: catalog);

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
    public void RefreshModsListDoesNotAddAdvertisingWhenDisconnected()
    {
        LauncherContentVersion advertising = CreateModification(
            "Advertising",
            ModificationType.Advertising).LatestVersion;
        var catalog = new FakeLauncherContentCatalog { Advertising = advertising };
        MainWindowViewModel viewModel = CreateViewModel(
            mods: new[]
            {
                CreateModification("First", ModificationType.Mod, numberInList: 1),
                CreateModification("Second", ModificationType.Mod, numberInList: 2),
                CreateModification("Third", ModificationType.Mod, numberInList: 3),
            },
            catalog: catalog,
            connected: false);

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
        LauncherContentVersion downloadedVersion = new()
        {
            Name = "New Mod",
            Version = "2.0",
            ModificationType = ModificationType.Mod
        };
        var catalog = new FakeLauncherContentCatalog
        {
            DownloadHandler = (_, _) => Task.FromResult(downloadedVersion)
        };
        MainWindowViewModel viewModel = CreateViewModel(
            catalog: catalog);
        using CancellationTokenSource cancellation = new();

        await viewModel.AddModToListAsync("New Mod", cancellation.Token);

        catalog.DownloadRequests.Should().Equal("New Mod");
        catalog.ChildManifestRequests.Should().Equal(downloadedVersion.ContentKey);
        catalog.Data.Modifications.Should().ContainSingle()
            .Which.LatestVersion.Should().BeSameAs(downloadedVersion);
        viewModel.ModsListSource.Should().ContainSingle();
        viewModel.ModsListSource[0].ContainerModification.Name.Should().Be("New Mod");
        viewModel.ModsListSource[0].ContainerModification.NumberInList.Should().Be(0);
        viewModel.ModsListSource[0].UpdateButtonBlinking.Should().BeTrue();
    }

    [Fact]
    public void UpdateAddonAndPatchTabLabelsUsesSelectedContentAndInstalledCounts()
    {
        const string parentName = "Shockwave";
        MainWindowViewModel viewModel = CreateViewModel(
            mods: new[] { CreateModification(parentName, ModificationType.Mod) },
            patches: new[]
            {
                CreateModification(
                    "Patch",
                    ModificationType.Patch,
                    parentName,
                    installed: true),
            },
            addons: new[]
            {
                CreateModification("Addon 1", ModificationType.Addon, parentName, installed: true),
                CreateModification("Addon 2", ModificationType.Addon, parentName),
                CreateModification("Addon 3", ModificationType.Addon, parentName, installed: true),
            });
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
    public void RefreshTabsUsesManagedGameWhenNoModificationIsSelected()
    {
        MainWindowViewModel viewModel = CreateViewModel(managedGame: SupportedGame.Generals);

        bool showChildContent = viewModel.RefreshTabs();

        showChildContent.Should().BeTrue();
        viewModel.PatchesTabText.Should().Be("Patches for Generals");
        viewModel.AddonsTabText.Should().Be("Add-ons for Generals");
        viewModel.ManualAddPatchText.Should().Be("Add patch for Generals");
        viewModel.ManualAddAddonText.Should().Be("Add addon for Generals");
    }

    [Fact]
    public void RefreshTabsHidesChildContentForAdvertising()
    {
        LauncherContentVersion advertising = CreateModification(
            "Sponsor",
            ModificationType.Advertising).LatestVersion;
        var catalog = new FakeLauncherContentCatalog { Advertising = advertising };
        MainWindowViewModel viewModel = CreateViewModel(
            mods:
            [
                CreateModification("First", ModificationType.Mod),
                CreateModification("Second", ModificationType.Mod),
                CreateModification("Third", ModificationType.Mod),
            ],
            catalog: catalog);
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
        MainWindowViewModel viewModel = CreateViewModel(
            patches: new[] { CreateModification("Patch", ModificationType.Patch) },
            addons: new[] { CreateModification("Addon", ModificationType.Addon) });

        viewModel.RefreshPatchesList();
        viewModel.RefreshAddonsList();

        viewModel.UpdateAddonAndPatchTabLabels();

        viewModel.IsPatchesTabDownloadIndicatorVisible.Should().BeFalse();
        viewModel.PatchesTabDownloadProgressValue.Should().Be(0);
        viewModel.IsAddonsTabDownloadIndicatorVisible.Should().BeFalse();
        viewModel.AddonsTabDownloadProgressValue.Should().Be(0);
    }

    [Fact]
    public void RemoveContentFromList_WhenRemovingModification_RemovesTileAndRenumbersRemainingMods()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            mods: new[]
            {
                CreateModification("First", ModificationType.Mod),
                CreateModification("Second", ModificationType.Mod)
            });
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
    public void AddImportedContentToListAddsContentToMatchingList(ModificationType kind)
    {
        MainWindowViewModel viewModel = CreateViewModel();
        LauncherContent importedModification = CreateModification(
            kind.ToString(),
            kind switch
            {
                ModificationType.Patch => ModificationType.Patch,
                ModificationType.Addon => ModificationType.Addon,
                _ => ModificationType.Mod
            });
        LauncherManualImportResult importResult = new(kind, importedModification);

        viewModel.AddImportedContentToList(importResult);

        IReadOnlyList<ModificationViewModel> targetList = kind switch
        {
            ModificationType.Patch => viewModel.PatchesListSource,
            ModificationType.Addon => viewModel.AddonsListSource,
            _ => viewModel.ModsListSource
        };
        targetList.Should().ContainSingle();
        targetList[0].ContainerModification.Name.Should().Be(importedModification.Name);
    }

    [Fact]
    public void SaveLauncherData_WhenOnlyOriginalGameContentIsSelected_ProjectsAndSaves()
    {
        var catalog = new FakeLauncherContentCatalog();
        catalog.Data.AddOrUpdate(new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation { Installed = true, IsSelected = true },
            ModificationType = ModificationType.Patch,
            Name = "Original Patch",
            Version = "1.0",
            ParentContentName = LauncherContentKey.OriginalGame.Name,
        });
        MainWindowViewModel viewModel = CreateViewModel(catalog: catalog);
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
        MainWindowViewModel viewModel = modificationType == ModificationType.Addon
            ? CreateViewModel(addons: new[] { CreateModification("Addon", ModificationType.Addon) })
            : CreateViewModel(patches: new[] { CreateModification("Patch", ModificationType.Patch) });
        if (modificationType == ModificationType.Addon)
        {
            viewModel.RefreshAddonsList();
        }
        else
        {
            viewModel.RefreshPatchesList();
        }

        IReadOnlyList<ModificationViewModel> childList = modificationType == ModificationType.Addon
            ? viewModel.AddonsListSource
            : viewModel.PatchesListSource;
        ModificationViewModel child = childList[0];
        child.IsSelected = true;

        viewModel.RemoveContentFromList(child);

        childList.Should().BeEmpty();
        child.IsSelected.Should().BeFalse();
    }

    private static MainWindowViewModel CreateViewModel(
        RecordingLauncherPreferencesService? preferencesService = null,
        IReadOnlyList<LauncherContent>? mods = null,
        IReadOnlyList<LauncherContent>? patches = null,
        IReadOnlyList<LauncherContent>? addons = null,
        FakeLauncherContentCatalog? catalog = null,
        IGameExecutableDiscoveryService? executableDiscovery = null,
        LauncherPackageActivityService? packageActivityService = null,
        LauncherLaunchCoordinator? launchCoordinator = null,
        bool connected = true,
        SupportedGame managedGame = SupportedGame.ZeroHour)
    {
        FakeLauncherContentCatalog resolvedCatalog = catalog ?? new FakeLauncherContentCatalog();
        LauncherPackageActivityService resolvedPackageActivityService =
            packageActivityService ?? new LauncherPackageActivityService();
        PopulateCatalog(resolvedCatalog.Data, mods, patches, addons);
        IGameExecutableDiscoveryService resolvedExecutableDiscovery =
            executableDiscovery ?? Substitute.For<IGameExecutableDiscoveryService>();
        LauncherPaths launcherPaths = CreateLauncherPaths(managedGame);
        var runtimeContext = new LauncherRuntimeContext(
            TestLauncherPaths.CreateRuntimePathContext(launcherPaths),
            "1.2.3");
        runtimeContext.Connected = connected;
        ColorsInfo colors = TestLauncherTheme.Create();
        runtimeContext.Colors = colors;
        var stringLocalizer = new TestStringLocalizer(new Dictionary<string, string>
        {
            ["AddAddonFromFiles"] = "Add addon for {0}",
            ["AddPatchFromFiles"] = "Add patch for {0}",
            ["Addons"] = "Add-ons for ",
            ["ChangeToFullScreen"] = "Change to full screen",
            ["ChangeToNormalStart"] = "Change to normal start",
            ["ChangeToQuickStart"] = "Change to quick start",
            ["ChangeToWindowed"] = "Change to windowed",
            ["CurrentVersion"] = "Current version: ",
            ["GeneralsShortName"] = "Generals",
            ["GeneralsOnlineClient"] = "GeneralsOnline client",
            ["NoSupportedClient"] = "No supported client",
            ["NoWorldBuildersFound"] = "No World Builders Found",
            ["Patches"] = "Patches for ",
            ["Preparing"] = "Preparing",
            ["RunningProcessStatus"] = "Running {0}",
            ["RunningProcessUnknown"] = "Unknown process",
            ["SuperHackersClient"] = "SuperHackers client",
            ["SuperHackersWorldBuilder"] = "SuperHackers World Builder",
            ["VanillaWorldBuilder"] = "Vanilla World Builder",
            ["ZeroHourShortName"] = "Zero Hour",
        });

        RecordingLauncherPreferencesService resolvedPreferencesService =
            preferencesService ?? new RecordingLauncherPreferencesService(new LauncherPreferences());

        return new MainWindowViewModel(
            resolvedPreferencesService,
            new LauncherExecutableSelectionService(
                resolvedExecutableDiscovery,
                runtimeContext,
                resolvedPreferencesService,
                stringLocalizer),
            resolvedCatalog,
            runtimeContext,
            stringLocalizer,
            new ModificationImageSourceFactory(NullLogger<ModificationImageSourceFactory>.Instance),
            Substitute.For<IModificationImageFileService>(),
            resolvedPackageActivityService,
            NullLogger<ModificationViewModel>.Instance,
            launchCoordinator ?? TestLauncherLaunchCoordinator.Create(
                resolvedPackageActivityService,
                catalog: resolvedCatalog,
                stringLocalizer: stringLocalizer),
            NullLogger<MainWindowViewModel>.Instance);
    }

    private static void PopulateCatalog(
        LauncherData data,
        IReadOnlyList<LauncherContent>? mods,
        IReadOnlyList<LauncherContent>? patches,
        IReadOnlyList<LauncherContent>? addons)
    {
        foreach (LauncherContent modification in mods ?? Array.Empty<LauncherContent>())
        {
            AddModification(data, modification);
        }

        IReadOnlyList<LauncherContent> childContent = (patches ?? Array.Empty<LauncherContent>())
            .Concat(addons ?? Array.Empty<LauncherContent>())
            .ToList();
        string? selectedParentName = childContent
            .Select(modification => modification.ContentKey.ParentIdentity)
            .FirstOrDefault(parentContentName => !String.IsNullOrWhiteSpace(parentContentName));
        if (!String.IsNullOrWhiteSpace(selectedParentName))
        {
            LauncherContent? parent = data.Modifications.FirstOrDefault(modification =>
                String.Equals(modification.Name, selectedParentName, StringComparison.OrdinalIgnoreCase));
            if (parent == null)
            {
                AddModification(
                    data,
                    CreateModification(selectedParentName, ModificationType.Mod),
                    isSelected: true);
            }
            else
            {
                parent.IsSelected = true;
                parent.Versions[0].Installation.IsSelected = true;
            }
        }

        foreach (LauncherContent patch in patches ?? Array.Empty<LauncherContent>())
        {
            AddModification(data, patch, defaultParentContentName: LauncherContentKey.OriginalGame.Name);
        }

        foreach (LauncherContent addon in addons ?? Array.Empty<LauncherContent>())
        {
            AddModification(data, addon, defaultParentContentName: LauncherContentKey.OriginalGame.Name);
        }
    }

    private static void AddModification(
        LauncherData data,
        LauncherContent modification,
        bool isSelected = false,
        string? defaultParentContentName = null)
    {
        foreach (LauncherContentVersion version in modification.Versions)
        {
            version.Installation.IsSelected = isSelected;
            data.AddOrUpdate(version);
        }

        IReadOnlyList<LauncherContent> collection = modification.ModificationType switch
        {
            ModificationType.Addon => data.Addons,
            ModificationType.Mod => data.Modifications,
            ModificationType.Patch => data.Patches,
            _ => throw new InvalidOperationException("Only persistent launcher content can populate the catalog."),
        };
        string? effectiveParentContentName = String.IsNullOrWhiteSpace(modification.ContentKey.ParentIdentity)
            ? defaultParentContentName
            : modification.ContentKey.ParentIdentity;
        LauncherContent savedModification = collection.Single(candidate =>
            String.Equals(candidate.Name, modification.Name, StringComparison.OrdinalIgnoreCase) &&
            (modification.ModificationType == ModificationType.Mod ||
             String.Equals(
                 candidate.ContentKey.ParentIdentity,
                 effectiveParentContentName,
                 StringComparison.OrdinalIgnoreCase)));
        savedModification.NumberInList = modification.NumberInList;
        savedModification.IsSelected = isSelected;
    }

    private static LauncherPaths CreateLauncherPaths(SupportedGame game = SupportedGame.ZeroHour)
    {
        return TestLauncherPaths.Create(game: game);
    }

    private static LauncherPreferences CreatePreferences(LauncherGamePreferences zeroHour)
    {
        return new LauncherPreferences
        {
            Games = new LauncherGamePreferencesSet { ZeroHour = zeroHour },
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

    private static LauncherContent CreateModification(
        string name,
        ModificationType modificationType,
        string parentContentName = "",
        bool installed = false,
        int numberInList = 0,
        string versionName = "1.0")
    {
        string effectiveParentContentName =
            modificationType == ModificationType.Mod
                ? string.Empty
                : String.IsNullOrWhiteSpace(parentContentName)
                    ? LauncherContentKey.OriginalGame.Name
                    : parentContentName;
        var version = new LauncherContentVersion(new LauncherContentInstallation
        {
            Installed = installed,
        })
        {
            Name = name,
            Version = versionName,
            ModificationType = modificationType,
            ParentContentName = effectiveParentContentName
        };

        return new LauncherContent(version)
        {
            NumberInList = numberInList,
        };
    }

    private sealed class RecordingLauncherPreferencesService : ILauncherPreferencesService
    {
        public RecordingLauncherPreferencesService(LauncherPreferences preferences)
        {
            Current = preferences;
        }

        public event EventHandler<LauncherPreferences>? PreferencesChanged;

        public LauncherPreferences Current { get; private set; }

        public List<LauncherPreferences> Updates { get; } = new();

        public Exception? UpdateFailure { get; init; }

        public void Update(LauncherPreferences preferences)
        {
            if (UpdateFailure != null)
            {
                throw UpdateFailure;
            }

            Current = preferences;
            Updates.Add(preferences);
            PreferencesChanged?.Invoke(this, preferences);
        }
    }
}
