using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Services;

public sealed class LauncherCloseGuardTests
{
    [Fact]
    public void CloseIsBlockedWhilePreProcessIntegrityVerificationIsRunning()
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
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherLaunchCoordinator launchCoordinator = TestLauncherLaunchCoordinator.Create(
                dialogService: dialogService,
                integrityResolutionService: resolutionService);
            var closeGuard = new LauncherCloseGuard(
                launchCoordinator,
                new LauncherPackageActivityService(),
                dialogService,
                new TestStringLocalizer(new Dictionary<string, string>
                {
                    ["LaunchCloseBlockedDetails"] = "Wait for launch.",
                    ["LaunchCloseBlockedTitle"] = "Launch in progress",
                }),
                NullLogger<LauncherCloseGuard>.Instance);
            var owner = new Window();
            Task<LauncherLaunchResult> launchTask = launchCoordinator.LaunchAsync(
                new LauncherLaunchRequest(
                    GameLaunchTargetKind.GameClient,
                    "generalszh.exe",
                    useGeneralsOnline: false,
                    Array.Empty<LauncherContentVersion>()),
                Array.Empty<ILaunchContentIntegrityProgressTarget>(),
                owner,
                CancellationToken.None);
            await verificationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            bool canClose = await closeGuard.CanCloseAsync(owner, LauncherCloseReason.Exit);

            canClose.Should().BeFalse();
            await dialogService.Received(1).ShowInfoAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Launch in progress" &&
                    request.DetailMessage == "Wait for launch."),
                owner);

            verificationCompletion.SetCanceled();
            LauncherLaunchResult result = await launchTask;
            result.FailureKind.Should().Be(LauncherLaunchFailureKind.VerificationCanceled);
            launchCoordinator.IsLaunchInProgress.Should().BeFalse();
            owner.Close();
        });
    }
}
