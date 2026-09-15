using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Features.Integrity;
using GenLauncherGO.Features.Launching;
using GenLauncherGO.Features.Mods;
using GenLauncherGO.Features.Startup;
using GenLauncherGO.Features.Updating;
using GenLauncherGO.Shared.Dialogs;

namespace GenLauncherGO.Tests.Features.Updating;

[Collection("Avalonia")]
public sealed class LauncherApplicationUpdateCoordinatorTests
{
    [Fact]
    public void StartWhenUpdateIsDownloadedAndRestartAccepted_RequestsUpdateRestartAndClosesWindow()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPackageActivityService packageActivityService = new();
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowInfoActionAsync(
                    Arg.Any<LauncherInfoDialogRequest>(),
                    Arg.Any<string>(),
                    Arg.Any<Window?>())
                .Returns(true);
            LauncherApplicationUpdateCoordinator coordinator = TestLauncherApplicationUpdateCoordinator.Create(
                out LauncherRestartCoordinator restartCoordinator,
                packageActivityService,
                dialogService);
            Window owner = new();
            bool closeRequested = false;
            owner.Closing += (_, _) => closeRequested = true;

            owner.Show();
            await coordinator.StartAsync(owner, CancellationToken.None);

            restartCoordinator.RestartKind.Should().Be(LauncherRestartKind.ApplicationUpdate);
            closeRequested.Should().BeTrue();
        });
    }

    [Fact]
    public void StartWhenUserChoosesLater_SuppressesAnotherPromptForSession()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPackageActivityService packageActivityService = new();
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowInfoActionAsync(
                    Arg.Any<LauncherInfoDialogRequest>(),
                    Arg.Any<string>(),
                    Arg.Any<Window?>())
                .Returns(false);
            LauncherApplicationUpdateCoordinator coordinator = TestLauncherApplicationUpdateCoordinator.Create(
                out LauncherRestartCoordinator restartCoordinator,
                packageActivityService,
                dialogService);
            Window owner = new();

            try
            {
                owner.Show();
                await coordinator.StartAsync(owner, CancellationToken.None);
                await coordinator.HandleLauncherActivatedAsync(owner);

                restartCoordinator.RestartKind.Should().Be(LauncherRestartKind.None);
                await dialogService.Received(1).ShowInfoActionAsync(
                    Arg.Is<LauncherInfoDialogRequest>(request =>
                        request.DetailMessage.Contains("1.2.0", System.StringComparison.Ordinal) &&
                        request.CancelText == "Later"),
                    "Restart now",
                    owner);
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public void StartDuringPackageActivity_DefersPromptUntilPackageActivityFinishes()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPackageActivityService packageActivityService = new();
            packageActivityService.TryBegin(
                    "Shockwave",
                    out LauncherPackageActivityService.LauncherPackageActivityLease? activityLease)
                .Should()
                .BeTrue();
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherApplicationUpdateCoordinator coordinator = TestLauncherApplicationUpdateCoordinator.Create(
                out _,
                packageActivityService,
                dialogService);
            Window owner = new();

            try
            {
                owner.Show();
                await coordinator.StartAsync(owner, CancellationToken.None);

                await dialogService.DidNotReceiveWithAnyArgs().ShowInfoActionAsync(
                    default!,
                    default!,
                    default);

                activityLease!.Dispose();

                await dialogService.Received(1).ShowInfoActionAsync(
                    Arg.Any<LauncherInfoDialogRequest>(),
                    Arg.Any<string>(),
                    owner);
            }
            finally
            {
                activityLease?.Dispose();
                owner.Close();
            }
        });
    }

    [Fact]
    public void StartDuringLaunchActivity_DefersPromptUntilLaunchActivityFinishes()
    {
        StaTestRunner.Run(async () =>
        {
            const string ProcessName = "generals.exe";
            IGameProcessLaunchOperation operation = Substitute.For<IGameProcessLaunchOperation>();
            var processCompletion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var processExposed = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            operation.CurrentExecutableName.Returns(ProcessName);
            operation.Completion.Returns(processCompletion.Task);
            IGameProcessLauncher processLauncher = Substitute.For<IGameProcessLauncher>();
            processLauncher.StartAsync(Arg.Any<GameLaunchRequest>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(operation));
            LauncherPackageActivityService packageActivityService = new();
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowInfoActionAsync(
                    Arg.Any<LauncherInfoDialogRequest>(),
                    Arg.Any<string>(),
                    Arg.Any<Window?>())
                .Returns(false);
            LauncherLaunchCoordinator launchCoordinator = TestLauncherLaunchCoordinator.Create(
                packageActivityService,
                processLauncher: processLauncher,
                dialogService: dialogService);
            LauncherApplicationUpdateCoordinator coordinator = TestLauncherApplicationUpdateCoordinator.Create(
                out _,
                packageActivityService,
                dialogService,
                launchCoordinator);
            Window owner = new();
            launchCoordinator.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(LauncherLaunchCoordinator.HasActiveProcess) &&
                    launchCoordinator.HasActiveProcess)
                {
                    processExposed.TrySetResult();
                }
            };

            Task<bool> launchTask = launchCoordinator.LaunchAsync(
                new LauncherLaunchRequest(
                    GameLaunchTargetKind.GameClient,
                    ProcessName,
                    false,
                    Array.Empty<LauncherContentVersion>()),
                Array.Empty<ILaunchContentIntegrityProgressTarget>(),
                owner,
                CancellationToken.None);
            try
            {
                owner.Show();
                await processExposed.Task.WaitAsync(TestTimeouts.Wait);
                await coordinator.StartAsync(owner, CancellationToken.None);

                await dialogService.DidNotReceiveWithAnyArgs().ShowInfoActionAsync(
                    default!,
                    default!,
                    default);

                processCompletion.TrySetResult(true);
                await launchTask.WaitAsync(TestTimeouts.Wait);

                await dialogService.Received(1).ShowInfoActionAsync(
                    Arg.Any<LauncherInfoDialogRequest>(),
                    Arg.Any<string>(),
                    owner);
            }
            finally
            {
                processCompletion.TrySetResult(true);
                await launchTask.WaitAsync(TestTimeouts.Wait);
                owner.Close();
            }
        });
    }
}
