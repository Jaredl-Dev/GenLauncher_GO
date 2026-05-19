using System.Threading.Tasks;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Mods;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Services;

public sealed partial class LauncherWindowWorkflowCoordinatorTests
{
    [Fact]
    public void UpdateModificationAsync_ActiveDownload_PausesAndResumesLifecycle()
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

            await UpdateModificationAsync(coordinator, fixture, viewModel);
            viewModel.UpdateButtonContent.Should().Be("Resume");
            downloadService.CancellationObserved.Task.IsCompleted.Should().BeFalse();

            await UpdateModificationAsync(coordinator, fixture, viewModel);
            viewModel.UpdateButtonContent.Should().Be("Pause");
            downloadService.CancellationObserved.Task.IsCompleted.Should().BeFalse();

            Task cancellation = DeleteModificationAsync(coordinator, fixture, viewModel);
            await downloadService.CancellationObserved.Task.WaitAsync(TestTimeouts.Wait);
            downloadService.Release();
            await Task.WhenAll(download, cancellation);
        });
    }

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
            catalog.DiscardedVersions.Should().BeEmpty();
            catalog.LocalDataUpdateCount.Should().Be(1);
            catalog.SaveCount.Should().Be(1);
            fixture.ViewModel.ModsListSource.Should().ContainSingle()
                .Which.Should().BeSameAs(viewModel);
            viewModel.ContainerModification.Installed.Should().BeFalse();
            viewModel.IsSelected.Should().BeTrue();
            viewModel.VersionActionContent.Should().Be("Remove from list");
            await dialogService.Received(1).ShowWarningConfirmationAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Cancel download" &&
                    request.DetailMessage == "Cancel Shockwave and delete content downloaded so far"),
                "Yes",
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
            catalog.DiscardedVersions.Should().BeEmpty();
            viewModel.LatestVersion.Installation.Installed.Should().BeTrue();
            fixture.ViewModel.ModsListSource.Should().ContainSingle()
                .Which.Should().BeSameAs(viewModel);
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
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Compatibility" &&
                    request.DetailMessage == "Shockwave is deprecated"),
                null,
                fixture.Owner);
        });
    }

    [Fact]
    public void DeleteModificationAsyncForUninstalledMod_RemovesCardImmediately()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = StubLauncherDialogService.AnsweringWarningConfirmations(true);
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherPackageActivityService packageActivityService = new();
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(
                packageActivityService,
                dialogService,
                catalog: catalog);
            LauncherContentVersion version = CreateVersion(
                "Shockwave",
                ContentSourceKind.ManagedSingleFile);
            version.Installation.Installed = false;
            ModificationViewModel viewModel = CreateTile(
                CreateModification("Shockwave", ModificationType.Mod, version),
                packageActivityService);
            WorkflowFixture fixture = new(catalog, packageActivityService);
            fixture.AddTile(viewModel);

            await DeleteModificationAsync(coordinator, fixture, viewModel);

            catalog.DiscardedVersions.Should().ContainSingle().Which.Should().Match<LauncherContentKey>(removed =>
                removed.Name == "Shockwave" &&
                removed.Version == "1.0");
            fixture.ViewModel.ModsListSource.Should().BeEmpty();
            catalog.SaveCount.Should().Be(1);
            await dialogService.Received(1).ShowWarningConfirmationAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Remove from list?" &&
                    request.DetailMessage == "Remove Shockwave from list"),
                "Remove from list",
                fixture.Owner);
        });
    }

    [Fact]
    public void DeleteModificationAsyncForUninstalledModWhenRemoval_IsDeclinedPreservesCard()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherPackageActivityService packageActivityService = new();
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(
                packageActivityService,
                dialogService,
                catalog: catalog);
            LauncherContentVersion version = CreateVersion(
                "Shockwave",
                ContentSourceKind.ManagedSingleFile);
            version.Installation.Installed = false;
            ModificationViewModel viewModel = CreateTile(
                CreateModification("Shockwave", ModificationType.Mod, version),
                packageActivityService);
            WorkflowFixture fixture = new(catalog, packageActivityService);
            fixture.AddTile(viewModel);

            await DeleteModificationAsync(coordinator, fixture, viewModel);

            catalog.DiscardedVersions.Should().BeEmpty();
            catalog.SaveCount.Should().Be(0);
            fixture.ViewModel.ModsListSource.Should().ContainSingle()
                .Which.Should().BeSameAs(viewModel);
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
            catalog.DiscardedVersions.Should().BeEmpty();
            viewModel.LatestVersion.Installation.DownloadSuspended.Should().BeTrue();
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
            catalog.DiscardedVersions.Should().BeEmpty();
            fixture.ViewModel.ModsListSource.Should().ContainSingle()
                .Which.Should().BeSameAs(viewModel);
        });
    }
}
