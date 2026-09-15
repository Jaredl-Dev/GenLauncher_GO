using System.Threading.Tasks;
using GenLauncherGO.Features.Integrity;
using GenLauncherGO.Features.Launcher;
using GenLauncherGO.Features.Mods;
using GenLauncherGO.Features.Updating;
using GenLauncherGO.Shared.Dialogs;

namespace GenLauncherGO.Tests.Features.Launcher;

public sealed partial class LauncherWindowWorkflowCoordinatorTests
{
    [Fact]
    public void DeleteModificationAsyncForActiveModDownload_CancelsLifecycleAndRemovesPartialContent()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = StubLauncherDialogService.AnsweringWarningConfirmations(true);
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherPackageActivityService packageActivityService = new();
            ControllablePackageDownloadService downloadService = new();
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(
                packageActivityService,
                dialogService,
                catalog: catalog,
                packageDownloadService: downloadService);
            ModificationViewModel viewModel = CreateManagedShockwaveTile(packageActivityService);
            WorkflowFixture fixture = new(catalog, packageActivityService);
            fixture.AddTile(viewModel);

            Task download = UpdateModificationAsync(coordinator, fixture, viewModel);
            await downloadService.Started.Task.WaitAsync(TestTimeouts.Wait);
            Task cancellation = DeleteModificationAsync(coordinator, fixture, viewModel);
            await downloadService.CancellationObserved.Task.WaitAsync(TestTimeouts.Wait);
            downloadService.Release();
            await Task.WhenAll(download, cancellation);

            catalog.UninstalledVersions.Should().ContainSingle().Which.Should().Match<LauncherContentKey>(contentKey =>
                contentKey.Name == "Shockwave" &&
                contentKey.Version == "1.0" &&
                contentKey.ContentType == ModificationType.Mod);
            catalog.DiscardedContents.Should().BeEmpty();
            catalog.LocalDataUpdateCount.Should().Be(1);
            catalog.SaveCount.Should().Be(1);
            fixture.ViewModel.ModsListSource.Should().ContainSingle()
                .Which.Should().BeSameAs(viewModel);
            viewModel.ContainerModification.Installed.Should().BeFalse();
            viewModel.IsSelected.Should().BeTrue();
            viewModel.VersionActionContent.Should().Be("Remove from list");
            await dialogService.Received(1).ShowWarningConfirmationAsync(
                Arg.Any<LauncherInfoDialogRequest>(),
                Arg.Any<string?>(),
                fixture.Owner);
        });
    }

    /// <summary>
    ///     Cancellation loses this race: the install already committed, so throwing the content away would delete a
    ///     version the user now has.
    /// </summary>
    [Fact]
    public void DeleteModificationAsyncWhenTheDownloadSucceedsFirst_KeepsTheInstalledVersion()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = StubLauncherDialogService.AnsweringWarningConfirmations(true);
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherPackageActivityService packageActivityService = new();
            ControllablePackageDownloadService downloadService = new();
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(
                packageActivityService,
                dialogService,
                catalog: catalog,
                packageDownloadService: downloadService);
            ModificationViewModel viewModel = CreateManagedShockwaveTile(packageActivityService);
            WorkflowFixture fixture = new(catalog, packageActivityService);
            fixture.AddTile(viewModel);

            Task download = UpdateModificationAsync(coordinator, fixture, viewModel);
            await downloadService.Started.Task.WaitAsync(TestTimeouts.Wait);
            Task cancellation = DeleteModificationAsync(coordinator, fixture, viewModel);
            await downloadService.CancellationObserved.Task.WaitAsync(TestTimeouts.Wait);
            downloadService.Complete(PackageDownloadResult.Succeeded());
            await Task.WhenAll(download, cancellation);

            catalog.UninstalledVersions.Should().BeEmpty();
            catalog.DiscardedContents.Should().BeEmpty();
            viewModel.LatestVersion.Installation.Installed.Should().BeTrue();
            fixture.ViewModel.ModsListSource.Should().ContainSingle()
                .Which.Should().BeSameAs(viewModel);
        });
    }

    [Fact]
    public void DeleteModificationAsyncWhenDownloadCompletesBeforeConfirmation_PreservesInstalledVersion()
    {
        StaTestRunner.Run(async () =>
        {
            TaskCompletionSource<bool> confirmationEntered = new();
            Task? download = null;
            ControllablePackageDownloadService downloadService = new();
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowWarningConfirmationAsync(
                    Arg.Any<LauncherInfoDialogRequest>(),
                    Arg.Any<string?>(),
                    Arg.Any<Avalonia.Controls.Window?>())
                .Returns(async _ =>
                {
                    confirmationEntered.TrySetResult(true);
                    downloadService.Complete(PackageDownloadResult.Succeeded());
                    if (download is not null)
                    {
                        await download;
                    }

                    return true;
                });
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherPackageActivityService packageActivityService = new();
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(
                packageActivityService,
                dialogService,
                catalog: catalog,
                packageDownloadService: downloadService);
            ModificationViewModel viewModel = CreateManagedShockwaveTile(packageActivityService);
            WorkflowFixture fixture = new(catalog, packageActivityService);
            fixture.AddTile(viewModel);

            download = UpdateModificationAsync(coordinator, fixture, viewModel);
            await downloadService.Started.Task.WaitAsync(TestTimeouts.Wait);
            Task cancellation = DeleteModificationAsync(coordinator, fixture, viewModel);
            await confirmationEntered.Task.WaitAsync(TestTimeouts.Wait);
            await Task.WhenAll(download, cancellation);

            catalog.UninstalledVersions.Should().BeEmpty();
            viewModel.LatestVersion.Installation.Installed.Should().BeTrue();
            viewModel.ProgressMessage.Should().NotBe("Download canceled");
        });
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void UpdateModificationAsyncForDeprecatedContent_OnlyDownloadsWhenConfirmed(
        bool confirmed,
        bool expectedUpdateButtonEnabled)
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = StubLauncherDialogService.AnsweringWarningConfirmations(confirmed);
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherPackageActivityService packageActivityService = new();
            ControllablePackageDownloadService downloadService = new();
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(
                packageActivityService,
                dialogService,
                catalog: catalog,
                packageDownloadService: downloadService);
            ModificationViewModel viewModel = CreateTile(
                TestLauncherContent.From(TestLauncherContent.Version(
                    "Shockwave",
                    isSelected: true,
                    sourceKind: ContentSourceKind.ManagedSingleFile,
                    simpleDownloadLink: "https://example.test/package.zip",
                    deprecated: true)),
                packageActivityService);
            WorkflowFixture fixture = new(catalog, packageActivityService);
            fixture.AddTile(viewModel);

            Task download = UpdateModificationAsync(coordinator, fixture, viewModel);
            if (confirmed)
            {
                await downloadService.Started.Task.WaitAsync(TestTimeouts.Wait);
                downloadService.Release();
            }

            await download.WaitAsync(TestTimeouts.Wait);

            downloadService.CallCount.Should().Be(confirmed ? 1 : 0);
            viewModel.LatestVersion.Installation.Installed.Should().Be(confirmed);
            viewModel.UpdateButtonEnabled.Should().Be(expectedUpdateButtonEnabled);
            await dialogService.Received(1).ShowWarningConfirmationAsync(
                Arg.Any<LauncherInfoDialogRequest>(),
                Arg.Any<string?>(),
                fixture.Owner);
        });
    }

    /// <summary>
    ///     Closing stops an in-flight download but must never discard it: the partial content is what a later session
    ///     resumes from, so uninstalling the version here would silently throw away the user's transfer.
    /// </summary>
    [Fact]
    public void PrepareForCloseAsyncSuspendsTheDownloadAnd_KeepsItsPartialContent()
    {
        StaTestRunner.Run(async () =>
        {
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherPackageActivityService packageActivityService = new();
            ControllablePackageDownloadService downloadService = new();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                packageActivityService,
                catalog: catalog);
            LauncherContentActionCoordinator contentActionCoordinator = CreateContentActionCoordinator(
                packageActivityService,
                catalog: catalog,
                packageDownloadService: downloadService);
            ModificationViewModel viewModel = CreateManagedShockwaveTile(packageActivityService);
            WorkflowFixture fixture = new(catalog, packageActivityService);
            fixture.AddTile(viewModel);

            Task download = UpdateModificationAsync(contentActionCoordinator, fixture, viewModel);
            await downloadService.Started.Task.WaitAsync(TestTimeouts.Wait);
            Task closePreparation = coordinator.PrepareForCloseAsync();
            await downloadService.CancellationObserved.Task.WaitAsync(TestTimeouts.Wait);

            closePreparation.IsCompleted.Should().BeFalse();
            fixture.ViewModel.ModsListSource.Should().ContainSingle();

            downloadService.Release();
            await Task.WhenAll(download, closePreparation);

            catalog.UninstalledVersions.Should().BeEmpty();
            catalog.DiscardedContents.Should().BeEmpty();
            viewModel.LatestVersion.Installation.DownloadSuspended.Should().BeTrue();
            viewModel.VersionActionContent.Should().Be("Cancel Download");
            viewModel.IsVersionActionVisible.Should().BeTrue();
            fixture.ViewModel.ModsListSource.Should().ContainSingle()
                .Which.Should().BeSameAs(viewModel);
        });
    }

    [Fact]
    public void PrepareForCloseAsyncAndButtonCancellationRace_RunsRegisteredCleanupOnce()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = StubLauncherDialogService.AnsweringWarningConfirmations(true);
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherPackageActivityService packageActivityService = new();
            ControllablePackageDownloadService downloadService = new();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                packageActivityService,
                dialogService,
                catalog: catalog);
            LauncherContentActionCoordinator contentActionCoordinator = CreateContentActionCoordinator(
                packageActivityService,
                dialogService,
                catalog: catalog,
                packageDownloadService: downloadService);
            ModificationViewModel viewModel = CreateManagedShockwaveTile(packageActivityService);
            WorkflowFixture fixture = new(catalog, packageActivityService);
            fixture.AddTile(viewModel);

            Task download = UpdateModificationAsync(contentActionCoordinator, fixture, viewModel);
            await downloadService.Started.Task.WaitAsync(TestTimeouts.Wait);
            Task buttonCancellation = DeleteModificationAsync(contentActionCoordinator, fixture, viewModel);
            Task closePreparation = coordinator.PrepareForCloseAsync();
            await downloadService.CancellationObserved.Task.WaitAsync(TestTimeouts.Wait);

            buttonCancellation.IsCompleted.Should().BeFalse();
            closePreparation.IsCompleted.Should().BeFalse();

            downloadService.Release();
            await Task.WhenAll(download, buttonCancellation, closePreparation);

            catalog.UninstalledVersions.Should().ContainSingle();
            catalog.DiscardedContents.Should().BeEmpty();
            fixture.ViewModel.ModsListSource.Should().ContainSingle()
                .Which.Should().BeSameAs(viewModel);
        });
    }

    [Fact]
    public void DeleteModificationAsyncForSuspendedModDownload_CancelsSuspendedDownloadAndRemovesPartialContent()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = StubLauncherDialogService.AnsweringWarningConfirmations(true);
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherPackageActivityService packageActivityService = new();
            LauncherContentActionCoordinator contentActionCoordinator = CreateContentActionCoordinator(
                packageActivityService,
                dialogService,
                catalog: catalog);
            ModificationViewModel viewModel = CreateManagedShockwaveTile(packageActivityService);
            viewModel.LatestVersion.Installation.DownloadSuspended = true;
            viewModel.LatestVersion.Installation.SuspendedProgressPercentage = 42;
            viewModel.RefreshFromModelAndPresentation();
            WorkflowFixture fixture = new(catalog, packageActivityService);
            fixture.AddTile(viewModel);

            viewModel.VersionActionContent.Should().Be("Cancel Download");
            viewModel.IsVersionActionVisible.Should().BeTrue();

            catalog.SaveHandler = () => viewModel.LatestVersion.Installation.DownloadSuspended.Should().BeFalse();
            await DeleteModificationAsync(contentActionCoordinator, fixture, viewModel);

            catalog.UninstalledVersions.Should().ContainSingle().Which.Should().Match<LauncherContentKey>(contentKey =>
                contentKey.Name == "Shockwave" &&
                contentKey.Version == "1.0" &&
                contentKey.ContentType == ModificationType.Mod);
            catalog.DiscardedContents.Should().BeEmpty();
            viewModel.LatestVersion.Installation.DownloadSuspended.Should().BeFalse();
            viewModel.VersionActionContent.Should().Be("Remove from list");
            fixture.ViewModel.ModsListSource.Should().ContainSingle()
                .Which.Should().BeSameAs(viewModel);
            await dialogService.Received(1).ShowWarningConfirmationAsync(
                Arg.Any<LauncherInfoDialogRequest>(),
                Arg.Any<string?>(),
                fixture.Owner);
        });
    }

    [Fact]
    public void DeleteModificationAsyncForSuspendedModDownloadDeclined_KeepsSuspendedDownload()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = StubLauncherDialogService.AnsweringWarningConfirmations(false);
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherPackageActivityService packageActivityService = new();
            LauncherContentActionCoordinator contentActionCoordinator = CreateContentActionCoordinator(
                packageActivityService,
                dialogService,
                catalog: catalog);
            ModificationViewModel viewModel = CreateManagedShockwaveTile(packageActivityService);
            viewModel.LatestVersion.Installation.DownloadSuspended = true;
            viewModel.LatestVersion.Installation.SuspendedProgressPercentage = 42;
            viewModel.RefreshFromModelAndPresentation();
            WorkflowFixture fixture = new(catalog, packageActivityService);
            fixture.AddTile(viewModel);

            await DeleteModificationAsync(contentActionCoordinator, fixture, viewModel);

            catalog.UninstalledVersions.Should().BeEmpty();
            catalog.DiscardedContents.Should().BeEmpty();
            viewModel.LatestVersion.Installation.DownloadSuspended.Should().BeTrue();
            viewModel.VersionActionContent.Should().Be("Cancel Download");
            fixture.ViewModel.ModsListSource.Should().ContainSingle()
                .Which.Should().BeSameAs(viewModel);
            await dialogService.Received(1).ShowWarningConfirmationAsync(
                Arg.Any<LauncherInfoDialogRequest>(),
                Arg.Any<string?>(),
                fixture.Owner);
        });
    }
}
