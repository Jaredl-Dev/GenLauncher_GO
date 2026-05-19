using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Mods.Services;
using GenLauncherGO.Core.Remote;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Shell.Contracts;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Core.Updating.Contracts;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Contracts;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Launcher.Support;
using GenLauncherGO.UI.Features.Launcher.ViewModels;
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Features.Startup.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Services;

public sealed class LauncherWindowWorkflowCoordinatorTests
{
    [Fact]
    public void LinkActionsOpenTheirConfiguredUris()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                out ILauncherShellService shellService);
            LauncherContent modification = CreateModification(
                "Shockwave",
                ModificationType.Mod,
                CreateVersion("Shockwave", ContentSourceKind.Manual),
                newsLink: "https://example.test/news",
                networkInfo: "https://example.test/network",
                modDbLink: "https://example.test/moddb",
                discordLink: "https://example.test/discord");
            ModificationViewModel viewModel = CreateTile(modification);

            coordinator.OpenChangeLog(viewModel);
            coordinator.OpenNetworkInfo(viewModel);
            coordinator.OpenModDb(viewModel);
            coordinator.OpenDiscord(viewModel);

            shellService.Received(1).OpenUri("https://example.test/news");
            shellService.Received(1).OpenUri("https://example.test/network");
            shellService.Received(1).OpenUri("https://example.test/moddb");
            shellService.Received(1).OpenUri("https://example.test/discord");
        });
    }

    [Fact]
    public void OpenSupportShowsThankYouAndOpensSupportLink()
    {
        StaTestRunner.Run(() =>
        {
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                out ILauncherShellService shellService);
            ModificationViewModel viewModel = CreateTile(CreateModification(
                "Shockwave",
                ModificationType.Mod,
                CreateVersion("Shockwave", ContentSourceKind.Manual),
                supportLink: "https://example.test/support"));

            coordinator.OpenSupport(viewModel);

            viewModel.ProgressMessage.Should().Be("Thank you");
            shellService.Received(1).OpenUri("https://example.test/support");
        });
    }

    [Fact]
    public void UpdateModificationAsyncForAdvertisingOpensAdvertisingLinkWithoutSelectingContent()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                out ILauncherShellService shellService);
            ModificationViewModel viewModel = CreateTile(CreateModification(
                "Donate",
                ModificationType.Advertising,
                CreateVersion("Donate", ContentSourceKind.UnknownLegacy),
                simpleDownloadLink: "https://example.test/donate"));
            WorkflowFixture fixture = new();
            fixture.AddTile(viewModel);

            await coordinator.UpdateModificationAsync(
                fixture.ViewModel,
                fixture.Content,
                fixture.Owner,
                viewModel);

            viewModel.ProgressMessage.Should().Be("Thank you");
            shellService.Received(1).OpenUri("https://example.test/donate");
            fixture.ViewModel.SelectedModifications.Should().BeEmpty();
        });
    }

    [Fact]
    public void ChangeVersionImageAsyncForManualContentReplacesImageAndRestoresControls()
    {
        StaTestRunner.Run(async () =>
        {
            IModificationImageFileService imageFileService = Substitute.For<IModificationImageFileService>();
            imageFileService.ReplaceImageAsync(
                    Arg.Any<ModificationImageReplacementRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(@"C:\Cache\Shockwave\1.0.png"));
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                filePicker: new StubLauncherFilePicker(pickedImageFile: @"C:\Pictures\custom.png"),
                modificationImageFileService: imageFileService);
            ModificationViewModel viewModel = CreateTile(CreateModification(
                "Shockwave",
                ModificationType.Mod,
                CreateVersion("Shockwave", ContentSourceKind.Manual)));
            WorkflowFixture fixture = new();
            List<bool> enabledStates = new();
            fixture.ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.MainControlsEnabled))
                {
                    enabledStates.Add(fixture.ViewModel.MainControlsEnabled);
                }
            };

            await coordinator.ChangeVersionImageAsync(
                fixture.ViewModel,
                fixture.Owner,
                viewModel,
                CancellationToken.None);

            enabledStates.Should().Equal(false, true);
            fixture.ViewModel.MainControlsEnabled.Should().BeTrue();
            await imageFileService.Received(1).ReplaceImageAsync(
                Arg.Is<ModificationImageReplacementRequest>(request =>
                    request != null &&
                    request.ModificationName == "Shockwave" &&
                    request.ImageBaseName == "1.0" &&
                    request.SourceImagePath == @"C:\Pictures\custom.png"),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void AddRepositoryModificationAsyncWhenSelectionIsReturnedAddsModificationAndRestoresFocus()
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
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            WorkflowFixture fixture = new(catalog);

            await coordinator.AddRepositoryModificationAsync(
                fixture.ViewModel,
                fixture.Content,
                fixture.Owner);

            fixture.ViewModel.ModsListSource.Should().ContainSingle()
                .Which.ContainerModification.Should().BeSameAs(catalog.Data.Modifications.Single());
            fixture.ViewModel.MainControlsEnabled.Should().BeTrue();
        });
    }

    [Fact]
    public void AddRepositoryModificationAsyncWhenPackageActivityIsActiveShowsInfoAndRestoresFocus()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPackageActivityService packageActivityService = new();
            packageActivityService.TryBegin(
                    "Download",
                    out LauncherPackageActivityService.LauncherPackageActivityLease? lease)
                .Should()
                .BeTrue();
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                packageActivityService: packageActivityService,
                dialogService: dialogService);
            WorkflowFixture fixture = new(packageActivityService: packageActivityService);

            try
            {
                await coordinator.AddRepositoryModificationAsync(
                    fixture.ViewModel,
                    fixture.Content,
                    fixture.Owner);

                fixture.ViewModel.ModsListSource.Should().BeEmpty();
                fixture.ViewModel.MainControlsEnabled.Should().BeTrue();
                await dialogService.Received(1).ShowInfoAsync(
                    Arg.Is<LauncherInfoDialogRequest>(request =>
                        request != null &&
                        request.MainMessage == "Package activity" &&
                        request.DetailMessage == "Package activity details"),
                    fixture.Owner);
            }
            finally
            {
                lease?.Dispose();
            }
        });
    }

    [Fact]
    public void ImportManualContentAsyncWhenImportSucceedsDisablesUiAddsResultAndEnablesUi()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherManualImportResult importResult = new(
                ModificationType.Mod,
                CreateModification("Manual Mod", ModificationType.Mod, CreateVersion("Manual Mod", ContentSourceKind.Manual)));
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                manualImportResult: importResult);
            WorkflowFixture fixture = new();
            List<bool> enabledStates = new();
            fixture.ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.MainControlsEnabled))
                {
                    enabledStates.Add(fixture.ViewModel.MainControlsEnabled);
                }
            };

            await coordinator.ImportManualContentAsync(
                fixture.ViewModel,
                fixture.Content,
                fixture.Owner,
                ModificationType.Mod,
                CancellationToken.None);

            enabledStates.Should().Equal(false, true);
            fixture.ViewModel.MainControlsEnabled.Should().BeTrue();
            LauncherContent addedModification = fixture.ViewModel.ModsListSource.Should().ContainSingle()
                .Which.ContainerModification;
            addedModification.Name.Should().Be("Manual Mod");
            addedModification.ModificationType.Should().Be(ModificationType.Mod);
            addedModification.Versions.Should().ContainSingle()
                .Which.Version.Should().Be("1.0");
        });
    }

    [Fact]
    public void ImportManualContentAsyncWhenContentTypeIsNotImportableThrows()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator();
            WorkflowFixture fixture = new();

            Func<Task> act = () => coordinator.ImportManualContentAsync(
                fixture.ViewModel,
                fixture.Content,
                fixture.Owner,
                ModificationType.Advertising,
                CancellationToken.None);

            await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
                .WithParameterName("kind");
            fixture.ViewModel.MainControlsEnabled.Should().BeTrue();
        });
    }

    [Fact]
    public void AddRepositoryModificationAsyncWhenSelectionIsCanceledRestoresFocusWithoutAdding()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowModificationSelectionAsync(
                    Arg.Any<IReadOnlyList<string>>(),
                    Arg.Any<Window?>())
                .Returns((string?)null);
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(dialogService: dialogService);
            WorkflowFixture fixture = new();

            await coordinator.AddRepositoryModificationAsync(
                fixture.ViewModel,
                fixture.Content,
                fixture.Owner);

            fixture.ViewModel.ModsListSource.Should().BeEmpty();
            fixture.Catalog.DownloadRequests.Should().BeEmpty();
        });
    }

    [Fact]
    public void ImportManualContentAsyncWhenPackageActivityIsActiveShowsInfoWithoutChangingUi()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPackageActivityService packageActivityService = new();
            packageActivityService.TryBegin(
                    "Download",
                    out LauncherPackageActivityService.LauncherPackageActivityLease? lease)
                .Should()
                .BeTrue();
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                packageActivityService: packageActivityService,
                dialogService: dialogService);
            WorkflowFixture fixture = new(packageActivityService: packageActivityService);
            List<bool> enabledStates = new();
            fixture.ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.MainControlsEnabled))
                {
                    enabledStates.Add(fixture.ViewModel.MainControlsEnabled);
                }
            };

            try
            {
                await coordinator.ImportManualContentAsync(
                    fixture.ViewModel,
                    fixture.Content,
                    fixture.Owner,
                    ModificationType.Mod,
                    CancellationToken.None);

                enabledStates.Should().BeEmpty();
                fixture.ViewModel.ModsListSource.Should().BeEmpty();
                fixture.ViewModel.MainControlsEnabled.Should().BeTrue();
                await dialogService.Received(1).ShowInfoAsync(
                    Arg.Is<LauncherInfoDialogRequest>(request =>
                        request != null &&
                        request.MainMessage == "Package activity" &&
                        request.DetailMessage == "Package activity details"),
                    fixture.Owner);
            }
            finally
            {
                lease?.Dispose();
            }
        });
    }

    [Fact]
    public void ConfirmCloseDuringActiveOperationsWhenNoActivityIsActiveAllowsCloseWithoutDialog()
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
    public void ConfirmCloseDuringActiveOperationsWhenPackageActivityIsActiveUsesWarningConfirmation()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPackageActivityService packageActivityService = new();
            packageActivityService.TryBegin(
                    "Shockwave",
                    out LauncherPackageActivityService.LauncherPackageActivityLease? lease)
                .Should()
                .BeTrue();
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowWarningConfirmationAsync(
                    Arg.Any<LauncherInfoDialogRequest>(),
                    Arg.Any<string?>(),
                    Arg.Any<Window?>())
                .Returns(false);
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                packageActivityService: packageActivityService,
                dialogService: dialogService);
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
    public void RestartRequest_WhenPackageActivityIsActive_IsBlockedWithoutCloseOverride()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPackageActivityService packageActivityService = new();
            packageActivityService.TryBegin(
                    "Shockwave",
                    out LauncherPackageActivityService.LauncherPackageActivityLease? lease)
                .Should()
                .BeTrue();
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherRestartCoordinator restartCoordinator = CreateRestartCoordinator(
                packageActivityService,
                dialogService);
            var owner = new Window();

            try
            {
                bool restartAccepted = await restartCoordinator.TryRequestRestartAsync(owner);

                restartAccepted.Should().BeFalse();
                restartCoordinator.IsRestartRequested.Should().BeFalse();
                await dialogService.Received(1).ShowInfoAsync(
                    Arg.Is<LauncherInfoDialogRequest>(request =>
                        request != null &&
                        request.MainMessage == "Restart unavailable" &&
                        request.DetailMessage == "Finish Shockwave before restarting."),
                    owner);
                await dialogService.DidNotReceive().ShowWarningConfirmationAsync(
                    Arg.Any<LauncherInfoDialogRequest>(),
                    Arg.Any<string?>(),
                    Arg.Any<Window?>());
            }
            finally
            {
                lease?.Dispose();
            }
        });
    }

    [Fact]
    public void RestartRequest_WhenNoOperationIsActive_IsRecorded()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherRestartCoordinator restartCoordinator = CreateRestartCoordinator(
                new LauncherPackageActivityService(),
                Substitute.For<ILauncherDialogService>());

            bool restartAccepted = await restartCoordinator.TryRequestRestartAsync(new Window());

            restartAccepted.Should().BeTrue();
            restartCoordinator.IsRestartRequested.Should().BeTrue();
        });
    }

    [Fact]
    public void ChangeVersionImageAsyncForNonManualContentDoesNothing()
    {
        StaTestRunner.Run(async () =>
        {
            IModificationImageFileService imageFileService = Substitute.For<IModificationImageFileService>();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                filePicker: new StubLauncherFilePicker(pickedImageFile: @"C:\Pictures\custom.png"),
                modificationImageFileService: imageFileService);
            ModificationViewModel viewModel = CreateTile(CreateModification(
                "Shockwave",
                ModificationType.Mod,
                CreateVersion("Shockwave", ContentSourceKind.ManagedSingleFile)));
            WorkflowFixture fixture = new();
            List<bool> enabledStates = new();
            fixture.ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.MainControlsEnabled))
                {
                    enabledStates.Add(fixture.ViewModel.MainControlsEnabled);
                }
            };

            await coordinator.ChangeVersionImageAsync(
                fixture.ViewModel,
                fixture.Owner,
                viewModel,
                CancellationToken.None);

            enabledStates.Should().BeEmpty();
            await imageFileService.DidNotReceive().ReplaceImageAsync(
                Arg.Any<ModificationImageReplacementRequest>(),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void DeleteVersionForModRemovesContentCardAndRefreshesTabs()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = CreateConfirmingDialogService();
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                dialogService: dialogService,
                catalog: catalog);
            ModificationViewModel viewModel = CreateTile(CreateModification(
                "Shockwave",
                ModificationType.Mod,
                CreateVersion("Shockwave", ContentSourceKind.Manual)));
            ModificationVersionSelection versionSelection = new(
                viewModel.LatestVersion,
                viewModel.LatestVersion.Version,
                viewModel);
            WorkflowFixture fixture = new(catalog);
            fixture.AddTile(viewModel);

            await coordinator.DeleteVersionAsync(
                fixture.ViewModel,
                fixture.Content,
                fixture.Owner,
                versionSelection);

            catalog.DiscardedContents.Should().ContainSingle().Which.Should().Match<LauncherContentKey>(
                contentKey =>
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
    public void DeleteVersionForRemoteChildContentRefreshesTileAndLabels()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = CreateConfirmingDialogService();
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                dialogService: dialogService,
                catalog: catalog);
            ModificationViewModel viewModel = CreateTile(CreateModification(
                "HD",
                ModificationType.Addon,
                CreateVersion("HD", ContentSourceKind.ManagedSingleFile)));
            ModificationVersionSelection versionSelection = new(
                viewModel.LatestVersion,
                viewModel.LatestVersion.Version,
                viewModel);
            WorkflowFixture fixture = new(catalog);
            fixture.AddTile(viewModel);

            await coordinator.DeleteVersionAsync(
                fixture.ViewModel,
                fixture.Content,
                fixture.Owner,
                versionSelection);

            catalog.UninstalledVersions.Should().ContainSingle().Which.Should().Match<LauncherContentKey>(
                contentKey =>
                    contentKey.Name == "HD" &&
                    contentKey.Version == "1.0" &&
                    contentKey.ContentType == ModificationType.Addon);
            catalog.LocalDataUpdateCount.Should().Be(1);
            catalog.SaveCount.Should().Be(1);
            fixture.ViewModel.AddonsListSource.Should().ContainSingle()
                .Which.Should().BeSameAs(viewModel);
        });
    }

    [Theory]
    [InlineData(ModificationType.Addon)]
    [InlineData(ModificationType.Patch)]
    public void DeleteVersionForManualChildContentRemovesContentCardAndRefreshesTabs(
        ModificationType modificationType)
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = CreateConfirmingDialogService();
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                dialogService: dialogService,
                catalog: catalog);
            ModificationViewModel viewModel = CreateTile(CreateModification(
                "Manual Child",
                modificationType,
                CreateVersion("Manual Child", ContentSourceKind.Manual)));
            ModificationVersionSelection versionSelection = new(
                viewModel.LatestVersion,
                viewModel.LatestVersion.Version,
                viewModel);
            WorkflowFixture fixture = new(catalog);
            fixture.AddTile(viewModel);

            await coordinator.DeleteVersionAsync(
                fixture.ViewModel,
                fixture.Content,
                fixture.Owner,
                versionSelection);

            catalog.DiscardedContents.Should().ContainSingle().Which.Should().Match<LauncherContentKey>(
                contentKey =>
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
    public void DeleteVersionWhenRemovalIsDeclinedPreservesInstalledContent()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                dialogService: dialogService,
                catalog: catalog);
            ModificationViewModel viewModel = CreateTile(CreateModification(
                "Shockwave",
                ModificationType.Mod,
                CreateVersion("Shockwave", ContentSourceKind.Manual)));
            ModificationVersionSelection versionSelection = new(
                viewModel.LatestVersion,
                viewModel.LatestVersion.Version,
                viewModel);
            WorkflowFixture fixture = new(catalog);
            fixture.AddTile(viewModel);

            await coordinator.DeleteVersionAsync(
                fixture.ViewModel,
                fixture.Content,
                fixture.Owner,
                versionSelection);

            catalog.DiscardedContents.Should().BeEmpty();
            catalog.UninstalledVersions.Should().BeEmpty();
            catalog.SaveCount.Should().Be(0);
            fixture.ViewModel.ModsListSource.Should().ContainSingle()
                .Which.Should().BeSameAs(viewModel);
        });
    }

    [Fact]
    public void LaunchAsyncForGameWhenExecutableIsUnavailableShowsErrorAndDoesNotLaunch()
    {
        StaTestRunner.Run(async () =>
        {
            IGameExecutableDiscoveryService executableDiscovery = Substitute.For<IGameExecutableDiscoveryService>();
            executableDiscovery.IsExecutableAvailable(Arg.Any<string?>()).Returns(false);
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                dialogService: dialogService,
                executableDiscovery: executableDiscovery);
            WorkflowFixture fixture = new();
            fixture.ViewModel.SelectedGameClientOption = new ExecutableOption(
                "Generals",
                "generals.exe",
                isAvailable: false,
                isBuiltIn: true);

            await coordinator.LaunchAsync(
                GameLaunchTargetKind.GameClient,
                fixture.ViewModel,
                fixture.Content,
                fixture.Owner,
                CancellationToken.None);

            await dialogService.Received(1).ShowErrorAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request != null &&
                    request.MainMessage == "Launch aborted" &&
                    request.DetailMessage == "Executable unavailable"),
                fixture.Owner);
            fixture.ViewModel.MainControlsEnabled.Should().BeTrue();
        });
    }

    [Fact]
    public void LaunchAsyncForGameWhenProcessSucceedsEnablesSelectedModificationSupport()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherContentVersion selectedVersion = CreateVersion("Shockwave", ContentSourceKind.Manual);
            FakeLauncherContentCatalog catalog = CreateCatalog();
            catalog.Data.AddOrUpdate(selectedVersion);
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(catalog: catalog);
            WorkflowFixture fixture = new(catalog);
            ModificationViewModel selectedTile = CreateTile(catalog.Data.GetSelectedMod()!);
            fixture.AddTile(selectedTile);
            selectedTile.IsSelected = true;
            fixture.ViewModel.SelectedGameClientOption = new ExecutableOption(
                "Generals",
                "generals.exe",
                isAvailable: true,
                isBuiltIn: true);

            await coordinator.LaunchAsync(
                GameLaunchTargetKind.GameClient,
                fixture.ViewModel,
                fixture.Content,
                fixture.Owner,
                CancellationToken.None);

            selectedTile.SupportButtonBlinking.Should().BeTrue();
            fixture.ViewModel.MainControlsEnabled.Should().BeTrue();
        });
    }

    [Fact]
    public void LaunchAsyncForWorldBuilderRunsSelectedWorkflowThroughProcessBoundary()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPreferences persistedPreferences = new()
            {
                Games = new LauncherGamePreferencesSet
                {
                    ZeroHour = new LauncherGamePreferences
                    {
                        WorldBuilderArguments = "-map \"Tournament Desert\"",
                    },
                },
            };
            ILauncherPreferencesService preferencesService = Substitute.For<ILauncherPreferencesService>();
            preferencesService.Current.Returns(_ => persistedPreferences);
            preferencesService.Update(
                Arg.Do<LauncherPreferences>(updatedPreferences => persistedPreferences = updatedPreferences));

            LauncherContentVersion selectedVersion = CreateVersion("Shockwave", ContentSourceKind.Manual);
            FakeLauncherContentCatalog catalog = CreateCatalog();
            catalog.Data.AddOrUpdate(selectedVersion);

            LaunchPreparationRequest? preparationRequest = null;
            ILaunchPreparationService preparationService = Substitute.For<ILaunchPreparationService>();
            preparationService.Prepare(
                    Arg.Any<LaunchPreparationRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    preparationRequest = call.ArgAt<LaunchPreparationRequest>(0);
                    return true;
                });
            preparationService.Cleanup(
                    Arg.Any<LauncherPaths>(),
                    Arg.Any<CancellationToken>())
                .Returns(true);

            GameLaunchRequest? processRequest = null;
            IGameProcessLauncher processLauncher = Substitute.For<IGameProcessLauncher>();
            processLauncher.StartAsync(
                    Arg.Any<GameLaunchRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    processRequest = call.ArgAt<GameLaunchRequest>(0);
                    return Task.FromResult(CreateProcessOperation(
                        succeeded: true,
                        processRequest.ExecutableName));
                });

            LauncherPackageActivityService packageActivityService = new();
            ILauncherDialogService launchDialogService = Substitute.For<ILauncherDialogService>();
            LauncherLaunchCoordinator launchCoordinator = CreateLaunchCoordinator(
                packageActivityService,
                launchDialogService,
                catalog,
                preferencesService,
                preparationService,
                processLauncher);
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                packageActivityService: packageActivityService,
                catalog: catalog,
                preferencesService: preferencesService,
                launchPreparationService: preparationService,
                gameProcessLauncher: processLauncher,
                launchCoordinator: launchCoordinator);
            WorkflowFixture fixture = new(
                catalog,
                packageActivityService,
                preferencesService,
                launchCoordinator);
            ModificationViewModel selectedTile = CreateTile(catalog.Data.GetSelectedMod()!);
            fixture.AddTile(selectedTile);
            selectedTile.IsSelected = true;
            fixture.ViewModel.SelectedWorldBuilderOption = new ExecutableOption(
                "World Builder",
                "worldbuilderzh.exe",
                isAvailable: true,
                isBuiltIn: true);
            List<bool> enabledStates = new();
            fixture.ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.MainControlsEnabled))
                {
                    enabledStates.Add(fixture.ViewModel.MainControlsEnabled);
                }
            };

            await coordinator.LaunchAsync(
                GameLaunchTargetKind.WorldBuilder,
                fixture.ViewModel,
                fixture.Content,
                fixture.Owner,
                CancellationToken.None);

            persistedPreferences.Games.ZeroHour.SelectedWorldBuilder.Should().Be("worldbuilderzh.exe");
            preparationRequest.Should().NotBeNull();
            preparationRequest!.Versions.Should().ContainSingle().Which.Should().BeSameAs(selectedVersion);
            preparationRequest.DisableBaseGameScriptFiles.Should().BeTrue();
            processRequest.Should().BeEquivalentTo(GameLaunchRequest.ForWorldBuilder(
                TestLauncherPaths.Create().GameDirectory,
                "worldbuilderzh.exe",
                "-map \"Tournament Desert\""));
            enabledStates.Should().Equal(false, true);
            fixture.ViewModel.MainControlsEnabled.Should().BeTrue();
            fixture.ViewModel.IsRunningProcessOverlayVisible.Should().BeFalse();
            fixture.ViewModel.RunningProcessStatusText.Should().BeEmpty();
            preparationService.Received(1).Cleanup(
                Arg.Any<LauncherPaths>(),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void UpdateModificationAsyncForActiveModDownloadPausesAndResumesLifecycle()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowWarningConfirmationAsync(
                    Arg.Any<LauncherInfoDialogRequest>(),
                    Arg.Any<string?>(),
                    Arg.Any<Window?>())
                .Returns(true);
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherPackageActivityService packageActivityService = new();
            ControlledPackageDownloadService downloadService = new();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                packageActivityService: packageActivityService,
                dialogService: dialogService,
                catalog: catalog,
                packageDownloadService: downloadService);
            ModificationViewModel viewModel = CreateTile(CreateModification(
                "Shockwave",
                ModificationType.Mod,
                CreateVersion("Shockwave", ContentSourceKind.ManagedSingleFile)),
                packageActivityService);
            WorkflowFixture fixture = new(catalog, packageActivityService);
            fixture.AddTile(viewModel);

            Task download = coordinator.UpdateModificationAsync(
                fixture.ViewModel,
                fixture.Content,
                fixture.Owner,
                viewModel);
            await downloadService.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await coordinator.UpdateModificationAsync(
                fixture.ViewModel,
                fixture.Content,
                fixture.Owner,
                viewModel);
            viewModel.UpdateButtonContent.Should().Be("Resume");
            downloadService.CancellationObserved.Task.IsCompleted.Should().BeFalse();

            await coordinator.UpdateModificationAsync(
                fixture.ViewModel,
                fixture.Content,
                fixture.Owner,
                viewModel);
            viewModel.UpdateButtonContent.Should().Be("Pause");
            downloadService.CancellationObserved.Task.IsCompleted.Should().BeFalse();

            Task cancellation = coordinator.DeleteModificationAsync(
                fixture.ViewModel,
                fixture.Content,
                fixture.Owner,
                viewModel);
            await downloadService.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            downloadService.Release();
            await Task.WhenAll(download, cancellation);
        });
    }

    [Fact]
    public void DeleteModificationAsyncForActiveModDownloadCancelsLifecycleAndRemovesPartialContent()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowWarningConfirmationAsync(
                    Arg.Any<LauncherInfoDialogRequest>(),
                    Arg.Any<string?>(),
                    Arg.Any<Window?>())
                .Returns(true);
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherPackageActivityService packageActivityService = new();
            ControlledPackageDownloadService downloadService = new();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                packageActivityService: packageActivityService,
                dialogService: dialogService,
                catalog: catalog,
                packageDownloadService: downloadService);
            ModificationViewModel viewModel = CreateTile(CreateModification(
                "Shockwave",
                ModificationType.Mod,
                CreateVersion("Shockwave", ContentSourceKind.ManagedSingleFile)),
                packageActivityService);
            WorkflowFixture fixture = new(catalog, packageActivityService);
            fixture.AddTile(viewModel);

            Task download = coordinator.UpdateModificationAsync(
                fixture.ViewModel,
                fixture.Content,
                fixture.Owner,
                viewModel);
            await downloadService.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task cancellation = coordinator.DeleteModificationAsync(
                fixture.ViewModel,
                fixture.Content,
                fixture.Owner,
                viewModel);
            await downloadService.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            downloadService.Release();
            await Task.WhenAll(download, cancellation);

            catalog.UninstalledVersions.Should().ContainSingle().Which.Should().Match<LauncherContentKey>(
                contentKey =>
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

    [Fact]
    public void DeleteModificationAsyncForUninstalledModRemovesCardImmediately()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = CreateConfirmingDialogService();
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherPackageActivityService packageActivityService = new();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                packageActivityService: packageActivityService,
                dialogService: dialogService,
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

            await coordinator.DeleteModificationAsync(
                fixture.ViewModel,
                fixture.Content,
                fixture.Owner,
                viewModel);

            catalog.DiscardedVersions.Should().ContainSingle().Which.Should().Match<LauncherContentKey>(
                removed =>
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
    public void DeleteModificationAsyncForUninstalledModWhenRemovalIsDeclinedPreservesCard()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherPackageActivityService packageActivityService = new();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                packageActivityService: packageActivityService,
                dialogService: dialogService,
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

            await coordinator.DeleteModificationAsync(
                fixture.ViewModel,
                fixture.Content,
                fixture.Owner,
                viewModel);

            catalog.DiscardedVersions.Should().BeEmpty();
            catalog.SaveCount.Should().Be(0);
            fixture.ViewModel.ModsListSource.Should().ContainSingle()
                .Which.Should().BeSameAs(viewModel);
        });
    }

    [Fact]
    public void PrepareForCloseAsyncCancelsAndAwaitsLifecycleOwnedCleanup()
    {
        StaTestRunner.Run(async () =>
        {
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherPackageActivityService packageActivityService = new();
            ControlledPackageDownloadService downloadService = new();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                packageActivityService: packageActivityService,
                catalog: catalog,
                packageDownloadService: downloadService);
            ModificationViewModel viewModel = CreateTile(CreateModification(
                "Shockwave",
                ModificationType.Mod,
                CreateVersion("Shockwave", ContentSourceKind.ManagedSingleFile)),
                packageActivityService);
            WorkflowFixture fixture = new(catalog, packageActivityService);
            fixture.AddTile(viewModel);

            Task download = coordinator.UpdateModificationAsync(
                fixture.ViewModel,
                fixture.Content,
                fixture.Owner,
                viewModel);
            await downloadService.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task closePreparation = coordinator.PrepareForCloseAsync();
            await downloadService.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

            closePreparation.IsCompleted.Should().BeFalse();
            fixture.ViewModel.ModsListSource.Should().ContainSingle();

            downloadService.Release();
            await Task.WhenAll(download, closePreparation);

            catalog.UninstalledVersions.Should().ContainSingle().Which.Should().Match<LauncherContentKey>(
                contentKey =>
                    contentKey.Name == "Shockwave" &&
                    contentKey.Version == "1.0" &&
                    contentKey.ContentType == ModificationType.Mod);
            catalog.DiscardedVersions.Should().BeEmpty();
            fixture.ViewModel.ModsListSource.Should().ContainSingle()
                .Which.Should().BeSameAs(viewModel);
        });
    }

    [Fact]
    public void PrepareForCloseAsyncAndButtonCancellationRaceRunsRegisteredCleanupOnce()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            dialogService.ShowWarningConfirmationAsync(
                    Arg.Any<LauncherInfoDialogRequest>(),
                    Arg.Any<string?>(),
                    Arg.Any<Window?>())
                .Returns(true);
            FakeLauncherContentCatalog catalog = CreateCatalog();
            LauncherPackageActivityService packageActivityService = new();
            ControlledPackageDownloadService downloadService = new();
            LauncherWindowWorkflowCoordinator coordinator = CreateCoordinator(
                packageActivityService: packageActivityService,
                dialogService: dialogService,
                catalog: catalog,
                packageDownloadService: downloadService);
            ModificationViewModel viewModel = CreateTile(CreateModification(
                "Shockwave",
                ModificationType.Mod,
                CreateVersion("Shockwave", ContentSourceKind.ManagedSingleFile)),
                packageActivityService);
            WorkflowFixture fixture = new(catalog, packageActivityService);
            fixture.AddTile(viewModel);

            Task download = coordinator.UpdateModificationAsync(
                fixture.ViewModel,
                fixture.Content,
                fixture.Owner,
                viewModel);
            await downloadService.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task buttonCancellation = coordinator.DeleteModificationAsync(
                fixture.ViewModel,
                fixture.Content,
                fixture.Owner,
                viewModel);
            Task closePreparation = coordinator.PrepareForCloseAsync();
            await downloadService.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

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

    private static LauncherWindowWorkflowCoordinator CreateCoordinator()
    {
        return CreateCoordinator(out _);
    }

    private static ILauncherDialogService CreateConfirmingDialogService()
    {
        ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
        dialogService.ShowWarningConfirmationAsync(
                Arg.Any<LauncherInfoDialogRequest>(),
                Arg.Any<string?>(),
                Arg.Any<Window?>())
            .Returns(true);
        return dialogService;
    }

    private static LauncherWindowWorkflowCoordinator CreateCoordinator(
        LauncherPackageActivityService? packageActivityService = null,
        ILauncherDialogService? dialogService = null,
        LauncherManualImportResult? manualImportResult = null,
        ILauncherFilePicker? filePicker = null,
        IModificationImageFileService? modificationImageFileService = null,
        FakeLauncherContentCatalog? catalog = null,
        IGameExecutableDiscoveryService? executableDiscovery = null,
        ILauncherPreferencesService? preferencesService = null,
        ILaunchPreparationService? launchPreparationService = null,
        IGameProcessLauncher? gameProcessLauncher = null,
        LauncherLaunchCoordinator? launchCoordinator = null,
        IPackageDownloadService? packageDownloadService = null)
    {
        return CreateCoordinator(
            out _,
            packageActivityService,
            dialogService,
            manualImportResult,
            filePicker,
            modificationImageFileService,
            catalog,
            executableDiscovery,
            preferencesService,
            launchPreparationService,
            gameProcessLauncher,
            launchCoordinator,
            packageDownloadService);
    }

    private static LauncherWindowWorkflowCoordinator CreateCoordinator(
        out ILauncherShellService shellService,
        LauncherPackageActivityService? packageActivityService = null,
        ILauncherDialogService? dialogService = null,
        LauncherManualImportResult? manualImportResult = null,
        ILauncherFilePicker? filePicker = null,
        IModificationImageFileService? modificationImageFileService = null,
        FakeLauncherContentCatalog? catalog = null,
        IGameExecutableDiscoveryService? executableDiscovery = null,
        ILauncherPreferencesService? preferencesService = null,
        ILaunchPreparationService? launchPreparationService = null,
        IGameProcessLauncher? gameProcessLauncher = null,
        LauncherLaunchCoordinator? launchCoordinator = null,
        IPackageDownloadService? packageDownloadService = null)
    {
        LauncherPackageActivityService resolvedPackageActivityService = packageActivityService ?? new();
        ILauncherDialogService resolvedDialogService = dialogService ?? Substitute.For<ILauncherDialogService>();
        FakeLauncherContentCatalog resolvedCatalog = catalog ?? CreateCatalog();
        ILauncherShellService resolvedShellService = Substitute.For<ILauncherShellService>();
        shellService = resolvedShellService;
        LauncherLaunchCoordinator resolvedLaunchCoordinator = launchCoordinator ??
            CreateLaunchCoordinator(
                resolvedPackageActivityService,
                resolvedDialogService,
                resolvedCatalog,
                preferencesService,
                launchPreparationService,
                gameProcessLauncher);
        TestStringLocalizer stringLocalizer = CreateStringLocalizer();
        LauncherCloseGuard closeGuard = new(
            resolvedLaunchCoordinator,
            resolvedPackageActivityService,
            resolvedDialogService,
            stringLocalizer,
            NullLogger<LauncherCloseGuard>.Instance);
        LauncherRestartCoordinator restartCoordinator = new(
            closeGuard,
            NullLogger<LauncherRestartCoordinator>.Instance);
        LauncherRuntimeContext runtimeContext = new(
            TestLauncherPaths.CreateRuntimePathContext(TestLauncherPaths.Create()),
            "1.0")
        {
            Colors = TestLauncherTheme.Create()
        };

        return new LauncherWindowWorkflowCoordinator(
            resolvedLaunchCoordinator,
            CreateLaunchReadinessCoordinator(resolvedDialogService, resolvedCatalog, executableDiscovery),
            new LauncherTileActionService(resolvedCatalog),
            runtimeContext,
            CreateGameSessionCoordinator(
                resolvedLaunchCoordinator,
                resolvedCatalog,
                resolvedPackageActivityService,
                resolvedDialogService,
                runtimeContext),
            CreateManualImportCoordinator(manualImportResult, resolvedCatalog),
            CreateDownloadCoordinator(
                resolvedPackageActivityService,
                resolvedDialogService,
                resolvedCatalog,
                packageDownloadService),
            resolvedPackageActivityService,
            resolvedShellService,
            filePicker ?? new StubLauncherFilePicker(),
            CreateIntegrityCoordinator(resolvedPackageActivityService, resolvedDialogService, resolvedCatalog),
            stringLocalizer,
            modificationImageFileService ?? Substitute.For<IModificationImageFileService>(),
            () => null!,
            resolvedDialogService,
            closeGuard,
            restartCoordinator);
    }

    private static LauncherLaunchCoordinator CreateLaunchCoordinator(
        LauncherPackageActivityService packageActivityService,
        ILauncherDialogService dialogService,
        FakeLauncherContentCatalog? catalog = null,
        ILauncherPreferencesService? preferencesService = null,
        ILaunchPreparationService? preparationService = null,
        IGameProcessLauncher? processLauncher = null)
    {
        ILaunchPreparationService resolvedPreparationService =
            preparationService ?? Substitute.For<ILaunchPreparationService>();
        if (preparationService == null)
        {
            resolvedPreparationService.Prepare(Arg.Any<LaunchPreparationRequest>(), Arg.Any<CancellationToken>())
                .Returns(true);
            resolvedPreparationService.Cleanup(Arg.Any<LauncherPaths>(), Arg.Any<CancellationToken>())
                .Returns(true);
        }

        IGameProcessLauncher resolvedProcessLauncher = processLauncher ?? Substitute.For<IGameProcessLauncher>();
        if (processLauncher == null)
        {
            resolvedProcessLauncher.StartAsync(Arg.Any<GameLaunchRequest>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(CreateProcessOperation(
                    succeeded: true,
                    "generals.exe")));
        }

        ILauncherPreferencesService resolvedPreferencesService =
            preferencesService ?? Substitute.For<ILauncherPreferencesService>();
        if (preferencesService == null)
        {
            resolvedPreferencesService.Current.Returns(new LauncherPreferences());
        }
        LauncherRuntimePathContext runtimePaths =
            TestLauncherPaths.CreateRuntimePathContext(TestLauncherPaths.Create());

        return new LauncherLaunchCoordinator(
            resolvedPreferencesService,
            resolvedPreparationService,
            resolvedProcessLauncher,
            CreateIntegrityCoordinator(
                packageActivityService,
                dialogService,
                catalog ?? CreateCatalog(),
                runtimePaths),
            packageActivityService,
            runtimePaths,
            CreateStringLocalizer(),
            dialogService,
            NullLogger<LauncherLaunchCoordinator>.Instance);
    }

    private static LauncherRestartCoordinator CreateRestartCoordinator(
        LauncherPackageActivityService packageActivityService,
        ILauncherDialogService dialogService)
    {
        LauncherCloseGuard closeGuard = new(
            CreateLaunchCoordinator(packageActivityService, dialogService),
            packageActivityService,
            dialogService,
            CreateStringLocalizer(),
            NullLogger<LauncherCloseGuard>.Instance);
        return new LauncherRestartCoordinator(
            closeGuard,
            NullLogger<LauncherRestartCoordinator>.Instance);
    }

    private static LauncherLaunchReadinessCoordinator CreateLaunchReadinessCoordinator(
        ILauncherDialogService dialogService,
        FakeLauncherContentCatalog catalog,
        IGameExecutableDiscoveryService? executableDiscovery = null)
    {
        IGameExecutableDiscoveryService resolvedExecutableDiscovery =
            executableDiscovery ?? Substitute.For<IGameExecutableDiscoveryService>();
        if (executableDiscovery == null)
        {
            resolvedExecutableDiscovery.IsExecutableAvailable(Arg.Any<string?>()).Returns(true);
        }

        return new LauncherLaunchReadinessCoordinator(
            resolvedExecutableDiscovery,
            dialogService,
            CreateStringLocalizer());
    }

    private static IGameProcessLaunchOperation CreateProcessOperation(
        bool succeeded,
        string executableName)
    {
        return new TestGameProcessLaunchOperation(succeeded, executableName);
    }

    private static LauncherManualImportCoordinator CreateManualImportCoordinator(
        LauncherManualImportResult? manualImportResult,
        FakeLauncherContentCatalog catalog)
    {
        ILauncherFilePicker filePicker = new StubLauncherFilePicker(manualImportResult == null
            ? Array.Empty<string>()
            : new[] { @"C:\Downloads\manual.zip" });
        ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
        if (manualImportResult != null)
        {
            LauncherContentVersion version = manualImportResult.Modification.Versions[0];
            dialogService.ShowManualModificationImportAsync(
                    Arg.Any<ManualModificationDialogRequest>(),
                    Arg.Any<Window?>())
                .Returns(new ManualModificationDialogResult(
                    new[] { @"C:\Downloads\manual.zip" },
                    null,
                    manualImportResult.Modification.Name,
                    version.Version));
        }

        if (manualImportResult != null)
        {
            catalog.Data.AddOrUpdate(manualImportResult.Modification.Versions[0]);
        }

        return new LauncherManualImportCoordinator(
            filePicker,
            dialogService,
            catalog,
            TestLauncherPaths.CreateRuntimePathContext(TestLauncherPaths.Create()),
            Substitute.For<IManualModificationImporter>(),
            CreateIntegrityCoordinator(new LauncherPackageActivityService(), dialogService),
            NullLogger<LauncherManualImportCoordinator>.Instance);
    }

    private static LauncherModificationDownloadCoordinator CreateDownloadCoordinator(
        LauncherPackageActivityService packageActivityService,
        ILauncherDialogService dialogService,
        FakeLauncherContentCatalog catalog,
        IPackageDownloadService? packageDownloadService = null)
    {
        ILauncherPreferencesService preferencesService = Substitute.For<ILauncherPreferencesService>();
        preferencesService.Current.Returns(new LauncherPreferences());
        return new LauncherModificationDownloadCoordinator(
            preferencesService,
            catalog,
            packageDownloadService ?? Substitute.For<IPackageDownloadService>(),
            CreateIntegrityCoordinator(packageActivityService, dialogService),
            packageActivityService,
            dialogService,
            CreateStringLocalizer(),
            NullLogger<LauncherModificationDownloadCoordinator>.Instance);
    }

    private static LaunchContentIntegrityCoordinator CreateIntegrityCoordinator(
        LauncherPackageActivityService packageActivityService,
        ILauncherDialogService dialogService,
        FakeLauncherContentCatalog? catalog = null,
        LauncherRuntimePathContext? runtimePaths = null)
    {
        ILaunchContentIntegrityResolutionService resolutionService =
            Substitute.For<ILaunchContentIntegrityResolutionService>();
        resolutionService.VerifyAsync(
                Arg.Any<LaunchContentIntegrityTargetRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new LaunchContentIntegrityVerificationResult(
                new ContentIntegrityReport(Array.Empty<ContentIntegrityIssue>()),
                Array.Empty<LaunchContentIntegrityTargetContext>()));
        resolutionService.CaptureManualImageSnapshotAsync(
                Arg.Any<LaunchContentIntegrityVersionRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return new LaunchContentIntegrityCoordinator(
            resolutionService,
            catalog ?? CreateCatalog(),
            runtimePaths ?? TestLauncherPaths.CreateRuntimePathContext(TestLauncherPaths.Create()),
            packageActivityService,
            CreateStringLocalizer(),
            dialogService,
            NullLogger<LaunchContentIntegrityCoordinator>.Instance);
    }

    private static MainWindowViewModel CreateMainWindowViewModel(
        FakeLauncherContentCatalog catalog,
        LauncherPackageActivityService packageActivityService,
        ILauncherPreferencesService? preferencesService = null,
        LauncherLaunchCoordinator? launchCoordinator = null)
    {
        LauncherRuntimeContext runtimeContext = new(
            TestLauncherPaths.CreateRuntimePathContext(TestLauncherPaths.Create()),
            "1.0")
        {
            Colors = TestLauncherTheme.Create()
        };
        TestStringLocalizer stringLocalizer = CreateStringLocalizer();

        ILauncherPreferencesService resolvedPreferencesService =
            preferencesService ?? Substitute.For<ILauncherPreferencesService>();
        if (preferencesService == null)
        {
            resolvedPreferencesService.Current.Returns(new LauncherPreferences());
        }

        return new MainWindowViewModel(
            resolvedPreferencesService,
            new LauncherExecutableSelectionService(
                Substitute.For<IGameExecutableDiscoveryService>(),
                runtimeContext,
                resolvedPreferencesService,
                stringLocalizer),
            catalog,
            runtimeContext,
            stringLocalizer,
            new ModificationImageSourceFactory(NullLogger<ModificationImageSourceFactory>.Instance),
            Substitute.For<IModificationImageFileService>(),
            packageActivityService,
            NullLogger<ModificationViewModel>.Instance,
            launchCoordinator ?? CreateLaunchCoordinator(
                packageActivityService,
                Substitute.For<ILauncherDialogService>(),
                catalog,
                resolvedPreferencesService),
            NullLogger<MainWindowViewModel>.Instance);
    }

    private static LauncherGameSessionCoordinator CreateGameSessionCoordinator(
        LauncherLaunchCoordinator launchCoordinator,
        FakeLauncherContentCatalog catalog,
        LauncherPackageActivityService packageActivityService,
        ILauncherDialogService dialogService,
        LauncherRuntimeContext runtimeContext)
    {
        ILauncherPreferencesService preferencesService = Substitute.For<ILauncherPreferencesService>();
        preferencesService.Current.Returns(new LauncherPreferences());

        return new LauncherGameSessionCoordinator(
            runtimeContext,
            preferencesService,
            Substitute.For<IGameInstallationService>(),
            Substitute.For<ILauncherPathResolver>(),
            Substitute.For<ILaunchPreparationService>(),
            Substitute.For<IRemoteConnectionProbe>(),
            catalog,
            packageActivityService,
            launchCoordinator,
            dialogService,
            CreateStringLocalizer(),
            NullLogger<LauncherGameSessionCoordinator>.Instance);
    }

    private static FakeLauncherContentCatalog CreateCatalog()
    {
        return new FakeLauncherContentCatalog
        {
            RepositoryModificationNames = new[] { "Shockwave" }
        };
    }

    private static ModificationViewModel CreateTile(
        LauncherContent modification,
        LauncherPackageActivityService? packageActivityService = null)
    {
        return new ModificationViewModel(
            modification,
            new ModificationImageSourceFactory(NullLogger<ModificationImageSourceFactory>.Instance),
            TestLauncherRuntimeContext.Create(colors: TestLauncherTheme.Create()),
            Substitute.For<IModificationImageFileService>(),
            CreateStringLocalizer(),
            packageActivityService ?? new LauncherPackageActivityService(),
            NullLogger<ModificationViewModel>.Instance);
    }

    private static LauncherContent CreateModification(
        string name,
        ModificationType modificationType,
        LauncherContentVersion version,
        string simpleDownloadLink = "",
        string supportLink = "",
        string newsLink = "",
        string networkInfo = "",
        string modDbLink = "",
        string discordLink = "")
    {
        var contentVersion = new LauncherContentVersion(version.Installation)
        {
            Name = name,
            Version = version.Version,
            ModificationType = modificationType,
            SimpleDownloadLink = simpleDownloadLink,
            SupportLink = supportLink,
            NewsLink = newsLink,
            NetworkInfo = networkInfo,
            ModDBLink = modDbLink,
            DiscordLink = discordLink
        };
        return new LauncherContent(contentVersion);
    }

    private static LauncherContentVersion CreateVersion(
        string name,
        ContentSourceKind sourceKind)
    {
        return new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation { Installed = true, IsSelected = true, ContentSourceKind = sourceKind },
            Name = name,
            Version = "1.0",
            ModificationType = ModificationType.Mod,
            SimpleDownloadLink = sourceKind == ContentSourceKind.ManagedSingleFile
                ? "https://example.test/package.zip"
                : string.Empty
        };
    }

    private static TestStringLocalizer CreateStringLocalizer()
    {
        return new TestStringLocalizer(new Dictionary<string, string>
        {
            ["CancelDownload"] = "Cancel download",
            ["CancelDownloadDetails"] = "Cancel {0} and delete content downloaded so far",
            ["CancelDownloadAction"] = "Cancel Download",
            ["CancelLaunch"] = "Cancel launch",
            ["Canceled"] = "Canceled",
            ["CloseAnyway"] = "Close anyway",
            ["ClosePackageActivityDetails"] = "Close {0}?",
            ["Compatibility"] = "Compatibility",
            ["Deprecated"] = "{0} is deprecated",
            ["Delete"] = "Delete",
            ["Discord"] = "Discord",
            ["Error"] = "Error: ",
            ["ExecutableUnavailable"] = "Executable unavailable",
            ["FilesCorrupted"] = "Files corrupted",
            ["FinishProcess"] = "Finish process",
            ["ForceQuitRunningProcess"] = "Force quit",
            ["ForceQuitRunningProcessConfirmationDetails"] = "Force quit {0}?",
            ["ForceQuitRunningProcessConfirmationTitle"] = "Force quit?",
            ["GameIsStillRunning"] = "Game running",
            ["GameRunning"] = "Game running",
            ["Image"] = "Image",
            ["Install"] = "Install",
            ["InstallInProgress"] = "{0} installing",
            ["IntegrityCacheSuffix"] = " cache",
            ["LaunchAborted"] = "Launch aborted",
            ["LaunchVerificationRunning"] = "Verification running",
            ["LatestVersion"] = "Latest version: ",
            ["ModDb"] = "Mod DB",
            ["ModificationsWithUpdate"] = "Updates",
            ["NoSupportedClient"] = "No client",
            ["NoWorldBuildersFound"] = "No World Builder",
            ["NotInstalled"] = "{0} missing",
            ["PackageActivityInProgress"] = "Package activity",
            ["PackageActivityInProgressDetails"] = "Package activity details",
            ["Pause"] = "Pause",
            ["Preparing"] = "Preparing",
            ["Reinstall"] = "Reinstall",
            ["RestartBlockedActiveOperation"] = "Finish {0} before restarting.",
            ["RestartBlockedTitle"] = "Restart unavailable",
            ["Resume"] = "Resume",
            ["Remove"] = "Remove",
            ["RemoveContent"] = "Remove content?",
            ["RemoveContentDetails"] = "Remove {0}",
            ["RemoveFromList"] = "Remove from list",
            ["RemoveFromListConfirmation"] = "Remove from list?",
            ["RemoveFromListDetails"] = "Remove {0} from list",
            ["RunningProcessCloseBlockedDetails"] = "Close process first",
            ["RunningProcessCloseBlockedTitle"] = "Process running",
            ["RunningProcessUnknown"] = "Unknown process",
            ["SetImage"] = "Set image",
            ["ThankYou"] = "Thank you",
            ["UninstalledUpdate"] = "{0} update missing",
            ["Update"] = "Update",
            ["UpToDate"] = "Up to date",
            ["WorldBuilderRunning"] = "World Builder running",
            ["Yes"] = "Yes",
        });
    }

    private sealed class StubLauncherFilePicker : ILauncherFilePicker
    {
        public Task<string?> PickGameInstallationFolderAsync(
            Window owner,
            string? initialDirectory)
        {
            return Task.FromResult<string?>(null);
        }

        private readonly IReadOnlyList<string> _files;
        private readonly string? _pickedImageFile;

        public StubLauncherFilePicker()
            : this(Array.Empty<string>())
        {
        }

        public StubLauncherFilePicker(
            IReadOnlyList<string>? files = null,
            string? pickedImageFile = null)
        {
            _files = files ?? Array.Empty<string>();
            _pickedImageFile = pickedImageFile;
        }

        public Task<IReadOnlyList<string>> PickManualPackageFilesAsync(Window owner)
        {
            return Task.FromResult(_files);
        }

        public Task<string?> PickModificationImageFileAsync(
            Window owner,
            string imageFilterLabel)
        {
            return Task.FromResult(_pickedImageFile);
        }

        public Task<string?> PickGameExecutableFileAsync(Window owner, string gameDirectory)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class TestGameProcessLaunchOperation : IGameProcessLaunchOperation
    {
        public TestGameProcessLaunchOperation(
            bool succeeded,
            string executableName)
        {
            CurrentExecutableName = executableName;
            Completion = Task.FromResult(succeeded);
        }

        public string CurrentExecutableName { get; }

        public event EventHandler? CurrentExecutableNameChanged
        {
            add { }
            remove { }
        }

        public Task<bool> Completion { get; }

        public void ForceClose()
        {
        }
    }

    private sealed class ControlledPackageDownloadService : IPackageDownloadService
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<PackageDownloadResult> DownloadAsync(
            LauncherContent modification,
            LauncherContentVersion version,
            IProgress<PackageUpdateProgress>? progress,
            CancellationToken cancellationToken,
            PackageDownloadPauseController? pauseController = null)
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(
                () => CancellationObserved.TrySetResult());
            Started.TrySetResult();
            await _release.Task;
            return cancellationToken.IsCancellationRequested
                ? PackageDownloadResult.Canceled()
                : PackageDownloadResult.Succeeded();
        }

        public void Release()
        {
            _release.TrySetResult();
        }
    }

    private sealed class WorkflowFixture
    {
        public WorkflowFixture(
            FakeLauncherContentCatalog? catalog = null,
            LauncherPackageActivityService? packageActivityService = null,
            ILauncherPreferencesService? preferencesService = null,
            LauncherLaunchCoordinator? launchCoordinator = null)
        {
            Catalog = catalog ?? CreateCatalog();
            LauncherPackageActivityService resolvedPackageActivityService =
                packageActivityService ?? new LauncherPackageActivityService();

            ViewModel = CreateMainWindowViewModel(
                Catalog,
                resolvedPackageActivityService,
                preferencesService,
                launchCoordinator);

            ModsList = new ListBox
            {
                Name = "ModsList",
                SelectionMode = SelectionMode.Multiple,
                ItemsSource = ViewModel.ModsListSource
            };
            PatchesList = new ListBox
            {
                Name = "PatchesList",
                SelectionMode = SelectionMode.Multiple,
                ItemsSource = ViewModel.PatchesListSource
            };
            AddonsList = new ListBox
            {
                Name = "AddonsList",
                SelectionMode = SelectionMode.Multiple,
                ItemsSource = ViewModel.AddonsListSource
            };

            Content = new LauncherWindowListController(
                Owner,
                ViewModel,
                new LauncherRuntimeContext(
                    TestLauncherPaths.CreateRuntimePathContext(TestLauncherPaths.Create()),
                    "1.0")
                {
                    Colors = TestLauncherTheme.Create()
                },
                ModsList,
                PatchesList,
                AddonsList);
        }

        public Window Owner { get; } = new();

        public MainWindowViewModel ViewModel { get; }

        public LauncherWindowListController Content { get; }

        public FakeLauncherContentCatalog Catalog { get; }

        public ListBox ModsList { get; }

        public ListBox PatchesList { get; }

        public ListBox AddonsList { get; }

        public void AddTile(ModificationViewModel modification)
        {
            switch (modification.ContainerModification.ModificationType)
            {
                case ModificationType.Mod:
                case ModificationType.Advertising:
                    ViewModel.ModsListSource.Add(modification);
                    break;
                case ModificationType.Patch:
                    ViewModel.PatchesListSource.Add(modification);
                    break;
                case ModificationType.Addon:
                    ViewModel.AddonsListSource.Add(modification);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(modification),
                        modification.ContainerModification.ModificationType,
                        "Unsupported launcher content type.");
            }
        }
    }

}
