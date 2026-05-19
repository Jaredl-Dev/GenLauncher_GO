using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Launcher.Views;
using GenLauncherGO.UI.Features.Mods;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Services;

public sealed partial class LauncherWindowWorkflowCoordinatorTests
{
    [Fact]
    public void ConfirmCloseDuringActiveOperationsWhenNoActivity_IsActiveAllowsCloseWithoutDialog()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(dialogService: dialogService);

            bool canClose = await coordinator.ConfirmCloseDuringActiveOperationsAsync(new Window());

            canClose.Should().BeTrue();
            await dialogService.DidNotReceive().ShowWarningConfirmationAsync(
                Arg.Any<LauncherInfoDialogRequest>(),
                Arg.Any<string?>(),
                Arg.Any<Window?>());
        });
    }

    [Fact]
    public void ConfirmCloseDuringActiveOperationsWhenPackageActivity_IsActiveUsesWarningConfirmation()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPackageActivityService packageActivityService = new();
            packageActivityService.TryBegin(
                    "Shockwave",
                    out LauncherPackageActivityService.LauncherPackageActivityLease? lease)
                .Should()
                .BeTrue();
            ILauncherDialogService dialogService = StubLauncherDialogService.AnsweringWarningConfirmations(false);
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                packageActivityService,
                dialogService);
            var owner = new Window();

            try
            {
                bool canClose = await coordinator.ConfirmCloseDuringActiveOperationsAsync(owner);

                canClose.Should().BeFalse();
                await dialogService.Received(1).ShowWarningConfirmationAsync(
                    Arg.Is<LauncherInfoDialogRequest>(request =>
                        request != null &&
                        request.MainMessage == "Package activity" &&
                        request.DetailMessage == "Close Shockwave?"),
                    "Close anyway",
                    owner);
            }
            finally
            {
                lease?.Dispose();
            }
        });
    }

    [Fact]
    public void MainWindowClosing_WhenCloseIsDeclined_LeavesWindowOpenAndEnabled()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPackageActivityService packageActivityService = new();
            packageActivityService.TryBegin(
                    "Shockwave",
                    out LauncherPackageActivityService.LauncherPackageActivityLease? lease)
                .Should()
                .BeTrue();
            ILauncherDialogService dialogService = StubLauncherDialogService.AnsweringWarningConfirmations(false);
            ILauncherPreferencesService preferencesService = Substitute.For<ILauncherPreferencesService>();
            preferencesService.Current.Returns(new LauncherPreferences());
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                packageActivityService,
                dialogService,
                catalog: catalog,
                preferencesService: preferencesService);
            WorkflowFixture fixture = new(catalog, packageActivityService, preferencesService);
            var boundary = new ControlledUiExceptionBoundary();
            MainWindow window = CreateMainWindow(fixture.ViewModel, coordinator, boundary);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();

            try
            {
                window.Show();

                window.Close();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                window.IsVisible.Should().BeTrue();
                window.IsEnabled.Should().BeTrue();
                closed.Task.IsCompleted.Should().BeFalse();
                boundary.CloseAttemptCount.Should().Be(0);
                catalog.SaveCount.Should().Be(0);
            }
            finally
            {
                lease?.Dispose();
                if (window.IsVisible)
                {
                    window.Close();
                    await closed.Task.WaitAsync(TestTimeouts.Wait);
                }

                fixture.Owner.Close();
            }
        });
    }

    [Fact]
    public void MainWindowClosing_WhenApproved_WaitsForCleanupAndWindowOperationsBeforeSavingAndDisposingOnce()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = StubLauncherDialogService.AnsweringWarningConfirmations(true);
            ILauncherPreferencesService preferencesService = Substitute.For<ILauncherPreferencesService>();
            preferencesService.Current.Returns(new LauncherPreferences());
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherPackageActivityService packageActivityService = new();
            ControllablePackageDownloadService downloadService = new();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                packageActivityService,
                dialogService,
                catalog: catalog,
                preferencesService: preferencesService,
                launcherSettingsWindowFactory: CreateAutoClosingSettingsWindow);
            LauncherContentActionCoordinator contentActionCoordinator = CreateContentActionCoordinator(
                packageActivityService,
                dialogService,
                catalog: catalog,
                packageDownloadService: downloadService);
            WorkflowFixture fixture = new(catalog, packageActivityService, preferencesService);
            ModificationViewModel modification = CreateTile(
                CreateModification(
                    "Shockwave",
                    ModificationType.Mod,
                    CreateVersion("Shockwave", ContentSourceKind.ManagedSingleFile)),
                packageActivityService);
            fixture.AddTile(modification);
            var boundary = new ControlledUiExceptionBoundary();
            MainWindow window = CreateMainWindow(
                fixture.ViewModel,
                coordinator,
                boundary,
                contentActionCoordinator);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();

            try
            {
                window.Show();
                Task download = contentActionCoordinator.UpdateModificationAsync(
                    new LauncherWindowContext(fixture.ViewModel, fixture.Content, window),
                    modification);
                await downloadService.Started.Task.WaitAsync(TestTimeouts.Wait);
                boundary.HoldNextWindowOperation();
                window.FindControl<Button>("OptionsButton")!.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                await boundary.WindowOperationStarted.Task.WaitAsync(TestTimeouts.Wait);

                boundary.TrackNextCloseOperation();
                window.Close();
                await boundary.ClosePreparationStarted.Task.WaitAsync(TestTimeouts.Wait);
                await downloadService.CancellationObserved.Task.WaitAsync(TestTimeouts.Wait);

                downloadService.Release();
                await download.WaitAsync(TestTimeouts.Wait);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                closed.Task.IsCompleted.Should().BeFalse();
                window.IsVisible.Should().BeTrue();
                window.IsEnabled.Should().BeFalse();
                catalog.SaveCount.Should().Be(0);

                boundary.ReleaseWindowOperation();
                await closed.Task.WaitAsync(TestTimeouts.Wait);

                boundary.HeldWindowOperationSucceeded.Should().BeTrue();
                catalog.SaveCount.Should().Be(1);
                preferencesService.Received(1).PreferencesChanged -=
                    Arg.Any<EventHandler<LauncherPreferences>>();
            }
            finally
            {
                downloadService.Release();
                boundary.ReleaseWindowOperation();
                if (window.IsVisible)
                {
                    window.Close();
                }

                fixture.Owner.Close();
            }
        });
    }

    [Fact]
    public void MainWindowClosing_WhenBoundaryFails_RestoresEnabledStateAndAllowsRetry()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherPreferencesService preferencesService = Substitute.For<ILauncherPreferencesService>();
            preferencesService.Current.Returns(new LauncherPreferences());
            FakeLauncherContentCatalog catalog = CreateCatalog();
            bool failFirstSave = true;
            catalog.SaveHandler = () =>
            {
                if (failFirstSave)
                {
                    failFirstSave = false;
                    throw new InvalidOperationException("save failed");
                }
            };
            LauncherPackageActivityService packageActivityService = new();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                packageActivityService,
                catalog: catalog,
                preferencesService: preferencesService);
            WorkflowFixture fixture = new(catalog, packageActivityService, preferencesService);
            var boundary = new ControlledUiExceptionBoundary();
            MainWindow window = CreateMainWindow(fixture.ViewModel, coordinator, boundary);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();

            try
            {
                window.Show();

                boundary.TrackNextCloseOperation();
                window.Close();
                await boundary.CloseAttemptCompleted.Task.WaitAsync(TestTimeouts.Wait);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                boundary.CloseAttemptCount.Should().Be(1);
                closed.Task.IsCompleted.Should().BeFalse();
                window.IsVisible.Should().BeTrue();
                window.IsEnabled.Should().BeTrue();
                catalog.SaveCount.Should().Be(1);

                boundary.TrackNextCloseOperation();
                window.Close();
                await closed.Task.WaitAsync(TestTimeouts.Wait);

                boundary.CloseAttemptCount.Should().Be(2);
                catalog.SaveCount.Should().Be(2);
                preferencesService.Received(1).PreferencesChanged -=
                    Arg.Any<EventHandler<LauncherPreferences>>();
            }
            finally
            {
                if (window.IsVisible)
                {
                    window.Close();
                }

                fixture.Owner.Close();
            }
        });
    }

    [Fact]
    public void ForceCloseRunningProcessAsync_NoActiveProcess_DoesNothing()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(dialogService: dialogService);

            await coordinator.ForceCloseRunningProcessAsync(new Window());

            await dialogService.DidNotReceive().ShowWarningConfirmationAsync(
                Arg.Any<LauncherInfoDialogRequest>(),
                Arg.Any<string?>(),
                Arg.Any<Window?>());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForceCloseRunningProcessAsync_ActiveProcess_HonorsConfirmation(bool confirmed)
    {
        StaTestRunner.Run(async () =>
        {
            const string ProcessName = "generalszh.exe";
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
            ILauncherDialogService dialogService = StubLauncherDialogService.AnsweringWarningConfirmations(confirmed);
            LauncherPackageActivityService packageActivityService = new();
            LauncherLaunchCoordinator launchCoordinator = TestLauncherLaunchCoordinator.Create(
                packageActivityService,
                processLauncher: processLauncher,
                dialogService: dialogService);
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                packageActivityService,
                dialogService,
                launchCoordinator: launchCoordinator);
            var owner = new Window();
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
                    ProcessName,
                    false,
                    Array.Empty<LauncherContentVersion>()),
                Array.Empty<ILaunchContentIntegrityProgressTarget>(),
                owner,
                CancellationToken.None);
            try
            {
                await processExposed.Task.WaitAsync(TestTimeouts.Wait);
                await coordinator.ForceCloseRunningProcessAsync(owner);
            }
            finally
            {
                processCompletion.TrySetResult(true);
                await launchTask;
            }

            await dialogService.Received(1).ShowWarningConfirmationAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request.MainMessage == "Force quit?" &&
                    request.DetailMessage == $"Force quit {ProcessName}?"),
                "Force quit",
                owner);
            operation.Received(confirmed ? 1 : 0).ForceClose();
        });
    }

}
