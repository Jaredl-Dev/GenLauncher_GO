using System;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Mods;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Services;

public sealed partial class LauncherWindowWorkflowCoordinatorTests
{
    [Fact]
    public void DeleteVersionForMod_RemovesContentCardAndRefreshesTabs()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = StubLauncherDialogService.AnsweringWarningConfirmations(true);
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(
                dialogService: dialogService,
                catalog: catalog);
            ModificationViewModel viewModel = CreateTile(CreateModification(
                "Shockwave",
                ModificationType.Mod,
                CreateVersion("Shockwave", ContentSourceKind.Manual)));
            ModificationVersionSelection versionSelection = new(
                viewModel.LatestVersion,
                viewModel);
            WorkflowFixture fixture = new(catalog);
            fixture.AddTile(viewModel);

            await coordinator.DeleteVersionAsync(
                fixture.Context,
                versionSelection);

            catalog.DiscardedContents.Should().ContainSingle().Which.Should().Match<LauncherContentKey>(contentKey =>
                contentKey.Name == "Shockwave" &&
                contentKey.Version == "1.0" &&
                contentKey.ContentType == ModificationType.Mod);
            catalog.LocalDataUpdateCount.Should().Be(1);
            catalog.SaveCount.Should().Be(1);
            fixture.ViewModel.ModsListSource.Should().BeEmpty();
            await dialogService.Received(1).ShowWarningConfirmationAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Remove content?" &&
                    request.DetailMessage == "Remove Shockwave"),
                "Remove",
                fixture.Owner);
        });
    }

    [Fact]
    public void DeleteVersionForRemoteChildContent_RefreshesTileAndLabels()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = StubLauncherDialogService.AnsweringWarningConfirmations(true);
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherPackageActivityService packageActivityService = new();
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(
                packageActivityService: packageActivityService,
                dialogService: dialogService,
                catalog: catalog);
            ModificationViewModel viewModel = CreateTile(CreateModification(
                "HD",
                ModificationType.Addon,
                CreateVersion("HD", ContentSourceKind.ManagedSingleFile)));
            int uiThreadId = Environment.CurrentManagedThreadId;
            int? deletionThreadId = null;
            bool packageActivityWasActive = false;
            catalog.UninstallVersionHandler = _ =>
            {
                deletionThreadId = Environment.CurrentManagedThreadId;
                packageActivityWasActive = packageActivityService.IsActive;
            };
            ModificationVersionSelection versionSelection = new(
                viewModel.LatestVersion,
                viewModel);
            WorkflowFixture fixture = new(catalog, packageActivityService);
            fixture.AddTile(viewModel);
            viewModel.IsSelected = false;

            await coordinator.DeleteVersionAsync(
                fixture.Context,
                versionSelection);

            catalog.UninstalledVersions.Should().ContainSingle().Which.Should().Match<LauncherContentKey>(contentKey =>
                contentKey.Name == "HD" &&
                contentKey.Version == "1.0" &&
                contentKey.ContentType == ModificationType.Addon);
            catalog.LocalDataUpdateCount.Should().Be(1);
            catalog.SaveCount.Should().Be(1);
            fixture.ViewModel.AddonsListSource.Should().ContainSingle()
                .Which.Should().BeSameAs(viewModel);
            viewModel.IsSelected.Should().BeTrue();
            deletionThreadId.Should().NotBe(uiThreadId);
            packageActivityWasActive.Should().BeTrue();
            packageActivityService.IsActive.Should().BeFalse();
            fixture.ViewModel.MainControlsEnabled.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData(ModificationType.Addon)]
    [InlineData(ModificationType.Patch)]
    public void DeleteVersionForManualChildContent_RemovesContentCardAndRefreshesTabs(
        ModificationType modificationType)
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = StubLauncherDialogService.AnsweringWarningConfirmations(true);
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(
                dialogService: dialogService,
                catalog: catalog);
            ModificationViewModel viewModel = CreateTile(CreateModification(
                "Manual Child",
                modificationType,
                CreateVersion("Manual Child", ContentSourceKind.Manual)));
            ModificationVersionSelection versionSelection = new(
                viewModel.LatestVersion,
                viewModel);
            WorkflowFixture fixture = new(catalog);
            fixture.AddTile(viewModel);

            await coordinator.DeleteVersionAsync(
                fixture.Context,
                versionSelection);

            catalog.DiscardedContents.Should().ContainSingle().Which.Should().Match<LauncherContentKey>(contentKey =>
                contentKey.Name == "Manual Child" &&
                contentKey.Version == "1.0" &&
                contentKey.ContentType == modificationType);
            catalog.LocalDataUpdateCount.Should().Be(1);
            catalog.SaveCount.Should().Be(1);
            if (modificationType == ModificationType.Patch)
            {
                fixture.ViewModel.PatchesListSource.Should().BeEmpty();
            }
            else
            {
                fixture.ViewModel.AddonsListSource.Should().BeEmpty();
            }
        });
    }

    [Fact]
    public void DeleteVersionWhenRemoval_IsDeclinedPreservesInstalledContent()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(
                dialogService: dialogService,
                catalog: catalog);
            ModificationViewModel viewModel = CreateTile(CreateModification(
                "Shockwave",
                ModificationType.Mod,
                CreateVersion("Shockwave", ContentSourceKind.Manual)));
            ModificationVersionSelection versionSelection = new(
                viewModel.LatestVersion,
                viewModel);
            WorkflowFixture fixture = new(catalog);
            fixture.AddTile(viewModel);

            await coordinator.DeleteVersionAsync(
                fixture.Context,
                versionSelection);

            catalog.DiscardedContents.Should().BeEmpty();
            catalog.UninstalledVersions.Should().BeEmpty();
            catalog.SaveCount.Should().Be(0);
            fixture.ViewModel.ModsListSource.Should().ContainSingle()
                .Which.Should().BeSameAs(viewModel);
        });
    }
}
