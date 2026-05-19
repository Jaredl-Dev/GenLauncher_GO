using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Launcher.Contracts;
using GenLauncherGO.UI.Features.Launcher.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Services;

[Collection("Avalonia")]
public sealed class LauncherManualImportCoordinatorTests
{
    [Fact]
    public void ImportAsyncWhenNoFilesAreSelected_ReturnsNullWithoutShowingDialog()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherFilePicker filePicker = new StubLauncherFilePicker();
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherManualImportCoordinator coordinator = CreateCoordinator(
                filePicker,
                dialogService);

            LauncherContent? result = await coordinator.ImportAsync(
                ModificationType.Mod,
                new Window(),
                null,
                CancellationToken.None);

            result.Should().BeNull();
            await dialogService.DidNotReceive().ShowManualModificationImportAsync(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<Window?>());
        });
    }

    [Fact]
    public void ImportAsync_Modification_ImportsToVersionFolderAndRegistersSnapshot()
    {
        StaTestRunner.Run(async () =>
        {
            string[] selectedFiles = [@"C:\Downloads\shockwave.zip"];
            LauncherContentVersion savedVersion = CreateVersion("Shockwave", "1.2", ModificationType.Mod);
            FakeLauncherContentCatalog catalog = CreateCatalog(savedVersion);
            LauncherContent savedModification = catalog.Data.Modifications.Single();
            ILauncherFilePicker filePicker = new StubLauncherFilePicker
            {
                ManualPackageFilesResult = selectedFiles
            };
            ILauncherDialogService dialogService = CreateDialogService(
                new ManualModificationDialogResult("Shockwave", "1.2"));
            IManualModificationImporter importer = Substitute.For<IManualModificationImporter>();
            IReadOnlyList<string>? capturedSourceFilePaths = null;
            OwnedContentPath? capturedDestinationPath = null;
            importer
                .When(service => service.Import(
                    Arg.Do<IReadOnlyList<string>>(paths => capturedSourceFilePaths = paths),
                    Arg.Do<OwnedContentPath>(path => capturedDestinationPath = path),
                    Arg.Any<CancellationToken>()))
                .Do(_ => { });
            ILaunchContentIntegrityResolutionService resolutionService =
                Substitute.For<ILaunchContentIntegrityResolutionService>();
            LauncherManualImportCoordinator coordinator = CreateCoordinator(
                filePicker,
                dialogService,
                catalog,
                importer,
                resolutionService);

            LauncherContent? result = await coordinator.ImportAsync(
                ModificationType.Mod,
                new Window(),
                null,
                CancellationToken.None);

            result.Should().BeSameAs(savedModification);
            capturedSourceFilePaths.Should().Equal(selectedFiles);
            capturedDestinationPath.Should().NotBeNull();
            LauncherPaths paths = CreateLauncherPaths();
            capturedDestinationPath.OwnerRoot.Should().Be(paths.ModsDirectory);
            capturedDestinationPath.FullPath.Should()
                .Be(Path.Combine(paths.ModsDirectory, "Shockwave", "1.2"));
            await resolutionService.Received(1).RegisterManualImportAsync(
                Arg.Is<LaunchContentIntegrityVersionRequest>(request =>
                    request != null && request.Version == savedVersion),
                Arg.Any<CancellationToken>());
        });
    }

    /// <summary>
    ///     The import operation owns the parent identity while the dialog supplies only the editable child name and version.
    /// </summary>
    [Theory]
    [InlineData("Patch", "Patches", "Balance Patch", "Shockwave")]
    [InlineData("Patch", "Patches", "Balance Patch", "Rise of the Reds")]
    [InlineData("Addon", "Addons", "HD Addon", "Shockwave")]
    public void ImportAsyncForChildContent_UsesParentContentFolder(
        string kindName,
        string childFolderName,
        string contentName,
        string parentContentName)
    {
        StaTestRunner.Run(async () =>
        {
            ModificationType kind = Enum.Parse<ModificationType>(kindName);
            string[] selectedFiles = [@"C:\Downloads\child.zip"];
            LauncherContentVersion savedVersion = CreateVersion(
                contentName,
                "3.0",
                kind,
                parentContentName);
            FakeLauncherContentCatalog catalog = CreateCatalog(savedVersion, parentContentName);
            LauncherContent savedModification = kind == ModificationType.Patch
                ? catalog.Data.Patches.Single()
                : catalog.Data.Addons.Single();
            ILauncherDialogService dialogService = CreateDialogService(
                new ManualModificationDialogResult(contentName, "3.0"));
            IManualModificationImporter importer = Substitute.For<IManualModificationImporter>();
            OwnedContentPath? capturedDestinationPath = null;
            importer
                .When(service => service.Import(
                    Arg.Any<IReadOnlyList<string>>(),
                    Arg.Do<OwnedContentPath>(path => capturedDestinationPath = path),
                    Arg.Any<CancellationToken>()))
                .Do(_ => { });
            LauncherManualImportCoordinator coordinator = CreateCoordinator(
                new StubLauncherFilePicker
                {
                    ManualPackageFilesResult = selectedFiles
                },
                dialogService,
                catalog,
                importer);

            LauncherContent? result = await coordinator.ImportAsync(
                kind,
                new Window(),
                parentContentName,
                CancellationToken.None);

            result.Should().BeSameAs(savedModification);
            capturedDestinationPath.Should().NotBeNull();
            LauncherPaths paths = CreateLauncherPaths();
            capturedDestinationPath.FullPath.Should().Be(
                Path.Combine(
                    paths.ModsDirectory,
                    parentContentName,
                    childFolderName,
                    contentName,
                    "3.0"));
        });
    }

    [Fact]
    public void ImportAsyncForChildContentWithoutParent_UsesOriginalGameFolder()
    {
        StaTestRunner.Run(async () =>
        {
            string[] selectedFiles = [@"C:\Downloads\original-patch.zip"];
            LauncherContentVersion savedVersion = CreateVersion(
                "Community Patch",
                "1.0",
                ModificationType.Patch,
                LauncherContentKey.OriginalGame.Name);
            FakeLauncherContentCatalog catalog = CreateCatalog(savedVersion);
            ILauncherDialogService dialogService = CreateDialogService(
                new ManualModificationDialogResult("Community Patch", "1.0"));
            IManualModificationImporter importer = Substitute.For<IManualModificationImporter>();
            IReadOnlyList<string>? capturedDialogFiles = null;
            OwnedContentPath? capturedDestinationPath = null;
            dialogService.ShowManualModificationImportAsync(
                    Arg.Do<IReadOnlyList<string>>(files => capturedDialogFiles = files),
                    Arg.Any<Window?>())
                .Returns(new ManualModificationDialogResult("Community Patch", "1.0"));
            importer
                .When(service => service.Import(
                    Arg.Any<IReadOnlyList<string>>(),
                    Arg.Do<OwnedContentPath>(path => capturedDestinationPath = path),
                    Arg.Any<CancellationToken>()))
                .Do(_ => { });
            LauncherManualImportCoordinator coordinator = CreateCoordinator(
                new StubLauncherFilePicker
                {
                    ManualPackageFilesResult = selectedFiles
                },
                dialogService,
                catalog,
                importer);

            await coordinator.ImportAsync(
                ModificationType.Patch,
                new Window(),
                null,
                CancellationToken.None);

            capturedDialogFiles.Should().Equal(selectedFiles);
            capturedDestinationPath.Should().NotBeNull();
            LauncherPaths paths = CreateLauncherPaths();
            capturedDestinationPath.FullPath.Should().Be(
                Path.Combine(
                    paths.ModsDirectory,
                    LauncherContentKey.OriginalGame.Name,
                    "Patches",
                    "Community Patch",
                    "1.0"));
        });
    }

    [Theory]
    [InlineData("Mod", null, ".", "1.0")]
    [InlineData("Mod", null, "..", "1.0")]
    [InlineData("Mod", null, "Shockwave", ".")]
    [InlineData("Mod", null, "Shockwave", "..")]
    [InlineData("Patch", ".", "Balance Patch", "1.0")]
    [InlineData("Addon", "..", "HD Addon", "1.0")]
    public void ImportAsyncWhenManualIdentity_UsesDotSegmentFailsBeforeImporter(
        string kindName,
        string? parentContentName,
        string contentName,
        string version)
    {
        StaTestRunner.Run(async () =>
        {
            ModificationType kind = Enum.Parse<ModificationType>(kindName);
            string[] selectedFiles = [@"C:\Downloads\manual.zip"];
            IManualModificationImporter importer = Substitute.For<IManualModificationImporter>();
            var catalog = new FakeLauncherContentCatalog();
            LauncherManualImportCoordinator coordinator = CreateCoordinator(
                new StubLauncherFilePicker
                {
                    ManualPackageFilesResult = selectedFiles
                },
                CreateDialogService(new ManualModificationDialogResult(
                    contentName,
                    version)),
                catalog,
                importer);

            Func<Task> act = () => coordinator.ImportAsync(
                kind,
                new Window(),
                parentContentName,
                CancellationToken.None);

            await act.Should().ThrowAsync<ArgumentException>();
            importer.DidNotReceive().Import(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<OwnedContentPath>(),
                Arg.Any<CancellationToken>());
            catalog.LocalDataUpdateCount.Should().Be(0);
        });
    }

    [Fact]
    public void ImportAsyncWhenImportedVersion_IsMissingThrows()
    {
        StaTestRunner.Run(async () =>
        {
            string[] selectedFiles = [@"C:\Downloads\shockwave.zip"];
            FakeLauncherContentCatalog catalog = CreateCatalog(
                CreateVersion("Shockwave", "1.1", ModificationType.Mod));
            LauncherManualImportCoordinator coordinator = CreateCoordinator(
                new StubLauncherFilePicker
                {
                    ManualPackageFilesResult = selectedFiles
                },
                CreateDialogService(new ManualModificationDialogResult("Shockwave", "1.2")),
                catalog);

            Func<Task> act = () => coordinator.ImportAsync(
                ModificationType.Mod,
                new Window(),
                null,
                CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Imported Modification 'Shockwave' version '1.2' was not found*");
        });
    }

    private static LauncherManualImportCoordinator CreateCoordinator(
        ILauncherFilePicker? filePicker = null,
        ILauncherDialogService? dialogService = null,
        FakeLauncherContentCatalog? catalog = null,
        IManualModificationImporter? manualModificationImporter = null,
        ILaunchContentIntegrityResolutionService? resolutionService = null)
    {
        FakeLauncherContentCatalog resolvedCatalog = catalog ?? new FakeLauncherContentCatalog();
        LauncherRuntimePathContext runtimePaths =
            TestLauncherPaths.CreateRuntimePathContext(CreateLauncherPaths());
        return new LauncherManualImportCoordinator(
            filePicker ?? new StubLauncherFilePicker(),
            dialogService ?? Substitute.For<ILauncherDialogService>(),
            resolvedCatalog,
            runtimePaths,
            manualModificationImporter ?? Substitute.For<IManualModificationImporter>(),
            TestLaunchContentIntegrityCoordinator.Create(
                resolutionService,
                resolvedCatalog,
                runtimePaths),
            NullLogger<LauncherManualImportCoordinator>.Instance);
    }

    private static LauncherPaths CreateLauncherPaths()
    {
        return TestLauncherPaths.Create();
    }

    private static ILauncherDialogService CreateDialogService(ManualModificationDialogResult result)
    {
        ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
        dialogService.ShowManualModificationImportAsync(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<Window?>())
            .Returns(result);
        return dialogService;
    }

    private static FakeLauncherContentCatalog CreateCatalog(
        LauncherContentVersion version,
        string? selectedParentName = null)
    {
        var catalog = new FakeLauncherContentCatalog();
        if (!string.IsNullOrWhiteSpace(selectedParentName))
        {
            var parentVersion = new LauncherContentVersion
            {
                Installation = new LauncherContentInstallation { IsSelected = true },
                Name = selectedParentName,
                Version = "1.0",
                ModificationType = ModificationType.Mod
            };
            catalog.Data.AddOrUpdate(parentVersion);
            catalog.Data.Modifications.Single().IsSelected = true;
        }

        catalog.Data.AddOrUpdate(version);
        return catalog;
    }

    private static LauncherContentVersion CreateVersion(
        string name,
        string version,
        ModificationType modificationType,
        string parentContentName = "")
    {
        return new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation
            { ContentSourceKind = ContentSourceKind.Manual },
            Name = name,
            Version = version,
            ModificationType = modificationType,
            ParentContentName = parentContentName
        };
    }
}
