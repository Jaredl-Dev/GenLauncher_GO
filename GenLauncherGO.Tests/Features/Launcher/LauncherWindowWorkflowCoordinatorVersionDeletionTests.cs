using System;
using System.Linq;
using Avalonia.Controls;
using GenLauncherGO.Features.Integrity;
using GenLauncherGO.Features.Launcher;
using GenLauncherGO.Features.Mods;
using GenLauncherGO.Shared.Dialogs;

namespace GenLauncherGO.Tests.Features.Launcher;

public sealed partial class LauncherWindowWorkflowCoordinatorTests
{
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
            ModificationViewModel viewModel = CreateTile(
                new LauncherContent(CreateVersion("Shockwave", ContentSourceKind.Manual)));
            ModificationVersionSelection versionSelection = new(
                viewModel.LatestVersion,
                viewModel);
            WorkflowFixture fixture = new(catalog);
            fixture.AddTile(viewModel);

            await coordinator.DeleteVersionAsync(fixture.Context, versionSelection);

            catalog.DiscardedContents.Should().BeEmpty();
            catalog.UninstalledVersions.Should().BeEmpty();
            fixture.ViewModel.ModsListSource.Should().ContainSingle()
                .Which.Should().BeSameAs(viewModel);
        });
    }

    [Fact]
    public void DeleteVersion_WhenRemovingModCard_ShowsWarningMentioningPatchesAndAddonsAndDiscardsContent()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowWarningConfirmationAsync(
                    Arg.Any<LauncherInfoDialogRequest>(),
                    Arg.Any<string>(),
                    Arg.Any<Window>())
                .Returns(true);
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(
                dialogService: dialogService,
                catalog: catalog);
            ModificationViewModel viewModel = CreateTile(
                new LauncherContent(CreateVersion("Shockwave", ContentSourceKind.Manual)));
            ModificationVersionSelection versionSelection = new(
                viewModel.LatestVersion,
                viewModel);
            WorkflowFixture fixture = new(catalog);
            fixture.AddTile(viewModel);

            await coordinator.DeleteVersionAsync(fixture.Context, versionSelection);

            await dialogService.Received(1).ShowWarningConfirmationAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request.DetailMessage.Contains("patches and add-ons will be deleted", StringComparison.OrdinalIgnoreCase)),
                Arg.Any<string>(),
                Arg.Any<Window>());
            catalog.DiscardedContents.Should().ContainSingle()
                .Which.HasName("Shockwave").Should().BeTrue();
            fixture.ViewModel.ModsListSource.Should().BeEmpty();
        });
    }

    [Fact]
    public void DeleteVersion_WhenAnotherVersionRemains_ShowsStandardRemovalWarning()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowWarningConfirmationAsync(
                    Arg.Any<LauncherInfoDialogRequest>(),
                    Arg.Any<string>(),
                    Arg.Any<Window>())
                .Returns(true);
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(
                dialogService: dialogService,
                catalog: catalog);
            LauncherContent content = new(TestLauncherContent.Version("Shockwave", "1.0", installed: true, sourceKind: ContentSourceKind.Manual));
            LauncherContentVersion secondVersion = TestLauncherContent.Version("Shockwave", "2.0", installed: true, sourceKind: ContentSourceKind.Manual);
            content.AddOrMergeVersion(secondVersion);
            ModificationViewModel viewModel = CreateTile(content);
            ModificationVersionSelection versionSelection = new(
                viewModel.ContainerModification.Versions.First(v => v.Version == "1.0"),
                viewModel);
            WorkflowFixture fixture = new(catalog);
            fixture.AddTile(viewModel);

            await coordinator.DeleteVersionAsync(fixture.Context, versionSelection);

            await dialogService.Received(1).ShowWarningConfirmationAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    !request.DetailMessage.Contains("patches and add-ons", StringComparison.OrdinalIgnoreCase)),
                Arg.Any<string>(),
                Arg.Any<Window>());
            catalog.UninstalledVersions.Should().ContainSingle()
                .Which.Version.Should().Be("1.0");
            fixture.ViewModel.ModsListSource.Should().ContainSingle();
        });
    }

    [Fact]
    public void DeleteModification_WhenRemovingUninstalledMod_ShowsConfirmationMentioningPatchesAndAddonsAndDiscardsContent()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowWarningConfirmationAsync(
                    Arg.Any<LauncherInfoDialogRequest>(),
                    Arg.Any<string>(),
                    Arg.Any<Window>())
                .Returns(true);
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(
                dialogService: dialogService,
                catalog: catalog);
            LauncherContentVersion version = CreateVersion("Shockwave", ContentSourceKind.ManagedSingleFile);
            version.Installation.Installed = false;
            ModificationViewModel viewModel = CreateTile(new LauncherContent(version));
            WorkflowFixture fixture = new(catalog);
            fixture.AddTile(viewModel);

            await DeleteModificationAsync(coordinator, fixture, viewModel);

            await dialogService.Received(1).ShowWarningConfirmationAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request.DetailMessage.Contains("patches and add-ons will also be deleted", StringComparison.OrdinalIgnoreCase)),
                Arg.Any<string>(),
                Arg.Any<Window>());
            catalog.DiscardedContents.Should().ContainSingle()
                .Which.HasName("Shockwave").Should().BeTrue();
            fixture.ViewModel.ModsListSource.Should().BeEmpty();
        });
    }
}
