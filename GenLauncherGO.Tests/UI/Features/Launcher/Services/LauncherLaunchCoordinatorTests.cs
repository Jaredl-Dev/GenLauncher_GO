using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Launching;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Services;

[Collection("Avalonia")]
public sealed class LauncherLaunchCoordinatorTests
{
    [Fact]
    public void LaunchAsyncWhenAnotherPackageInstall_IsActiveStopsBeforeVerification()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPackageActivityService packageActivityService = new();
            packageActivityService.TryBegin("Resumed Mod",
                    out LauncherPackageActivityService.LauncherPackageActivityLease? lease)
                .Should().BeTrue();
            RecordingLauncherDialogService dialogService = new();
            ILaunchContentIntegrityResolutionService resolutionService =
                Substitute.For<ILaunchContentIntegrityResolutionService>();
            LauncherLaunchCoordinator coordinator = TestLauncherLaunchCoordinator.Create(
                packageActivityService,
                dialogService: dialogService,
                integrityResolutionService: resolutionService);

            try
            {
                LauncherLaunchResult result = await LaunchAsync(
                    coordinator,
                    CreateRequest(activeVersions: [TestLauncherContent.Version("Installed Mod", installed: true)]),
                    CancellationToken.None);

                result.LaunchStarted.Should().BeFalse();
                result.FailureKind.Should().Be(LauncherLaunchFailureKind.PackageActivityInProgress);
                coordinator.IsLaunchInProgress.Should().BeFalse();
                await resolutionService.DidNotReceive().VerifyAsync(
                    Arg.Any<LaunchContentIntegrityTargetRequest>(),
                    Arg.Any<CancellationToken>());
                dialogService.ErrorRequests.Should().ContainSingle().Which.Should()
                    .Match<LauncherInfoDialogRequest>(request =>
                        request.MainMessage == "Launch aborted" &&
                        request.DetailMessage == "Resumed Mod install running");
            }
            finally
            {
                lease?.Dispose();
            }
        });
    }

    /// <summary>
    ///     A paused download is already stopped, so it suspends out of the way instead of refusing the launch. Its
    ///     partial content stays on disk, and verification deliberately skips a version that is waiting to resume.
    /// </summary>
    [Fact]
    public void LaunchAsyncWhenADownload_IsPausedSuspendsItAndContinues()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPackageActivityService packageActivityService = new();
            Task<PackageDownloadResult> pausedDownload = TestPackageDownload.StartPaused(packageActivityService);
            RecordingLauncherDialogService dialogService = new();
            ILaunchContentIntegrityResolutionService resolutionService =
                Substitute.For<ILaunchContentIntegrityResolutionService>();
            LauncherLaunchCoordinator coordinator = TestLauncherLaunchCoordinator.Create(
                packageActivityService,
                dialogService: dialogService,
                integrityResolutionService: resolutionService);

            LauncherLaunchResult result = await LaunchAsync(
                coordinator,
                CreateRequest(activeVersions: [TestLauncherContent.Version("Installed Mod", installed: true)]),
                CancellationToken.None);

            (await pausedDownload).Status.Should().Be(PackageDownloadStatus.Suspended);
            result.FailureKind.Should().NotBe(LauncherLaunchFailureKind.PackageActivityInProgress);
            await resolutionService.Received(1).VerifyAsync(
                Arg.Any<LaunchContentIntegrityTargetRequest>(),
                Arg.Any<CancellationToken>());
        });
    }

    /// <summary>
    ///     Each target owns its own busy message, so a running game and a running World Builder never explain each
    ///     other's refusal.
    /// </summary>
    [Theory]
    [InlineData(GameLaunchTargetKind.GameClient, "Game running")]
    [InlineData(GameLaunchTargetKind.WorldBuilder, "World Builder running")]
    public void LaunchAsync_WhenTheSameTargetIsAlreadyRunning_StopsWithThatTargetsMessage(
        GameLaunchTargetKind targetKind,
        string expectedDetailMessage)
    {
        StaTestRunner.Run(async () =>
        {
            var operation = new ControllableGameProcessLaunchOperation();
            RecordingLauncherDialogService dialogService = new();
            IGameProcessLauncher processLauncher = CreateProcessLauncher(operation);
            LauncherLaunchCoordinator coordinator = TestLauncherLaunchCoordinator.Create(
                processLauncher: processLauncher,
                dialogService: dialogService);
            Task<LauncherLaunchResult> runningLaunch = LaunchAsync(
                coordinator,
                CreateRequest(targetKind),
                CancellationToken.None);
            await WaitForActiveProcessAsync(coordinator);

            LauncherLaunchResult refused = await LaunchAsync(
                coordinator,
                CreateRequest(targetKind),
                CancellationToken.None);

            refused.LaunchStarted.Should().BeFalse();
            refused.FailureKind.Should().Be(LauncherLaunchFailureKind.AlreadyRunning);
            dialogService.ErrorRequests.Should().ContainSingle().Which.Should()
                .Match<LauncherInfoDialogRequest>(request =>
                    request.MainMessage == "Launch aborted" &&
                    request.DetailMessage == expectedDetailMessage);
            await processLauncher.ReceivedWithAnyArgs(1).StartAsync(default!, default);

            operation.Complete(true);
            await runningLaunch.WaitAsync(TestTimeouts.Wait);
        });
    }

    [Fact]
    public void LaunchAsync_WhileTheOtherTargetIsVerifying_StopsWithTheVerificationMessage()
    {
        StaTestRunner.Run(async () =>
        {
            var verificationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var verificationCompletion = new TaskCompletionSource<LaunchContentIntegrityVerificationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            RecordingLauncherDialogService dialogService = new();
            IGameProcessLauncher processLauncher = CreateProcessLauncher(
                new CompletedGameProcessLaunchOperation(true, "generals.exe"));
            LauncherLaunchCoordinator coordinator = TestLauncherLaunchCoordinator.Create(
                processLauncher: processLauncher,
                dialogService: dialogService,
                integrityResolutionService: CreateGatedResolutionService(
                    verificationStarted,
                    verificationCompletion));
            Task<LauncherLaunchResult> gameLaunch = LaunchAsync(
                coordinator,
                CreateRequest(),
                CancellationToken.None);
            await verificationStarted.Task.WaitAsync(TestTimeouts.Wait);

            LauncherLaunchResult worldBuilderLaunch = await LaunchAsync(
                coordinator,
                CreateRequest(GameLaunchTargetKind.WorldBuilder, "worldbuilder.exe"),
                CancellationToken.None);

            worldBuilderLaunch.LaunchStarted.Should().BeFalse();
            worldBuilderLaunch.FailureKind.Should().Be(LauncherLaunchFailureKind.VerificationAlreadyRunning);
            dialogService.ErrorRequests.Should().ContainSingle().Which.Should()
                .Match<LauncherInfoDialogRequest>(request =>
                    request.MainMessage == "Launch aborted" &&
                    request.DetailMessage == "Verification running");
            await processLauncher.DidNotReceiveWithAnyArgs().StartAsync(default!, default);

            verificationCompletion.SetResult(CreateNoIssueVerificationResult());
            await gameLaunch.WaitAsync(TestTimeouts.Wait);
        });
    }

    [Fact]
    public void LaunchAsync_WhenTheOtherTargetVerifiedAndKeptRunning_StartsTheSecondProcess()
    {
        StaTestRunner.Run(async () =>
        {
            var verificationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var verificationCompletion = new TaskCompletionSource<LaunchContentIntegrityVerificationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var gameOperation = new ControllableGameProcessLaunchOperation();
            var startedRequests = new List<GameLaunchRequest>();
            IGameProcessLauncher processLauncher = Substitute.For<IGameProcessLauncher>();
            processLauncher.StartAsync(
                    Arg.Do<GameLaunchRequest>(startedRequests.Add),
                    Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<IGameProcessLaunchOperation>(
                    call.ArgAt<GameLaunchRequest>(0).TargetKind == GameLaunchTargetKind.GameClient
                        ? gameOperation
                        : new CompletedGameProcessLaunchOperation(true, "worldbuilder.exe")));
            LauncherLaunchCoordinator coordinator = TestLauncherLaunchCoordinator.Create(
                processLauncher: processLauncher,
                integrityResolutionService: CreateGatedResolutionService(
                    verificationStarted,
                    verificationCompletion));
            Task<LauncherLaunchResult> gameLaunch = LaunchAsync(
                coordinator,
                CreateRequest(),
                CancellationToken.None);
            await verificationStarted.Task.WaitAsync(TestTimeouts.Wait);
            verificationCompletion.SetResult(CreateNoIssueVerificationResult());
            await WaitForActiveProcessAsync(coordinator);

            LauncherLaunchResult worldBuilderLaunch = await LaunchAsync(
                coordinator,
                CreateRequest(GameLaunchTargetKind.WorldBuilder, "worldbuilder.exe"),
                CancellationToken.None);

            worldBuilderLaunch.LaunchStarted.Should().BeTrue();
            worldBuilderLaunch.ProcessSucceeded.Should().BeTrue();
            startedRequests.Select(request => request.TargetKind).Should()
                .Equal(GameLaunchTargetKind.GameClient, GameLaunchTargetKind.WorldBuilder);

            gameOperation.Complete(true);
            await gameLaunch.WaitAsync(TestTimeouts.Wait);
        });
    }

    [Fact]
    public void LaunchAsyncWhenIntegrityReview_IsCanceledStopsBeforePreparation()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowIntegrityReviewAsync(
                    Arg.Any<ContentIntegrityReport>(),
                    Arg.Any<Window?>())
                .Returns(false);
            FakeLaunchPreparationService preparationService = new();
            LauncherLaunchCoordinator coordinator = TestLauncherLaunchCoordinator.Create(
                preparationService: preparationService,
                dialogService: dialogService,
                integrityResolutionService: CreateResolutionServiceWithIssues());

            LauncherLaunchResult result = await LaunchAsync(
                coordinator,
                CreateRequest(),
                CancellationToken.None);

            result.LaunchStarted.Should().BeFalse();
            result.FailureKind.Should().Be(LauncherLaunchFailureKind.VerificationCanceled);
            coordinator.IsPreparingLaunch.Should().BeFalse();
            preparationService.PrepareRequests.Should().BeEmpty();
        });
    }

    [Fact]
    public void LaunchAsyncWhenPreparation_FailsShowsInstallErrorAndCleansUp()
    {
        StaTestRunner.Run(async () =>
        {
            FakeLaunchPreparationService preparationService = new() { PrepareResult = false };
            RecordingLauncherDialogService dialogService = new();
            LauncherLaunchCoordinator coordinator = TestLauncherLaunchCoordinator.Create(
                preparationService: preparationService,
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
            preparationService.CleanupRequests.Should().ContainSingle();
            dialogService.ErrorRequests.Should().ContainSingle().Which.Should()
                .Match<LauncherInfoDialogRequest>(request =>
                    request.MainMessage == "Files corrupted" &&
                    request.DetailMessage == "Reinstall");
        });
    }

    [Fact]
    public void LaunchAsyncWhenGameLaunchSucceeds_BuildsGameLaunchRequestAndCleansUp()
    {
        StaTestRunner.Run(async () =>
        {
            FakeLaunchPreparationService preparationService = new();
            GameLaunchRequest? capturedRequest = null;
            IGameProcessLauncher processLauncher = Substitute.For<IGameProcessLauncher>();
            processLauncher.StartAsync(
                    Arg.Do<GameLaunchRequest>(request => capturedRequest = request),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IGameProcessLaunchOperation>(
                    new CompletedGameProcessLaunchOperation(true, "generals.exe")));
            LauncherLaunchCoordinator coordinator = TestLauncherLaunchCoordinator.Create(
                preferencesService: CreateZeroHourPreferences(new LauncherGamePreferences
                {
                    GameArguments = "-win"
                }),
                preparationService: preparationService,
                processLauncher: processLauncher);

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
            preparationService.CleanupRequests.Should().ContainSingle();
        });
    }

    [Fact]
    public void LaunchAsyncForModdedGeneralsOnline_ForwardsArgumentsAndDisablesCommunityDataPatch()
    {
        StaTestRunner.Run(async () =>
        {
            GameLaunchRequest? capturedRequest = null;
            IGameProcessLauncher processLauncher = Substitute.For<IGameProcessLauncher>();
            processLauncher.StartAsync(
                    Arg.Do<GameLaunchRequest>(request => capturedRequest = request),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IGameProcessLaunchOperation>(
                    new CompletedGameProcessLaunchOperation(true, "generalsonlinezh.exe")));
            LauncherLaunchCoordinator coordinator = TestLauncherLaunchCoordinator.Create(
                preferencesService: CreateZeroHourPreferences(new LauncherGamePreferences
                {
                    GameArguments = "-quickstart -win"
                }),
                processLauncher: processLauncher);

            LauncherLaunchResult result = await LaunchAsync(
                coordinator,
                CreateRequest(
                    executableName: "generalsonlinezh.exe",
                    useGeneralsOnline: true,
                    activeVersions: [TestLauncherContent.Version("Rise")]),
                CancellationToken.None);

            result.LaunchStarted.Should().BeTrue();
            capturedRequest.Should().NotBeNull();
            capturedRequest!.ExecutableName.Should().Be("generalsonlinezh.exe");
            capturedRequest.Arguments.Should().Be(
                $"-quickstart -win {LauncherGameArgumentService.GeneralsOnlineDisableCommunityDataPatchArgument}");
        });
    }

    [Fact]
    public void LaunchAsyncForVanillaFullscreenGeneralsOnline_ForwardsArgumentsAndAddsFullscreenOverride()
    {
        StaTestRunner.Run(async () =>
        {
            GameLaunchRequest? capturedRequest = null;
            IGameProcessLauncher processLauncher = Substitute.For<IGameProcessLauncher>();
            processLauncher.StartAsync(
                    Arg.Do<GameLaunchRequest>(request => capturedRequest = request),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IGameProcessLaunchOperation>(
                    new CompletedGameProcessLaunchOperation(true, "generalsonlinezh.exe")));
            LauncherLaunchCoordinator coordinator = TestLauncherLaunchCoordinator.Create(
                preferencesService: CreateZeroHourPreferences(new LauncherGamePreferences
                {
                    GameArguments = "-quickstart"
                }),
                processLauncher: processLauncher);

            LauncherLaunchResult result = await LaunchAsync(
                coordinator,
                CreateRequest(
                    executableName: "generalsonlinezh.exe",
                    useGeneralsOnline: true),
                CancellationToken.None);

            result.LaunchStarted.Should().BeTrue();
            capturedRequest.Should().NotBeNull();
            capturedRequest!.Arguments.Should().Be(
                $"-quickstart {LauncherGameArgumentService.GeneralsOnlineFullscreenArgument}");
            capturedRequest.Arguments.Should().NotContain(
                LauncherGameArgumentService.GeneralsOnlineDisableCommunityDataPatchArgument);
        });
    }

    /// <summary>
    ///     Only a selected modification makes the base game's script files wrong for the session, and that is true of
    ///     the World Builder as much as of the game itself.
    /// </summary>
    [Theory]
    [InlineData(GameLaunchTargetKind.GameClient, true, true)]
    [InlineData(GameLaunchTargetKind.GameClient, false, false)]
    [InlineData(GameLaunchTargetKind.WorldBuilder, true, true)]
    [InlineData(GameLaunchTargetKind.WorldBuilder, false, false)]
    public void LaunchAsync_RequestsBaseGameScriptDisableOnlyForModdedContent(
        GameLaunchTargetKind targetKind,
        bool hasSelectedModification,
        bool expectedDisable)
    {
        StaTestRunner.Run(async () =>
        {
            FakeLaunchPreparationService preparationService = new();
            LauncherLaunchCoordinator coordinator = TestLauncherLaunchCoordinator.Create(
                preparationService: preparationService);

            LauncherLaunchResult result = await LaunchAsync(
                coordinator,
                CreateRequest(
                    targetKind,
                    activeVersions: hasSelectedModification
                        ? [TestLauncherContent.Version("Rise")]
                        : []),
                CancellationToken.None);

            result.LaunchStarted.Should().BeTrue();
            preparationService.PrepareRequests.Should().ContainSingle()
                .Which.DisableBaseGameScriptFiles.Should().Be(expectedDisable);
        });
    }

    [Fact]
    public void LaunchAsyncWhenWorldBuilderLaunchSucceeds_UsesSelectedExecutableAndArguments()
    {
        StaTestRunner.Run(async () =>
        {
            GameLaunchRequest? capturedRequest = null;
            IGameProcessLauncher processLauncher = Substitute.For<IGameProcessLauncher>();
            processLauncher.StartAsync(
                    Arg.Do<GameLaunchRequest>(request => capturedRequest = request),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IGameProcessLaunchOperation>(
                    new CompletedGameProcessLaunchOperation(true, "worldbuilder.exe")));
            LauncherLaunchCoordinator coordinator = TestLauncherLaunchCoordinator.Create(
                preferencesService: CreateZeroHourPreferences(new LauncherGamePreferences
                {
                    WorldBuilderArguments = "-wb"
                }),
                processLauncher: processLauncher);

            LauncherLaunchResult result = await LaunchAsync(
                coordinator,
                CreateRequest(
                    GameLaunchTargetKind.WorldBuilder,
                    "worldbuilder.exe"),
                CancellationToken.None);

            result.LaunchStarted.Should().BeTrue();
            capturedRequest.Should().NotBeNull();
            capturedRequest!.TargetKind.Should().Be(GameLaunchTargetKind.WorldBuilder);
            capturedRequest.ExecutableName.Should().Be("worldbuilder.exe");
            capturedRequest.Arguments.Should().Be("-wb");
        });
    }

    [Fact]
    public void LaunchAsync_ExposesTrackedProcessUntilCompletionAndForceClosesActiveProcess()
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
            LauncherLaunchCoordinator coordinator = TestLauncherLaunchCoordinator.Create(
                processLauncher: processLauncher);
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

            string shownProcessName = await processExposed.Task.WaitAsync(TestTimeouts.Wait);
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
    public void LaunchAsyncWhenHideLauncherAfterGameStart_IsEnabledExposesWindowVisibilityState()
    {
        StaTestRunner.Run(async () =>
        {
            var operation = new ControllableGameProcessLaunchOperation();
            var windowVisibilityStates = new List<bool>();
            var hideRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            LauncherLaunchCoordinator coordinator = TestLauncherLaunchCoordinator.Create(
                preferencesService: new RecordingLauncherPreferencesService(new LauncherPreferences
                {
                    Shared = new LauncherSharedPreferences
                    {
                        HideLauncherAfterGameStart = true
                    }
                }),
                processLauncher: CreateProcessLauncher(operation));
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

            await hideRequested.Task.WaitAsync(TestTimeouts.Wait);
            await WaitForActiveProcessAsync(coordinator);
            coordinator.ShouldHideLauncherWindow.Should().BeTrue();
            windowVisibilityStates.Should().Equal(true);

            operation.Complete(true);
            LauncherLaunchResult result = await launchTask.WaitAsync(TestTimeouts.Wait);

            result.ProcessSucceeded.Should().BeTrue();
            coordinator.ShouldHideLauncherWindow.Should().BeFalse();
            windowVisibilityStates.Should().Equal(true, false);
        });
    }

    [Fact]
    public void LaunchAsync_UpdatesActiveProcessNameWhenTrackedCurrentProcessChanges()
    {
        StaTestRunner.Run(async () =>
        {
            var operation = new ControllableGameProcessLaunchOperation("generalsonlinezh.exe");
            var initialProcessExposed = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var gameProcessExposed = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var exposedProcessNames = new List<string>();
            LauncherLaunchCoordinator coordinator = TestLauncherLaunchCoordinator.Create(
                processLauncher: CreateProcessLauncher(operation));
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

            string initialProcessName = await initialProcessExposed.Task.WaitAsync(TestTimeouts.Wait);
            initialProcessName.Should().Be("generalsonlinezh.exe");

            operation.RaiseCurrentExecutableNameChanged("generalszh.exe");
            await gameProcessExposed.Task.WaitAsync(TestTimeouts.Wait);
            coordinator.ActiveProcessName.Should().Be("generalszh.exe");

            operation.Complete(true);
            LauncherLaunchResult result = await launchTask;

            result.ProcessSucceeded.Should().BeTrue();
            exposedProcessNames.Should().Equal("generalsonlinezh.exe", "generalszh.exe");
        });
    }

    [Fact]
    public void LaunchAsync_ExposesActiveWorkflowDuringIntegrityVerification()
    {
        StaTestRunner.Run(async () =>
        {
            var verificationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var verificationCompletion = new TaskCompletionSource<LaunchContentIntegrityVerificationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            LauncherLaunchCoordinator coordinator = TestLauncherLaunchCoordinator.Create(
                integrityResolutionService: CreateGatedResolutionService(
                    verificationStarted,
                    verificationCompletion));
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
            await verificationStarted.Task.WaitAsync(TestTimeouts.Wait);

            coordinator.IsLaunchInProgress.Should().BeTrue();
            verificationCompletion.SetResult(CreateNoIssueVerificationResult());
            await launchTask;

            coordinator.IsLaunchInProgress.Should().BeFalse();
            activeStates.Should().Equal(true, false);
        });
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

    private static IGameProcessLauncher CreateProcessLauncher(IGameProcessLaunchOperation operation)
    {
        IGameProcessLauncher processLauncher = Substitute.For<IGameProcessLauncher>();
        processLauncher.StartAsync(Arg.Any<GameLaunchRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(operation));
        return processLauncher;
    }

    private static async Task WaitForActiveProcessAsync(LauncherLaunchCoordinator coordinator)
    {
        var processExposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(LauncherLaunchCoordinator.HasActiveProcess) &&
                coordinator.HasActiveProcess)
            {
                processExposed.TrySetResult();
            }
        };
        if (coordinator.HasActiveProcess)
        {
            return;
        }

        await processExposed.Task.WaitAsync(TestTimeouts.Wait);
    }

    private static RecordingLauncherPreferencesService CreateZeroHourPreferences(
        LauncherGamePreferences preferences)
    {
        return new RecordingLauncherPreferencesService(new LauncherPreferences
        {
            Games = new LauncherGamePreferencesSet { ZeroHour = preferences }
        });
    }

    private static LaunchContentIntegrityVerificationResult CreateNoIssueVerificationResult()
    {
        return new LaunchContentIntegrityVerificationResult(
            new ContentIntegrityReport(Array.Empty<ContentIntegrityIssue>()),
            Array.Empty<LaunchContentIntegrityTargetContext>());
    }

    /// <summary>
    ///     Holds verification open until the test releases it, which is how the launch-busy states are observed.
    /// </summary>
    private static ILaunchContentIntegrityResolutionService CreateGatedResolutionService(
        TaskCompletionSource verificationStarted,
        TaskCompletionSource<LaunchContentIntegrityVerificationResult> verificationCompletion)
    {
        ILaunchContentIntegrityResolutionService resolutionService =
            Substitute.For<ILaunchContentIntegrityResolutionService>();
        resolutionService.VerifyAsync(
                Arg.Any<LaunchContentIntegrityTargetRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                verificationStarted.TrySetResult();
                return verificationCompletion.Task;
            });
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
}
