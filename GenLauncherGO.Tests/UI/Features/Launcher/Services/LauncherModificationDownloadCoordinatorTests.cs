using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Updating.Contracts;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Mods;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Services;

public sealed class LauncherModificationDownloadCoordinatorTests
{
    [Fact]
    public void ConcurrentStartsPublishOneOperationAndRejectTheOther()
    {
        StaTestRunner.Run(async () =>
        {
            CancelablePackageDownloadService downloadService = new();
            LauncherPackageActivityService activityService = new();
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherModificationDownloadCoordinator coordinator = CreateCoordinator(
                downloadService,
                activityService,
                dialogService: dialogService);
            ModificationViewModel first = CreateViewModel(activityService, "First");
            ModificationViewModel second = CreateViewModel(activityService, "Second");

            Task firstStart = coordinator.StartDownloadAsync(first, new Window(), () => { });
            await downloadService.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await coordinator.StartDownloadAsync(second, new Window(), () => { });

            downloadService.CallCount.Should().Be(1);
            activityService.GetActiveDownloadTask(first).Should().BeSameAs(activityService.ActiveDownloadTask);
            activityService.GetActiveDownloadTask(second).Should().BeNull();
            await dialogService.Received(1).ShowInfoAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null && request.MainMessage == "Package activity"),
                Arg.Any<Window?>());

            activityService.RequestDownloadCancellation(first).Should().BeTrue();
            await firstStart.WaitAsync(TimeSpan.FromSeconds(5));
        });
    }

    [Fact]
    public void CancellationPublishesCleanupTerminalAndIdleExactlyOnceInOrder()
    {
        StaTestRunner.Run(async () =>
        {
            CancelablePackageDownloadService downloadService = new();
            LauncherPackageActivityService activityService = new();
            LauncherModificationDownloadCoordinator coordinator = CreateCoordinator(
                downloadService,
                activityService);
            ModificationViewModel viewModel = CreateViewModel(activityService);
            List<string> publications = new();
            viewModel.PackageActivityChanged += (_, _) =>
            {
                if (!viewModel.HasActivePackageActivity &&
                    viewModel.ProgressMessage == "Canceled")
                {
                    publications.Add("terminal");
                }
            };
            activityService.ActivityChanged += (_, _) =>
            {
                if (!activityService.IsActive)
                {
                    publications.Add("idle");
                }
            };

            Task start = coordinator.StartDownloadAsync(
                viewModel,
                new Window(),
                () => publications.Add("cleanup"));
            await downloadService.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task<PackageDownloadResult> lifecycle = activityService.GetActiveDownloadTask(viewModel)!;

            activityService.RequestDownloadCancellation(viewModel).Should().BeTrue();
            activityService.RequestDownloadCancellation(viewModel).Should().BeTrue();
            PackageDownloadResult result = await lifecycle.WaitAsync(TimeSpan.FromSeconds(5));
            await start.WaitAsync(TimeSpan.FromSeconds(5));

            result.Status.Should().Be(PackageDownloadStatus.Canceled);
            publications.Should().Equal("cleanup", "terminal", "idle");
            activityService.GetActiveDownloadTask(viewModel).Should().BeNull();
            activityService.ActiveDownloadTask.Should().BeNull();
            activityService.IsActive.Should().BeFalse();
        });
    }

    [Fact]
    public void SuccessfulDownloadHoldsLifecycleUntilIntegritySnapshotCompletes()
    {
        StaTestRunner.Run(async () =>
        {
            RecordingPackageDownloadService downloadService = new(PackageDownloadResult.Succeeded());
            var snapshotCompletion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            ILaunchContentIntegrityResolutionService resolutionService =
                Substitute.For<ILaunchContentIntegrityResolutionService>();
            resolutionService.CaptureManagedInstallSnapshotAsync(
                    Arg.Any<GenLauncherGO.Core.Launching.Models.LaunchContentIntegrityVersionRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(snapshotCompletion.Task);
            LauncherPackageActivityService activityService = new();
            LauncherModificationDownloadCoordinator coordinator = CreateCoordinator(
                downloadService,
                activityService,
                resolutionService: resolutionService);
            ModificationViewModel viewModel = CreateViewModel(activityService);

            Task start = coordinator.StartDownloadAsync(viewModel, new Window(), () => { });
            await resolutionService.Received(1).CaptureManagedInstallSnapshotAsync(
                Arg.Any<GenLauncherGO.Core.Launching.Models.LaunchContentIntegrityVersionRequest>(),
                Arg.Any<CancellationToken>());
            Task<PackageDownloadResult> lifecycle = activityService.GetActiveDownloadTask(viewModel)!;

            lifecycle.Should().BeSameAs(activityService.ActiveDownloadTask);
            lifecycle.IsCompleted.Should().BeFalse();
            activityService.IsActive.Should().BeTrue();

            snapshotCompletion.SetResult();
            await start.WaitAsync(TimeSpan.FromSeconds(5));

            lifecycle.IsCompletedSuccessfully.Should().BeTrue();
            activityService.IsActive.Should().BeFalse();
        });
    }

    [Fact]
    public void PostInstallFailurePreservesCommittedSuccessAndShowsWarning()
    {
        StaTestRunner.Run(async () =>
        {
            RecordingPackageDownloadService downloadService = new(PackageDownloadResult.Succeeded());
            var catalog = new FakeLauncherContentCatalog
            {
                LocalDataUpdateHandler = () => throw new IOException("catalog update failed")
            };
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherPackageActivityService activityService = new();
            LauncherModificationDownloadCoordinator coordinator = CreateCoordinator(
                downloadService,
                activityService,
                dialogService: dialogService,
                catalog: catalog);
            ModificationViewModel viewModel = CreateViewModel(activityService);

            await coordinator.StartDownloadAsync(viewModel, new Window(), () => { });

            viewModel.ContainerModification.Installed.Should().BeTrue();
            await dialogService.Received(1).ShowErrorAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Unexpected error" &&
                    request.DetailMessage == "Try again"),
                Arg.Any<Window?>());
        });
    }

    [Fact]
    public void QueuedProgressFromCompletedOperationCannotOverwriteTerminalState()
    {
        StaTestRunner.Run(async () =>
        {
            ControlledPackageDownloadService downloadService = new();
            LauncherPackageActivityService activityService = new();
            LauncherModificationDownloadCoordinator coordinator = CreateCoordinator(
                downloadService,
                activityService);
            ModificationViewModel viewModel = CreateViewModel(activityService);

            Task start = coordinator.StartDownloadAsync(viewModel, new Window(), () => { });
            await downloadService.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            downloadService.Report(new PackageUpdateProgress(100, 25, 25, "package.big"));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            viewModel.ProgressValue.Should().Be(25);

            downloadService.Complete(PackageDownloadResult.Succeeded());
            await start.WaitAsync(TimeSpan.FromSeconds(5));
            string terminalMessage = viewModel.ProgressMessage;
            double terminalProgress = viewModel.ProgressValue;

            downloadService.Report(new PackageUpdateProgress(100, 90, 90, "late.big"));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            viewModel.ProgressMessage.Should().Be(terminalMessage);
            viewModel.ProgressValue.Should().Be(terminalProgress);
        });
    }

    private static LauncherModificationDownloadCoordinator CreateCoordinator(
        IPackageDownloadService downloadService,
        LauncherPackageActivityService packageActivityService,
        ILauncherDialogService? dialogService = null,
        ILaunchContentIntegrityResolutionService? resolutionService = null,
        FakeLauncherContentCatalog? catalog = null)
    {
        ILauncherDialogService resolvedDialogService = dialogService ?? Substitute.For<ILauncherDialogService>();
        ILauncherPreferencesService preferencesService = Substitute.For<ILauncherPreferencesService>();
        preferencesService.Current.Returns(new LauncherPreferences());
        FakeLauncherContentCatalog resolvedCatalog = catalog ?? new FakeLauncherContentCatalog();
        LaunchContentIntegrityCoordinator integrityCoordinator = new(
            resolutionService ?? Substitute.For<ILaunchContentIntegrityResolutionService>(),
            resolvedCatalog,
            TestLauncherPaths.CreateRuntimePathContext(TestLauncherPaths.Create()),
            packageActivityService,
            CreateStringLocalizer(),
            resolvedDialogService,
            NullLogger<LaunchContentIntegrityCoordinator>.Instance);

        return new LauncherModificationDownloadCoordinator(
            preferencesService,
            resolvedCatalog,
            downloadService,
            integrityCoordinator,
            packageActivityService,
            resolvedDialogService,
            CreateStringLocalizer(),
            NullLogger<LauncherModificationDownloadCoordinator>.Instance);
    }

    private static ModificationViewModel CreateViewModel(
        LauncherPackageActivityService packageActivityService,
        string name = "Shockwave")
    {
        LauncherContentVersion version = new()
        {
            Installation = new LauncherContentInstallation
            {
                Installed = false,
                ContentSourceKind = ContentSourceKind.ManagedSingleFile
            },
            Name = name,
            Version = "1.0",
            ModificationType = ModificationType.Mod,
            SimpleDownloadLink = "https://example.test/package.zip",
        };
        return new ModificationViewModel(
            new LauncherContent(version),
            new ModificationImageSourceFactory(NullLogger<ModificationImageSourceFactory>.Instance),
            TestLauncherRuntimeContext.Create(),
            Substitute.For<IModificationImageFileService>(),
            CreateStringLocalizer(),
            packageActivityService,
            NullLogger<ModificationViewModel>.Instance);
    }

    private static TestStringLocalizer CreateStringLocalizer()
    {
        return new TestStringLocalizer(new Dictionary<string, string>
        {
            ["AdvertisingDonationAlerts"] = "Donate",
            ["Cancel"] = "Cancel",
            ["CancelDownloadAction"] = "Cancel Download",
            ["Canceled"] = "Canceled",
            ["Delete"] = "Delete",
            ["DownloadInProgress"] = "Downloaded {0} of {1}",
            ["Error"] = "Error: ",
            ["Install"] = "Install",
            ["IntegrityCacheSuffix"] = " cache",
            ["LatestVersion"] = "Latest version: ",
            ["PackageActivityInProgress"] = "Package activity",
            ["PackageActivityInProgressDetails"] = "Package activity details",
            ["Pause"] = "Pause",
            ["Preparing"] = "Preparing",
            ["UnexpectedErrorTitle"] = "Unexpected error",
            ["UnexpectedErrorDetails"] = "Try again",
            ["Resume"] = "Resume",
            ["RemoveFromList"] = "Remove from list",
            ["UnpackingPreparing"] = "Unpacking",
            ["Update"] = "Update",
            ["UpToDate"] = "Up to date",
        });
    }

    private sealed class RecordingPackageDownloadService : IPackageDownloadService
    {
        private readonly PackageDownloadResult _result;

        public RecordingPackageDownloadService(PackageDownloadResult result)
        {
            _result = result;
        }

        public Task<PackageDownloadResult> DownloadAsync(
            LauncherContent modification,
            LauncherContentVersion version,
            IProgress<PackageUpdateProgress>? progress,
            CancellationToken cancellationToken,
            PackageDownloadPauseController? pauseController = null)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class CancelablePackageDownloadService : IPackageDownloadService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }

        public async Task<PackageDownloadResult> DownloadAsync(
            LauncherContent modification,
            LauncherContentVersion version,
            IProgress<PackageUpdateProgress>? progress,
            CancellationToken cancellationToken,
            PackageDownloadPauseController? pauseController = null)
        {
            CallCount++;
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return PackageDownloadResult.Succeeded();
            }
            catch (OperationCanceledException)
            {
                return PackageDownloadResult.Canceled();
            }
        }
    }

    private sealed class ControlledPackageDownloadService : IPackageDownloadService
    {
        private readonly TaskCompletionSource<PackageDownloadResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private IProgress<PackageUpdateProgress>? _progress;

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<PackageDownloadResult> DownloadAsync(
            LauncherContent modification,
            LauncherContentVersion version,
            IProgress<PackageUpdateProgress>? progress,
            CancellationToken cancellationToken,
            PackageDownloadPauseController? pauseController = null)
        {
            _progress = progress;
            Started.TrySetResult();
            return _completion.Task;
        }

        public void Report(PackageUpdateProgress progress)
        {
            _progress!.Report(progress);
        }

        public void Complete(PackageDownloadResult result)
        {
            _completion.TrySetResult(result);
        }
    }
}
