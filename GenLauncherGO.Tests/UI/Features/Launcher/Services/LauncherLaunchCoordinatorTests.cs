using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Shared.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Services;

public sealed class LauncherLaunchCoordinatorTests
{
    [Fact]
    public void LaunchAsyncWhenPackageActivityIsActiveStopsBeforeVerification()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPackageActivityService packageActivityService = new();
            packageActivityService.TryBegin("Download", out LauncherPackageActivityService.LauncherPackageActivityLease? lease)
                .Should().BeTrue();
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            ILaunchContentIntegrityResolutionService resolutionService =
                Substitute.For<ILaunchContentIntegrityResolutionService>();
            LauncherLaunchCoordinator coordinator = CreateCoordinator(
                packageActivityService: packageActivityService,
                resolutionService: resolutionService,
                dialogService: dialogService);

            try
            {
                LauncherLaunchResult result = await LaunchAsync(coordinator, CreateRequest(), CancellationToken.None);

                result.LaunchStarted.Should().BeFalse();
                result.FailureKind.Should().Be(LauncherLaunchFailureKind.VerificationAlreadyRunning);
                coordinator.IsLaunchInProgress.Should().BeFalse();
                await resolutionService.DidNotReceive().VerifyAsync(
                    Arg.Any<LaunchContentIntegrityTargetRequest>(),
                    Arg.Any<CancellationToken>());
                await dialogService.Received(1).ShowErrorAsync(
                    Arg.Is<LauncherInfoDialogRequest>(request =>
                        request != null &&
                        request.MainMessage == "Launch aborted" &&
                        request.DetailMessage == "Verification running"));
            }
            finally
            {
                lease?.Dispose();
            }
        });
    }

    [Fact]
    public void LaunchAsyncWhenIntegrityReviewIsCanceledStopsBeforePreparation()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowIntegrityReviewAsync(
                    Arg.Any<ContentIntegrityReport>(),
                    Arg.Any<Window?>())
                .Returns(false);
            ILaunchPreparationService preparationService = Substitute.For<ILaunchPreparationService>();
            LauncherLaunchCoordinator coordinator = CreateCoordinator(
                launchPreparationService: preparationService,
                resolutionService: CreateResolutionServiceWithIssues(),
                dialogService: dialogService);

            LauncherLaunchResult result = await LaunchAsync(
                coordinator,
                CreateRequest(),
                CancellationToken.None);

            result.LaunchStarted.Should().BeFalse();
            result.FailureKind.Should().Be(LauncherLaunchFailureKind.VerificationCanceled);
            coordinator.IsPreparingLaunch.Should().BeFalse();
            preparationService.DidNotReceive().Prepare(
                Arg.Any<LaunchPreparationRequest>(),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void LaunchAsyncWhenPreparationFailsShowsInstallErrorAndCleansUp()
    {
        StaTestRunner.Run(async () =>
        {
            ILaunchPreparationService preparationService = Substitute.For<ILaunchPreparationService>();
            preparationService.Prepare(
                    Arg.Any<LaunchPreparationRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(false);
            preparationService.Cleanup(Arg.Any<LauncherPaths>(), Arg.Any<CancellationToken>())
                .Returns(true);
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherLaunchCoordinator coordinator = CreateCoordinator(
                launchPreparationService: preparationService,
                dialogService: dialogService);
            var preparingStates = new List<bool>();
            coordinator.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(LauncherLaunchCoordinator.IsPreparingLaunch))
                {
                    preparingStates.Add(coordinator.IsPreparingLaunch);
                }
            };

            LauncherLaunchResult result = await LaunchAsync(
                coordinator,
                CreateRequest(),
                CancellationToken.None);

            result.LaunchStarted.Should().BeFalse();
            result.FailureKind.Should().Be(LauncherLaunchFailureKind.PreparationFailed);
            preparingStates.Should().Equal(true, false);
            coordinator.IsPreparingLaunch.Should().BeFalse();
            preparationService.Received(1).Cleanup(
                Arg.Any<LauncherPaths>(),
                Arg.Any<CancellationToken>());
            await dialogService.Received(1).ShowErrorAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Files corrupted" &&
                    request.DetailMessage == "Reinstall"));
        });
    }

    [Fact]
    public void LaunchAsyncWhenGameLaunchSucceedsBuildsGameLaunchRequestAndCleansUp()
    {
        StaTestRunner.Run(async () =>
        {
            ILaunchPreparationService preparationService = CreateSuccessfulPreparationService();
            IGameProcessLauncher processLauncher = Substitute.For<IGameProcessLauncher>();
            GameLaunchRequest? capturedRequest = null;
            processLauncher.StartAsync(
                    Arg.Do<GameLaunchRequest>(request => capturedRequest = request),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(CreateProcessOperation(
                    succeeded: true,
                    "generals.exe")));
            LauncherLaunchCoordinator coordinator = CreateCoordinator(
                launcherPreferencesService: CreatePreferencesService(
                    CreateZeroHourPreferences(new LauncherGamePreferences
                    {
                        GameArguments = "-win",
                    })),
                launchPreparationService: preparationService,
                gameProcessLauncher: processLauncher);

            LauncherLaunchResult result = await LaunchAsync(
                coordinator,
                CreateRequest(useGeneralsOnline: false),
                CancellationToken.None);

            result.LaunchStarted.Should().BeTrue();
            result.ProcessSucceeded.Should().BeTrue();
            capturedRequest.Should().NotBeNull();
            capturedRequest!.TargetKind.Should().Be(GameLaunchTargetKind.GameClient);
            capturedRequest.ExecutableName.Should().Be("generals.exe");
            capturedRequest.Arguments.Should().Be("-win");
            preparationService.Received(1).Cleanup(
                Arg.Any<LauncherPaths>(),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void LaunchAsyncForGeneralsOnlinePreservesNoArgumentsBehavior()
    {
        StaTestRunner.Run(async () =>
        {
            GameLaunchRequest? capturedRequest = null;
            IGameProcessLauncher processLauncher = Substitute.For<IGameProcessLauncher>();
            processLauncher.StartAsync(
                    Arg.Do<GameLaunchRequest>(request => capturedRequest = request),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(CreateProcessOperation(
                    succeeded: true,
                    "generalsonlinezh.exe")));
            LauncherLaunchCoordinator coordinator = CreateCoordinator(
                launcherPreferencesService: CreatePreferencesService(
                    CreateZeroHourPreferences(new LauncherGamePreferences
                    {
                        GameArguments = "-quickstart -win",
                    })),
                launchPreparationService: CreateSuccessfulPreparationService(),
                gameProcessLauncher: processLauncher);

            LauncherLaunchResult result = await LaunchAsync(
                coordinator,
                CreateRequest(
                    executableName: "generalsonlinezh.exe",
                    useGeneralsOnline: true),
                CancellationToken.None);

            result.LaunchStarted.Should().BeTrue();
            capturedRequest.Should().NotBeNull();
            capturedRequest!.ExecutableName.Should().Be("generalsonlinezh.exe");
            capturedRequest.Arguments.Should().BeEmpty();
        });
    }

    [Fact]
    public void LaunchAsyncForModdedGameRequestsBaseGameScriptDisable()
    {
        StaTestRunner.Run(async () =>
        {
            ILaunchPreparationService preparationService = Substitute.For<ILaunchPreparationService>();
            LaunchPreparationRequest? capturedPreparationRequest = null;
            preparationService.Prepare(
                    Arg.Do<LaunchPreparationRequest>(request => capturedPreparationRequest = request),
                    Arg.Any<CancellationToken>())
                .Returns(true);
            preparationService.Cleanup(Arg.Any<LauncherPaths>(), Arg.Any<CancellationToken>())
                .Returns(true);
            LauncherLaunchCoordinator coordinator = CreateCoordinator(launchPreparationService: preparationService);

            LauncherLaunchResult result = await LaunchAsync(
                coordinator,
                CreateRequest(activeVersions: new[]
                {
                    new LauncherContentVersion
                    {
                        ModificationType = ModificationType.Mod,
                        Name = "Rise",
                        Version = "1.0"
                    }
                }),
                CancellationToken.None);

            result.LaunchStarted.Should().BeTrue();
            capturedPreparationRequest.Should().NotBeNull();
            capturedPreparationRequest!.DisableBaseGameScriptFiles.Should().BeTrue();
        });
    }

    [Fact]
    public void LaunchAsyncForUnmoddedGameDoesNotRequestBaseGameScriptDisable()
    {
        StaTestRunner.Run(async () =>
        {
            ILaunchPreparationService preparationService = Substitute.For<ILaunchPreparationService>();
            LaunchPreparationRequest? capturedPreparationRequest = null;
            preparationService.Prepare(
                    Arg.Do<LaunchPreparationRequest>(request => capturedPreparationRequest = request),
                    Arg.Any<CancellationToken>())
                .Returns(true);
            preparationService.Cleanup(Arg.Any<LauncherPaths>(), Arg.Any<CancellationToken>())
                .Returns(true);
            LauncherLaunchCoordinator coordinator = CreateCoordinator(launchPreparationService: preparationService);

            LauncherLaunchResult result = await LaunchAsync(coordinator, CreateRequest(), CancellationToken.None);

            result.LaunchStarted.Should().BeTrue();
            capturedPreparationRequest.Should().NotBeNull();
            capturedPreparationRequest!.DisableBaseGameScriptFiles.Should().BeFalse();
        });
    }

    [Fact]
    public void LaunchAsyncForModdedWorldBuilderRequestsBaseGameScriptDisable()
    {
        StaTestRunner.Run(async () =>
        {
            ILaunchPreparationService preparationService = Substitute.For<ILaunchPreparationService>();
            LaunchPreparationRequest? capturedPreparationRequest = null;
            preparationService.Prepare(
                    Arg.Do<LaunchPreparationRequest>(request => capturedPreparationRequest = request),
                    Arg.Any<CancellationToken>())
                .Returns(true);
            preparationService.Cleanup(Arg.Any<LauncherPaths>(), Arg.Any<CancellationToken>())
                .Returns(true);
            LauncherLaunchCoordinator coordinator = CreateCoordinator(launchPreparationService: preparationService);

            LauncherLaunchResult result = await LaunchAsync(
                coordinator,
                CreateRequest(
                    targetKind: GameLaunchTargetKind.WorldBuilder,
                    activeVersions: new[]
                    {
                        new LauncherContentVersion
                        {
                            ModificationType = ModificationType.Mod,
                            Name = "Rise",
                            Version = "1.0"
                        }
                    }),
                CancellationToken.None);

            result.LaunchStarted.Should().BeTrue();
            capturedPreparationRequest.Should().NotBeNull();
            capturedPreparationRequest!.DisableBaseGameScriptFiles.Should().BeTrue();
        });
    }

    [Fact]
    public void LaunchAsyncForUnmoddedWorldBuilderDoesNotRequestBaseGameScriptDisable()
    {
        StaTestRunner.Run(async () =>
        {
            ILaunchPreparationService preparationService = Substitute.For<ILaunchPreparationService>();
            LaunchPreparationRequest? capturedPreparationRequest = null;
            preparationService.Prepare(
                    Arg.Do<LaunchPreparationRequest>(request => capturedPreparationRequest = request),
                    Arg.Any<CancellationToken>())
                .Returns(true);
            preparationService.Cleanup(Arg.Any<LauncherPaths>(), Arg.Any<CancellationToken>())
                .Returns(true);
            LauncherLaunchCoordinator coordinator = CreateCoordinator(launchPreparationService: preparationService);

            LauncherLaunchResult result = await LaunchAsync(
                coordinator,
                CreateRequest(targetKind: GameLaunchTargetKind.WorldBuilder),
                CancellationToken.None);

            result.LaunchStarted.Should().BeTrue();
            capturedPreparationRequest.Should().NotBeNull();
            capturedPreparationRequest!.DisableBaseGameScriptFiles.Should().BeFalse();
        });
    }

    [Fact]
    public void LaunchAsyncWhenWorldBuilderLaunchSucceedsUsesSelectedExecutableAndArguments()
    {
        StaTestRunner.Run(async () =>
        {
            IGameProcessLauncher processLauncher = Substitute.For<IGameProcessLauncher>();
            GameLaunchRequest? capturedRequest = null;
            processLauncher.StartAsync(
                    Arg.Do<GameLaunchRequest>(request => capturedRequest = request),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(CreateProcessOperation(
                    succeeded: true,
                    "worldbuilder.exe")));
            LauncherLaunchCoordinator coordinator = CreateCoordinator(
                launcherPreferencesService: CreatePreferencesService(
                    CreateZeroHourPreferences(new LauncherGamePreferences
                    {
                        WorldBuilderArguments = "-wb",
                    })),
                gameProcessLauncher: processLauncher);

            LauncherLaunchResult result = await LaunchAsync(
                coordinator,
                CreateRequest(
                    targetKind: GameLaunchTargetKind.WorldBuilder,
                    executableName: "worldbuilder.exe"),
                CancellationToken.None);

            result.LaunchStarted.Should().BeTrue();
            capturedRequest.Should().NotBeNull();
            capturedRequest!.TargetKind.Should().Be(GameLaunchTargetKind.WorldBuilder);
            capturedRequest.ExecutableName.Should().Be("worldbuilder.exe");
            capturedRequest.Arguments.Should().Be("-wb");
        });
    }

    [Fact]
    public void LaunchAsyncExposesTrackedProcessUntilCompletionAndForceClosesActiveProcess()
    {
        StaTestRunner.Run(async () =>
        {
            IGameProcessLauncher processLauncher = Substitute.For<IGameProcessLauncher>();
            IGameProcessLaunchOperation operation = Substitute.For<IGameProcessLaunchOperation>();
            var processCompletion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var processExposed = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            operation.CurrentExecutableName.Returns("generals.exe");
            operation.Completion.Returns(processCompletion.Task);
            processLauncher.StartAsync(Arg.Any<GameLaunchRequest>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(operation));
            LauncherLaunchCoordinator coordinator = CreateCoordinator(gameProcessLauncher: processLauncher);
            coordinator.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(LauncherLaunchCoordinator.ActiveProcessName) &&
                    coordinator.HasActiveProcess &&
                    coordinator.ActiveProcessName is { } processName)
                {
                    processExposed.TrySetResult(processName);
                }
            };

            Task<LauncherLaunchResult> launchTask = LaunchAsync(
                coordinator,
                CreateRequest(),
                CancellationToken.None);

            string shownProcessName = await processExposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            shownProcessName.Should().Be("generals.exe");
            coordinator.HasActiveProcess.Should().BeTrue();
            coordinator.ActiveProcessName.Should().Be("generals.exe");

            coordinator.ForceCloseActiveProcess().Should().BeTrue();
            operation.Received(1).ForceClose();

            processCompletion.SetResult(true);
            LauncherLaunchResult result = await launchTask;

            result.ProcessSucceeded.Should().BeTrue();
            coordinator.HasActiveProcess.Should().BeFalse();
            coordinator.ActiveProcessName.Should().BeNull();
        });
    }

    [Fact]
    public void LaunchAsyncWhenHideLauncherAfterGameStartIsEnabledExposesWindowVisibilityState()
    {
        StaTestRunner.Run(async () =>
        {
            IGameProcessLauncher processLauncher = Substitute.For<IGameProcessLauncher>();
            IGameProcessLaunchOperation operation = Substitute.For<IGameProcessLaunchOperation>();
            var processStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var processCompletion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var windowVisibilityStates = new List<bool>();
            var hideRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            operation.CurrentExecutableName.Returns("generals.exe");
            operation.Completion.Returns(processCompletion.Task);
            processLauncher.StartAsync(Arg.Any<GameLaunchRequest>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    processStarted.SetResult();
                    return Task.FromResult(operation);
                });
            LauncherLaunchCoordinator coordinator = CreateCoordinator(
                launcherPreferencesService: CreatePreferencesService(new LauncherPreferences
                {
                    Shared = new LauncherSharedPreferences
                    {
                        HideLauncherAfterGameStart = true,
                    },
                }),
                gameProcessLauncher: processLauncher);
            coordinator.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(LauncherLaunchCoordinator.ShouldHideLauncherWindow))
                {
                    windowVisibilityStates.Add(coordinator.ShouldHideLauncherWindow);
                    if (coordinator.ShouldHideLauncherWindow)
                    {
                        hideRequested.TrySetResult();
                    }
                }
            };

            Task<LauncherLaunchResult> launchTask = LaunchAsync(
                coordinator,
                CreateRequest(),
                CancellationToken.None);

            await hideRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await processStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            coordinator.ShouldHideLauncherWindow.Should().BeTrue();
            windowVisibilityStates.Should().Equal(true);

            processCompletion.SetResult(true);
            LauncherLaunchResult result = await launchTask.WaitAsync(TimeSpan.FromSeconds(5));

            result.ProcessSucceeded.Should().BeTrue();
            coordinator.ShouldHideLauncherWindow.Should().BeFalse();
            windowVisibilityStates.Should().Equal(true, false);
        });
    }

    [Fact]
    public void LaunchAsyncUpdatesActiveProcessNameWhenTrackedCurrentProcessChanges()
    {
        StaTestRunner.Run(async () =>
        {
            IGameProcessLauncher processLauncher = Substitute.For<IGameProcessLauncher>();
            var operation = new ControllableGameProcessLaunchOperation("generalsonlinezh.exe");
            var initialProcessExposed = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var gameProcessExposed = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var exposedProcessNames = new List<string>();
            processLauncher.StartAsync(Arg.Any<GameLaunchRequest>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IGameProcessLaunchOperation>(operation));
            LauncherLaunchCoordinator coordinator = CreateCoordinator(gameProcessLauncher: processLauncher);
            coordinator.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(LauncherLaunchCoordinator.ActiveProcessName) ||
                    coordinator.ActiveProcessName is not { } processName)
                {
                    return;
                }

                exposedProcessNames.Add(processName);
                initialProcessExposed.TrySetResult(processName);
                if (processName == "generalszh.exe")
                {
                    gameProcessExposed.TrySetResult();
                }
            };

            Task<LauncherLaunchResult> launchTask = LaunchAsync(
                coordinator,
                CreateRequest(),
                CancellationToken.None);

            string initialProcessName = await initialProcessExposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            initialProcessName.Should().Be("generalsonlinezh.exe");

            operation.SetCurrentExecutableName("generalszh.exe");
            await gameProcessExposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            coordinator.ActiveProcessName.Should().Be("generalszh.exe");

            operation.Complete(succeeded: true);
            LauncherLaunchResult result = await launchTask;

            result.ProcessSucceeded.Should().BeTrue();
            exposedProcessNames.Should().Equal("generalsonlinezh.exe", "generalszh.exe");
        });
    }

    [Fact]
    public void LaunchAsyncExposesActiveWorkflowDuringIntegrityVerification()
    {
        StaTestRunner.Run(async () =>
        {
            ILaunchContentIntegrityResolutionService resolutionService =
                Substitute.For<ILaunchContentIntegrityResolutionService>();
            var verificationStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var verificationCompletion =
                new TaskCompletionSource<LaunchContentIntegrityVerificationResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            resolutionService.VerifyAsync(
                    Arg.Any<LaunchContentIntegrityTargetRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    verificationStarted.TrySetResult();
                    return verificationCompletion.Task;
                });
            LauncherLaunchCoordinator coordinator = CreateCoordinator(
                resolutionService: resolutionService);
            var activeStates = new List<bool>();
            coordinator.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(LauncherLaunchCoordinator.IsLaunchInProgress))
                {
                    activeStates.Add(coordinator.IsLaunchInProgress);
                }
            };

            Task<LauncherLaunchResult> launchTask = LaunchAsync(
                coordinator,
                CreateRequest(),
                CancellationToken.None);
            await verificationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            coordinator.IsLaunchInProgress.Should().BeTrue();
            verificationCompletion.SetResult(new LaunchContentIntegrityVerificationResult(
                new ContentIntegrityReport(Array.Empty<ContentIntegrityIssue>()),
                Array.Empty<LaunchContentIntegrityTargetContext>()));
            await launchTask;

            coordinator.IsLaunchInProgress.Should().BeFalse();
            activeStates.Should().Equal(true, false);
        });
    }

    private static LauncherLaunchCoordinator CreateCoordinator(
        ILauncherPreferencesService? launcherPreferencesService = null,
        ILaunchPreparationService? launchPreparationService = null,
        IGameProcessLauncher? gameProcessLauncher = null,
        ILaunchContentIntegrityResolutionService? resolutionService = null,
        LauncherPackageActivityService? packageActivityService = null,
        ILauncherDialogService? dialogService = null)
    {
        ILauncherDialogService resolvedDialogService = dialogService ?? Substitute.For<ILauncherDialogService>();
        LauncherPackageActivityService resolvedPackageActivityService = packageActivityService ?? new();
        LauncherRuntimePathContext runtimePaths =
            TestLauncherPaths.CreateRuntimePathContext(TestLauncherPaths.Create());

        return new LauncherLaunchCoordinator(
            launcherPreferencesService ?? CreatePreferencesService(new LauncherPreferences()),
            launchPreparationService ?? CreateSuccessfulPreparationService(),
            gameProcessLauncher ?? CreateSuccessfulProcessLauncher(),
            CreateIntegrityCoordinator(
                resolutionService ?? CreateNoIssueResolutionService(),
                resolvedPackageActivityService,
                resolvedDialogService,
                runtimePaths),
            resolvedPackageActivityService,
            runtimePaths,
            CreateStringLocalizer(),
            resolvedDialogService,
            NullLogger<LauncherLaunchCoordinator>.Instance);
    }

    private static LauncherLaunchRequest CreateRequest(
        GameLaunchTargetKind targetKind = GameLaunchTargetKind.GameClient,
        string executableName = "generals.exe",
        bool useGeneralsOnline = false,
        IReadOnlyList<LauncherContentVersion>? activeVersions = null)
    {
        return new LauncherLaunchRequest(
            targetKind,
            executableName,
            useGeneralsOnline,
            activeVersions ?? Array.Empty<LauncherContentVersion>());
    }

    private static Task<LauncherLaunchResult> LaunchAsync(
        LauncherLaunchCoordinator coordinator,
        LauncherLaunchRequest request,
        CancellationToken cancellationToken)
    {
        return coordinator.LaunchAsync(
            request,
            Array.Empty<ILaunchContentIntegrityProgressTarget>(),
            new Window(),
            cancellationToken);
    }

    private static ILauncherPreferencesService CreatePreferencesService(LauncherPreferences preferences)
    {
        ILauncherPreferencesService preferencesService = Substitute.For<ILauncherPreferencesService>();
        preferencesService.Current.Returns(preferences);
        return preferencesService;
    }

    private static ILaunchPreparationService CreateSuccessfulPreparationService()
    {
        ILaunchPreparationService preparationService = Substitute.For<ILaunchPreparationService>();
        preparationService.Prepare(
                Arg.Any<LaunchPreparationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        preparationService.Cleanup(Arg.Any<LauncherPaths>(), Arg.Any<CancellationToken>())
            .Returns(true);
        return preparationService;
    }

    private static IGameProcessLauncher CreateSuccessfulProcessLauncher()
    {
        IGameProcessLauncher processLauncher = Substitute.For<IGameProcessLauncher>();
        processLauncher.StartAsync(
                Arg.Any<GameLaunchRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateProcessOperation(
                succeeded: true,
                "generals.exe")));
        return processLauncher;
    }

    private static IGameProcessLaunchOperation CreateProcessOperation(
        bool succeeded,
        string executableName)
    {
        return new TestGameProcessLaunchOperation(succeeded, executableName);
    }

    private static LaunchContentIntegrityCoordinator CreateIntegrityCoordinator(
        ILaunchContentIntegrityResolutionService resolutionService,
        LauncherPackageActivityService packageActivityService,
        ILauncherDialogService dialogService,
        LauncherRuntimePathContext runtimePaths)
    {
        return new LaunchContentIntegrityCoordinator(
            resolutionService,
            new FakeLauncherContentCatalog(),
            runtimePaths,
            packageActivityService,
            CreateStringLocalizer(),
            dialogService,
            NullLogger<LaunchContentIntegrityCoordinator>.Instance);
    }

    private static LauncherPreferences CreateZeroHourPreferences(LauncherGamePreferences preferences)
    {
        return new LauncherPreferences
        {
            Games = new LauncherGamePreferencesSet { ZeroHour = preferences },
        };
    }

    private static ILaunchContentIntegrityResolutionService CreateNoIssueResolutionService()
    {
        ILaunchContentIntegrityResolutionService resolutionService =
            Substitute.For<ILaunchContentIntegrityResolutionService>();
        resolutionService.VerifyAsync(
                Arg.Any<LaunchContentIntegrityTargetRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new LaunchContentIntegrityVerificationResult(
                new ContentIntegrityReport(Array.Empty<ContentIntegrityIssue>()),
                Array.Empty<LaunchContentIntegrityTargetContext>())));
        return resolutionService;
    }

    private static ILaunchContentIntegrityResolutionService CreateResolutionServiceWithIssues()
    {
        ILaunchContentIntegrityResolutionService resolutionService =
            Substitute.For<ILaunchContentIntegrityResolutionService>();
        resolutionService.VerifyAsync(
                Arg.Any<LaunchContentIntegrityTargetRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new LaunchContentIntegrityVerificationResult(
                new ContentIntegrityReport(new[]
                {
                    new ContentIntegrityIssue(
                        "target",
                        "Shockwave",
                        ContentSourceKind.Manual,
                        IntegrityIssueKind.ModifiedFile,
                        IntegrityIssueAction.Absorb,
                        "Data/file.big")
                }),
                Array.Empty<LaunchContentIntegrityTargetContext>())));
        resolutionService.InitializeUntrackedManagedCachesAsync(
                Arg.Any<LaunchContentIntegrityResolutionRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));
        return resolutionService;
    }

    private static ILauncherStringLocalizer CreateStringLocalizer()
    {
        return new TestStringLocalizer(new Dictionary<string, string>
        {
            ["AbsorbAndLaunch"] = "Absorb and launch",
            ["CancelLaunch"] = "Cancel launch",
            ["FilesCorrupted"] = "Files corrupted",
            ["GameRunning"] = "Game running",
            ["IntegrityAbsorbGroup"] = "Absorb",
            ["IntegrityBlockGroup"] = "Blocked",
            ["IntegrityBlockedDescription"] = "Blocked",
            ["IntegrityCacheSuffix"] = " cache",
            ["IntegrityDeleteGroup"] = "Delete",
            ["IntegrityDialogTitle"] = "Integrity",
            ["IntegrityManualDescription"] = "Absorb {0}",
            ["IntegrityRepairGroup"] = "Repair",
            ["IntegrityRedownloadGroup"] = "Redownload",
            ["IntegrityTrustGroup"] = "Trust",
            ["LaunchAborted"] = "Launch aborted",
            ["LaunchVerificationRunning"] = "Verification running",
            ["Reinstall"] = "Reinstall",
            ["WorldBuilderRunning"] = "World Builder running",
        });
    }

    private sealed class TestGameProcessLaunchOperation : IGameProcessLaunchOperation
    {
        public TestGameProcessLaunchOperation(
            bool succeeded,
            string executableName)
        {
            CurrentExecutableName = executableName;
            Completion = Task.FromResult(succeeded);
        }

        public string CurrentExecutableName { get; }

        public event EventHandler? CurrentExecutableNameChanged
        {
            add { }
            remove { }
        }

        public Task<bool> Completion { get; }

        public void ForceClose()
        {
        }
    }

    private sealed class ControllableGameProcessLaunchOperation : IGameProcessLaunchOperation
    {
        private readonly TaskCompletionSource<bool> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ControllableGameProcessLaunchOperation(string executableName)
        {
            CurrentExecutableName = executableName;
        }

        public string CurrentExecutableName { get; private set; }

        public event EventHandler? CurrentExecutableNameChanged;

        public Task<bool> Completion => _completion.Task;

        public void SetCurrentExecutableName(string executableName)
        {
            CurrentExecutableName = executableName;
            CurrentExecutableNameChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Complete(bool succeeded)
        {
            _completion.SetResult(succeeded);
        }

        public void ForceClose()
        {
        }
    }
}
