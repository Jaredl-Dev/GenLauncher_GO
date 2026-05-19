using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Updating.Contracts;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Mods;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Services;

[Collection("Avalonia")]
public sealed class LauncherModificationDownloadCoordinatorTests
{
    [Fact]
    public void Concurrent_StartsPublishOneOperationAndRejectTheOther()
    {
        StaTestRunner.Run(async () =>
        {
            ControllablePackageDownloadService downloadService = new();
            LauncherPackageActivityService activityService = new();
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherModificationDownloadCoordinator coordinator = CreateCoordinator(
                downloadService,
                activityService,
                dialogService);
            ModificationViewModel first = CreateViewModel(activityService, "First");
            ModificationViewModel second = CreateViewModel(activityService, "Second");

            Task firstStart = coordinator.StartDownloadAsync(first, new Window(), () => { });
            await downloadService.Started.Task.WaitAsync(TestTimeouts.Wait);
            await coordinator.StartDownloadAsync(second, new Window(), () => { });

            downloadService.CallCount.Should().Be(1);
            activityService.GetActiveDownloadTask(first).Should().BeSameAs(activityService.ActiveDownloadTask);
            activityService.GetActiveDownloadTask(second).Should().BeNull();
            await dialogService.Received(1).ShowInfoAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null && request.MainMessage == "Package activity"),
                Arg.Any<Window?>());

            activityService.RequestDownloadCancellation(first).Should().BeTrue();
            downloadService.Release();
            await firstStart.WaitAsync(TestTimeouts.Wait);
        });
    }

    [Fact]
    public void Cancellation_PublishesCleanupTerminalAndIdleExactlyOnceInOrder()
    {
        StaTestRunner.Run(async () =>
        {
            ControllablePackageDownloadService downloadService = new();
            LauncherPackageActivityService activityService = new();
            LauncherModificationDownloadCoordinator coordinator = CreateCoordinator(
                downloadService,
                activityService);
            ModificationViewModel viewModel = CreateViewModel(activityService);
            List<string> publications = [];
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
            await downloadService.Started.Task.WaitAsync(TestTimeouts.Wait);
            Task<PackageDownloadResult> lifecycle = activityService.GetActiveDownloadTask(viewModel)!;

            activityService.RequestDownloadCancellation(viewModel).Should().BeTrue();
            activityService.RequestDownloadCancellation(viewModel).Should().BeTrue();
            downloadService.Release();
            PackageDownloadResult result = await lifecycle.WaitAsync(TestTimeouts.Wait);
            await start.WaitAsync(TestTimeouts.Wait);

            downloadService.CancellationObserved.Task.IsCompleted.Should().BeTrue();
            result.Status.Should().Be(PackageDownloadStatus.Canceled);
            publications.Should().Equal("cleanup", "terminal", "idle");
            activityService.GetActiveDownloadTask(viewModel).Should().BeNull();
            activityService.ActiveDownloadTask.Should().BeNull();
            activityService.IsActive.Should().BeFalse();
        });
    }

    /// <summary>
    ///     A failed download never installed anything, so the tile must not claim it did and canceled-content cleanup
    ///     must not delete a version the user still has.
    /// </summary>
    [Fact]
    public void FailedDownload_LeavesTheVersionUninstalledAndTakesNoIntegritySnapshot()
    {
        StaTestRunner.Run(async () =>
        {
            StubPackageDownloadService downloadService =
                new(PackageDownloadResult.RecoverableFailure("mirror unavailable"));
            LauncherPackageActivityService activityService = new();
            var catalog = new FakeLauncherContentCatalog();
            ILaunchContentIntegrityResolutionService resolutionService =
                Substitute.For<ILaunchContentIntegrityResolutionService>();
            LauncherModificationDownloadCoordinator coordinator = CreateCoordinator(
                downloadService,
                activityService,
                resolutionService: resolutionService,
                catalog: catalog);
            ModificationViewModel viewModel = CreateViewModel(activityService);

            await coordinator.StartDownloadAsync(viewModel, new Window(), () => { });

            viewModel.LatestVersion.Installation.Installed.Should().BeFalse();
            catalog.UninstalledVersions.Should().BeEmpty();
            catalog.LocalDataUpdateCount.Should().Be(0);
            await resolutionService.DidNotReceiveWithAnyArgs()
                .CaptureManagedInstallSnapshotAsync(default!, default);
        });
    }

    /// <summary>
    ///     A provider that reports cancellation without the launcher asking for it is a transport fault, not a user
    ///     cancellation, so the partial content must survive instead of being cleaned away.
    /// </summary>
    [Fact]
    public void UnrequestedCancellation_EndsAsAnUnexpectedFailureWithoutCanceledCleanup()
    {
        StaTestRunner.Run(async () =>
        {
            ControllablePackageDownloadService downloadService = new();
            LauncherPackageActivityService activityService = new();
            LauncherModificationDownloadCoordinator coordinator = CreateCoordinator(
                downloadService,
                activityService);
            ModificationViewModel viewModel = CreateViewModel(activityService);
            int cleanupCount = 0;

            Task start = coordinator.StartDownloadAsync(viewModel, new Window(), () => cleanupCount++);
            await downloadService.Started.Task.WaitAsync(TestTimeouts.Wait);
            Task<PackageDownloadResult> lifecycle = activityService.GetActiveDownloadTask(viewModel)!;
            downloadService.Throw(new OperationCanceledException());
            PackageDownloadResult result = await lifecycle.WaitAsync(TestTimeouts.Wait);
            await start.WaitAsync(TestTimeouts.Wait);

            result.Status.Should().Be(PackageDownloadStatus.UnexpectedFailure);
            cleanupCount.Should().Be(0);
            viewModel.LatestVersion.Installation.Installed.Should().BeFalse();
            activityService.IsActive.Should().BeFalse();
        });
    }

    [Fact]
    public void DownloadThatThrows_CompletesTheLifecycleAsAnUnexpectedFailure()
    {
        StaTestRunner.Run(async () =>
        {
            ControllablePackageDownloadService downloadService = new();
            LauncherPackageActivityService activityService = new();
            LauncherModificationDownloadCoordinator coordinator = CreateCoordinator(
                downloadService,
                activityService);
            ModificationViewModel viewModel = CreateViewModel(activityService);

            Task start = coordinator.StartDownloadAsync(viewModel, new Window(), () => { });
            await downloadService.Started.Task.WaitAsync(TestTimeouts.Wait);
            Task<PackageDownloadResult> lifecycle = activityService.GetActiveDownloadTask(viewModel)!;
            downloadService.Throw(new IOException("the mirror closed the connection"));
            PackageDownloadResult result = await lifecycle.WaitAsync(TestTimeouts.Wait);
            await start.WaitAsync(TestTimeouts.Wait);

            result.Status.Should().Be(PackageDownloadStatus.UnexpectedFailure);
            lifecycle.IsCompletedSuccessfully.Should().BeTrue();
            viewModel.LatestVersion.Installation.Installed.Should().BeFalse();
            activityService.IsActive.Should().BeFalse();
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SuccessfulDownload_AutoDeletePolicyDeletesOnlyOlderVersions(
        bool autoDeleteOldVersions)
    {
        StaTestRunner.Run(async () =>
        {
            StubPackageDownloadService downloadService = new(PackageDownloadResult.Succeeded());
            LauncherPackageActivityService activityService = new();
            LauncherContentVersion oldest = CreateVersion("Shockwave", "1.0", installed: true);
            LauncherContentVersion older = CreateVersion("Shockwave", "2.0", installed: true);
            LauncherContentVersion latest = CreateVersion("Shockwave", "3.0");
            var data = new LauncherData();
            data.AddOrUpdate(oldest);
            data.AddOrUpdate(older);
            data.AddOrUpdate(latest);
            var catalog = new FakeLauncherContentCatalog { Data = data };
            LauncherModificationDownloadCoordinator coordinator = CreateCoordinator(
                downloadService,
                activityService,
                catalog: catalog,
                autoDeleteOldVersions: autoDeleteOldVersions);
            ModificationViewModel viewModel = CreateViewModel(
                activityService,
                data.FindContent(latest.ContentKey)!);

            await coordinator.StartDownloadAsync(viewModel, new Window(), () => { });

            LauncherContentKey[] expectedUninstalls = autoDeleteOldVersions
                ? [oldest.ContentKey, older.ContentKey]
                : [];
            catalog.UninstalledVersions.Should().BeEquivalentTo(expectedUninstalls);
            catalog.UninstalledVersions.Should().NotContain(latest.ContentKey);
        });
    }

    [Fact]
    public void SuccessfulDownloadHoldsLifecycleUntilIntegritySnapshot_Completes()
    {
        StaTestRunner.Run(async () =>
        {
            StubPackageDownloadService downloadService = new(PackageDownloadResult.Succeeded());
            var snapshotCompletion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            ILaunchContentIntegrityResolutionService resolutionService =
                Substitute.For<ILaunchContentIntegrityResolutionService>();
            resolutionService.CaptureManagedInstallSnapshotAsync(
                    Arg.Any<LaunchContentIntegrityVersionRequest>(),
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
                Arg.Any<LaunchContentIntegrityVersionRequest>(),
                Arg.Any<CancellationToken>());
            Task<PackageDownloadResult> lifecycle = activityService.GetActiveDownloadTask(viewModel)!;

            lifecycle.Should().BeSameAs(activityService.ActiveDownloadTask);
            lifecycle.IsCompleted.Should().BeFalse();
            activityService.IsActive.Should().BeTrue();

            snapshotCompletion.SetResult();
            await start.WaitAsync(TestTimeouts.Wait);

            lifecycle.IsCompletedSuccessfully.Should().BeTrue();
            activityService.IsActive.Should().BeFalse();
        });
    }

    [Fact]
    public void PostInstallFailure_PreservesCommittedSuccessAndShowsWarning()
    {
        StaTestRunner.Run(async () =>
        {
            StubPackageDownloadService downloadService = new(PackageDownloadResult.Succeeded());
            var catalog = new FakeLauncherContentCatalog
            {
                LocalDataUpdateHandler = () => throw new IOException("catalog update failed")
            };
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherPackageActivityService activityService = new();
            LauncherModificationDownloadCoordinator coordinator = CreateCoordinator(
                downloadService,
                activityService,
                dialogService,
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
    public void QueuedProgressFromCompletedOperation_CannotOverwriteTerminalState()
    {
        StaTestRunner.Run(async () =>
        {
            ControllablePackageDownloadService downloadService = new();
            LauncherPackageActivityService activityService = new();
            LauncherModificationDownloadCoordinator coordinator = CreateCoordinator(
                downloadService,
                activityService);
            ModificationViewModel viewModel = CreateViewModel(activityService);

            Task start = coordinator.StartDownloadAsync(viewModel, new Window(), () => { });
            await downloadService.Started.Task.WaitAsync(TestTimeouts.Wait);
            downloadService.Report(new PackageUpdateProgress(100, 25, 25, "package.big"));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            viewModel.ProgressValue.Should().Be(25);

            downloadService.Complete(PackageDownloadResult.Succeeded());
            await start.WaitAsync(TestTimeouts.Wait);
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
        FakeLauncherContentCatalog? catalog = null,
        bool autoDeleteOldVersions = false)
    {
        ILauncherDialogService resolvedDialogService = dialogService ?? Substitute.For<ILauncherDialogService>();
        ILauncherPreferencesService preferencesService = Substitute.For<ILauncherPreferencesService>();
        preferencesService.Current.Returns(new LauncherPreferences
        {
            Shared = new LauncherSharedPreferences
            {
                AutoDeleteOldVersions = autoDeleteOldVersions
            }
        });
        FakeLauncherContentCatalog resolvedCatalog = catalog ?? new FakeLauncherContentCatalog();
        FakeStringLocalizer stringLocalizer = CreateStringLocalizer();

        return new LauncherModificationDownloadCoordinator(
            preferencesService,
            resolvedCatalog,
            downloadService,
            TestLaunchContentIntegrityCoordinator.Create(
                resolutionService,
                resolvedCatalog,
                packageActivityService: packageActivityService,
                dialogService: resolvedDialogService,
                stringLocalizer: stringLocalizer),
            packageActivityService,
            new LauncherPackageActivityAdmissionService(
                packageActivityService,
                resolvedDialogService,
                stringLocalizer,
                NullLogger<LauncherPackageActivityAdmissionService>.Instance),
            new LauncherTileActionService(resolvedCatalog),
            resolvedDialogService,
            stringLocalizer,
            NullLogger<LauncherModificationDownloadCoordinator>.Instance);
    }

    private static ModificationViewModel CreateViewModel(
        LauncherPackageActivityService packageActivityService,
        string name = "Shockwave")
    {
        LauncherContentVersion version = CreateVersion(name, "1.0");
        return CreateViewModel(packageActivityService, new LauncherContent(version));
    }

    private static ModificationViewModel CreateViewModel(
        LauncherPackageActivityService packageActivityService,
        LauncherContent modification)
    {
        return new ModificationViewModel(
            modification,
            new ModificationImageSourceFactory(NullLogger<ModificationImageSourceFactory>.Instance),
            TestLauncherRuntimeContext.Create(),
            Substitute.For<IModificationImageFileService>(),
            CreateStringLocalizer(),
            packageActivityService,
            NullLogger<ModificationViewModel>.Instance);
    }

    private static LauncherContentVersion CreateVersion(
        string name,
        string version,
        bool installed = false)
    {
        return new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation
            {
                Installed = installed,
                ContentSourceKind = ContentSourceKind.ManagedSingleFile
            },
            Name = name,
            Version = version,
            ModificationType = ModificationType.Mod,
            SimpleDownloadLink = "https://example.test/package.zip"
        };
    }

    private static FakeStringLocalizer CreateStringLocalizer()
    {
        return FakeStringLocalizer.Create(TestLocalizedStrings.Launch);
    }
}
