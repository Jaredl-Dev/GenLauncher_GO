using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Services;

[Collection("Avalonia")]
public sealed class LauncherCloseGuardTests
{
    [Fact]
    public void CanCloseAsync_WhilePreProcessIntegrityVerificationIsRunning_IsBlocked()
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
            RecordingLauncherDialogService dialogService = new();
            LauncherLaunchCoordinator launchCoordinator = TestLauncherLaunchCoordinator.Create(
                dialogService: dialogService,
                integrityResolutionService: resolutionService);
            LauncherCloseGuard closeGuard = CreateGuard(launchCoordinator, dialogService);
            var owner = new Window();
            Task<LauncherLaunchResult> launchTask = LaunchAsync(launchCoordinator, owner);
            await verificationStarted.Task.WaitAsync(TestTimeouts.Wait);

            bool canClose = await closeGuard.CanCloseAsync(owner, LauncherCloseReason.Exit);

            canClose.Should().BeFalse();
            dialogService.InfoRequests.Should().ContainSingle().Which.Should()
                .Match<LauncherInfoDialogRequest>(request =>
                    request.MainMessage == "Launch in progress" &&
                    request.DetailMessage == "Wait for launch.");

            verificationCompletion.SetCanceled();
            LauncherLaunchResult result = await launchTask;
            result.FailureKind.Should().Be(LauncherLaunchFailureKind.VerificationCanceled);
            launchCoordinator.IsLaunchInProgress.Should().BeFalse();
            owner.Close();
        });
    }

    [Fact]
    public void CanCloseAsync_WhileALaunchedProcessIsRunning_IsBlockedWithTheRunningProcessMessage()
    {
        StaTestRunner.Run(async () =>
        {
            var operation = new ControllableGameProcessLaunchOperation("generalszh.exe");
            IGameProcessLauncher processLauncher = Substitute.For<IGameProcessLauncher>();
            processLauncher.StartAsync(Arg.Any<GameLaunchRequest>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IGameProcessLaunchOperation>(operation));
            RecordingLauncherDialogService dialogService = new();
            LauncherLaunchCoordinator launchCoordinator = TestLauncherLaunchCoordinator.Create(
                processLauncher: processLauncher,
                dialogService: dialogService);
            LauncherCloseGuard closeGuard = CreateGuard(launchCoordinator, dialogService);
            var owner = new Window();
            var processExposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            launchCoordinator.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(LauncherLaunchCoordinator.HasActiveProcess) &&
                    launchCoordinator.HasActiveProcess)
                {
                    processExposed.TrySetResult();
                }
            };
            Task<LauncherLaunchResult> launchTask = LaunchAsync(launchCoordinator, owner);
            await processExposed.Task.WaitAsync(TestTimeouts.Wait);

            bool canClose = await closeGuard.CanCloseAsync(owner, LauncherCloseReason.Exit);

            canClose.Should().BeFalse();
            dialogService.InfoRequests.Should().ContainSingle().Which.Should()
                .Match<LauncherInfoDialogRequest>(request =>
                    request.MainMessage == "Process running" &&
                    request.DetailMessage == "Close process first");

            operation.Complete(true);
            await launchTask.WaitAsync(TestTimeouts.Wait);
            owner.Close();
        });
    }

    /// <summary>
    ///     A paused download is already stopped and its partial content survives an exit, so closing must not ask.
    ///     A restart would resume into a different process, so it is refused instead.
    /// </summary>
    [Theory]
    [InlineData("Exit", true, null, null)]
    [InlineData("Restart", false, "Restart unavailable", "Finish Shockwave before restarting.")]
    public void CanCloseAsync_WhileADownloadIsPaused_OnlyRefusesARestart(
        string reasonName,
        bool expectedCanClose,
        string? expectedBlockTitle,
        string? expectedBlockDetail)
    {
        StaTestRunner.Run(async () =>
        {
            LauncherCloseReason reason = Enum.Parse<LauncherCloseReason>(reasonName);
            RecordingLauncherDialogService dialogService = new();
            LauncherPackageActivityService packageActivityService = new();
            LauncherCloseGuard closeGuard = CreateGuard(
                TestLauncherLaunchCoordinator.Create(dialogService: dialogService),
                dialogService,
                packageActivityService);
            var owner = new Window();
            var downloadReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var downloadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            object tile = new();
            packageActivityService.TryStartDownload(
                tile,
                "Shockwave",
                async (_, _, _) =>
                {
                    downloadStarted.TrySetResult();
                    await downloadReleased.Task;
                    return PackageDownloadResult.Canceled();
                },
                () => { },
                _ => { },
                () => { },
                _ => { },
                out Task<PackageDownloadResult>? lifecycle);
            await downloadStarted.Task.WaitAsync(TestTimeouts.Wait);
            packageActivityService.TryToggleDownloadPause(tile, out bool isPaused).Should().BeTrue();
            isPaused.Should().BeTrue();

            bool canClose = await closeGuard.CanCloseAsync(owner, reason);

            (string MainMessage, string DetailMessage)[] expectedInfoRequests = expectedBlockTitle == null
                ? []
                : [(expectedBlockTitle, expectedBlockDetail!)];
            canClose.Should().Be(expectedCanClose);
            dialogService.InfoRequests.Select(request => (request.MainMessage, request.DetailMessage))
                .Should().Equal(expectedInfoRequests);
            dialogService.WarningConfirmationRequests.Should().BeEmpty();

            downloadReleased.TrySetResult();
            await lifecycle!;
            owner.Close();
        });
    }

    private static LauncherCloseGuard CreateGuard(
        LauncherLaunchCoordinator launchCoordinator,
        ILauncherDialogService dialogService,
        LauncherPackageActivityService? packageActivityService = null)
    {
        return new LauncherCloseGuard(
            launchCoordinator,
            packageActivityService ?? new LauncherPackageActivityService(),
            dialogService,
            FakeStringLocalizer.Create(TestLocalizedStrings.Launcher),
            NullLogger<LauncherCloseGuard>.Instance);
    }

    private static Task<LauncherLaunchResult> LaunchAsync(
        LauncherLaunchCoordinator launchCoordinator,
        Window owner)
    {
        return launchCoordinator.LaunchAsync(
            new LauncherLaunchRequest(
                GameLaunchTargetKind.GameClient,
                "generalszh.exe",
                false,
                Array.Empty<LauncherContentVersion>()),
            Array.Empty<ILaunchContentIntegrityProgressTarget>(),
            owner,
            CancellationToken.None);
    }
}
