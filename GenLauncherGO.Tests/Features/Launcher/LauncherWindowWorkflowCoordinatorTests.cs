using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Features.Integrity;
using GenLauncherGO.Features.Launcher;
using GenLauncherGO.Features.Mods;
using GenLauncherGO.Shared.Dialogs;

namespace GenLauncherGO.Tests.Features.Launcher;

[Collection("Avalonia")]
public sealed partial class LauncherWindowWorkflowCoordinatorTests
{
    [Fact]
    public void AddRepositoryModificationAsyncWhenSelection_IsReturnedAddsModification()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowModificationSelectionAsync(
                    Arg.Any<IReadOnlyList<string>>(),
                    Arg.Any<Window?>())
                .Returns("Shockwave");
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherContentVersion repositoryVersion =
                CreateVersion("Shockwave", ContentSourceKind.ManagedSingleFile);
            catalog.DownloadHandler = (_, _) => Task.FromResult(repositoryVersion);
            LauncherContentActionCoordinator coordinator = CreateContentActionCoordinator(dialogService: dialogService);
            WorkflowFixture fixture = new(catalog);

            await coordinator.AddRepositoryModificationAsync(fixture.Context);

            fixture.ViewModel.ModsListSource.Should().ContainSingle()
                .Which.ContainerModification.Should().BeSameAs(catalog.Data.Modifications.Single());
        });
    }
}
