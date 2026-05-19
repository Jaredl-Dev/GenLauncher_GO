using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Contracts;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Services;

public sealed class LauncherManualImportCoordinatorTests
{
    [Fact]
    public void ImportAsyncWhenNoFilesAreSelectedReturnsNullWithoutShowingDialog()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherFilePicker filePicker = CreateFilePicker(Array.Empty<string>());
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherManualImportCoordinator coordinator = CreateCoordinator(
                filePicker: filePicker,
                dialogService: dialogService);

            LauncherManualImportResult? result = await coordinator.ImportAsync(
                new LauncherManualImportRequest(ModificationType.Mod, new Window()),
                CancellationToken.None);

            result.Should().BeNull();
            await dialogService.DidNotReceive().ShowManualModificationImportAsync(
                Arg.Any<ManualModificationDialogRequest>(),
                Arg.Any<Window?>());
        });
    }

    [Fact]
    public void ImportAsyncForModificationImportsToModVersionFolderAndRegistersSnapshot()
    {
        StaTestRunner.Run(async () =>
        {
            string[] selectedFiles = { @"C:\Downloads\shockwave.zip" };
            LauncherContentVersion savedVersion = CreateVersion("Shockwave", "1.2", ModificationType.Mod);
            FakeLauncherContentCatalog catalog = CreateCatalog(savedVersion);
            LauncherContent savedModification = catalog.Data.Modifications.Single();
            ILauncherFilePicker filePicker = CreateFilePicker(selectedFiles);
            ILauncherDialogService dialogService = CreateDialogService(
                new ManualModificationDialogResult(selectedFiles, null, "Shockwave", "1.2"));
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
                manualModificationImporter: importer,
                resolutionService: resolutionService);

            LauncherManualImportResult? result = await coordinator.ImportAsync(
                new LauncherManualImportRequest(ModificationType.Mod, new Window()),
                CancellationToken.None);

            result.Should().NotBeNull();
            result.Kind.Should().Be(ModificationType.Mod);
            result.Modification.Should().BeSameAs(savedModification);
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

    [Theory]
    [InlineData("Patch", "Patches", "Balance Patch")]
    [InlineData("Addon", "Addons", "HD Addon")]
    public void ImportAsyncForChildContentUsesParentContentFolder(
        string kindName,
        string childFolderName,
        string contentName)
    {
        StaTestRunner.Run(async () =>
        {
            ModificationType kind = Enum.Parse<ModificationType>(kindName);
            string[] selectedFiles = { @"C:\Downloads\child.zip" };
            ModificationType modificationType = kind == ModificationType.Patch
                ? ModificationType.Patch
                : ModificationType.Addon;
            LauncherContentVersion savedVersion = CreateVersion(contentName, "3.0", modificationType, "Shockwave");
            FakeLauncherContentCatalog catalog = CreateCatalog(savedVersion, selectedParentName: "Shockwave");
            LauncherContent savedModification = modificationType == ModificationType.Patch
                ? catalog.Data.Patches.Single()
                : catalog.Data.Addons.Single();
            ILauncherDialogService dialogService = CreateDialogService(
                new ManualModificationDialogResult(selectedFiles, "Shockwave", contentName, "3.0"));
            IManualModificationImporter importer = Substitute.For<IManualModificationImporter>();
            OwnedContentPath? capturedDestinationPath = null;
            importer
                .When(service => service.Import(
                    Arg.Any<IReadOnlyList<string>>(),
                    Arg.Do<OwnedContentPath>(path => capturedDestinationPath = path),
                    Arg.Any<CancellationToken>()))
                .Do(_ => { });
            LauncherManualImportCoordinator coordinator = CreateCoordinator(
                CreateFilePicker(selectedFiles),
                dialogService,
                catalog,
                manualModificationImporter: importer);

            LauncherManualImportResult? result = await coordinator.ImportAsync(
                new LauncherManualImportRequest(kind, new Window(), "Shockwave"),
                CancellationToken.None);

            result.Should().NotBeNull();
            result.Kind.Should().Be(kind);
            result.Modification.Should().BeSameAs(savedModification);
            capturedDestinationPath.Should().NotBeNull();
            LauncherPaths paths = CreateLauncherPaths();
            capturedDestinationPath.FullPath.Should().Be(
                Path.Combine(
                    paths.ModsDirectory,
                    "Shockwave",
                    childFolderName,
                    contentName,
                    "3.0"));
        });
    }

    [Fact]
    public void ImportAsyncForChildContentWithoutParentUsesOriginalGameFolder()
    {
        StaTestRunner.Run(async () =>
        {
            string[] selectedFiles = { @"C:\Downloads\original-patch.zip" };
            LauncherContentVersion savedVersion = CreateVersion(
                "Community Patch",
                "1.0",
                ModificationType.Patch,
                LauncherContentKey.OriginalGame.Name);
            FakeLauncherContentCatalog catalog = CreateCatalog(savedVersion);
            ILauncherDialogService dialogService = CreateDialogService(
                new ManualModificationDialogResult(selectedFiles, null, "Community Patch", "1.0"));
            IManualModificationImporter importer = Substitute.For<IManualModificationImporter>();
            ManualModificationDialogRequest? capturedDialogRequest = null;
            OwnedContentPath? capturedDestinationPath = null;
            dialogService.ShowManualModificationImportAsync(
                    Arg.Do<ManualModificationDialogRequest>(request => capturedDialogRequest = request),
                    Arg.Any<Window?>())
                .Returns(new ManualModificationDialogResult(selectedFiles, null, "Community Patch", "1.0"));
            importer
                .When(service => service.Import(
                    Arg.Any<IReadOnlyList<string>>(),
                    Arg.Do<OwnedContentPath>(path => capturedDestinationPath = path),
                    Arg.Any<CancellationToken>()))
                .Do(_ => { });
            LauncherManualImportCoordinator coordinator = CreateCoordinator(
                CreateFilePicker(selectedFiles),
                dialogService,
                catalog,
                manualModificationImporter: importer);

            await coordinator.ImportAsync(
                new LauncherManualImportRequest(ModificationType.Patch, new Window()),
                CancellationToken.None);

            capturedDialogRequest.Should().NotBeNull();
            capturedDialogRequest.ParentContentName.Should().Be(LauncherContentKey.OriginalGame.Name);
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
    public void ImportAsyncWhenManualIdentityUsesDotSegmentFailsBeforeImporter(
        string kindName,
        string? parentContentName,
        string contentName,
        string version)
    {
        StaTestRunner.Run(async () =>
        {
            ModificationType kind = Enum.Parse<ModificationType>(kindName);
            string[] selectedFiles = { @"C:\Downloads\manual.zip" };
            IManualModificationImporter importer = Substitute.For<IManualModificationImporter>();
            var catalog = new FakeLauncherContentCatalog();
            LauncherManualImportCoordinator coordinator = CreateCoordinator(
                CreateFilePicker(selectedFiles),
                CreateDialogService(new ManualModificationDialogResult(
                    selectedFiles,
                    parentContentName,
                    contentName,
                    version)),
                catalog: catalog,
                manualModificationImporter: importer);

            Func<Task> act = () => coordinator.ImportAsync(
                new LauncherManualImportRequest(kind, new Window(), parentContentName),
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
    public void ImportAsyncWhenImportedVersionIsMissingThrows()
    {
        StaTestRunner.Run(async () =>
        {
            string[] selectedFiles = { @"C:\Downloads\shockwave.zip" };
            FakeLauncherContentCatalog catalog = CreateCatalog(
                CreateVersion("Shockwave", "1.1", ModificationType.Mod));
            LauncherManualImportCoordinator coordinator = CreateCoordinator(
                CreateFilePicker(selectedFiles),
                CreateDialogService(new ManualModificationDialogResult(selectedFiles, null, "Shockwave", "1.2")),
                catalog);

            Func<Task> act = () => coordinator.ImportAsync(
                new LauncherManualImportRequest(ModificationType.Mod, new Window()),
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
            filePicker ?? CreateFilePicker(Array.Empty<string>()),
            dialogService ?? Substitute.For<ILauncherDialogService>(),
            resolvedCatalog,
            runtimePaths,
            manualModificationImporter ?? Substitute.For<IManualModificationImporter>(),
            CreateIntegrityCoordinator(resolutionService, resolvedCatalog, runtimePaths),
            NullLogger<LauncherManualImportCoordinator>.Instance);
    }

    private static LauncherPaths CreateLauncherPaths()
    {
        return TestLauncherPaths.Create();
    }

    private static LaunchContentIntegrityCoordinator CreateIntegrityCoordinator(
        ILaunchContentIntegrityResolutionService? resolutionService,
        ILauncherContentCatalog catalog,
        LauncherRuntimePathContext runtimePaths)
    {
        return new LaunchContentIntegrityCoordinator(
            resolutionService ?? Substitute.For<ILaunchContentIntegrityResolutionService>(),
            catalog,
            runtimePaths,
            new LauncherPackageActivityService(),
            new TestStringLocalizer(new Dictionary<string, string>
            {
                ["IntegrityCacheSuffix"] = " cache"
            }),
            Substitute.For<ILauncherDialogService>(),
            NullLogger<LaunchContentIntegrityCoordinator>.Instance);
    }

    private static ILauncherFilePicker CreateFilePicker(IReadOnlyList<string> selectedFiles)
    {
        return new StubLauncherFilePicker(selectedFiles);
    }

    private static ILauncherDialogService CreateDialogService(ManualModificationDialogResult result)
    {
        ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
        dialogService.ShowManualModificationImportAsync(
                Arg.Any<ManualModificationDialogRequest>(),
                Arg.Any<Window?>())
            .Returns(result);
        return dialogService;
    }

    private static FakeLauncherContentCatalog CreateCatalog(
        LauncherContentVersion version,
        string? selectedParentName = null)
    {
        var catalog = new FakeLauncherContentCatalog();
        if (!String.IsNullOrWhiteSpace(selectedParentName))
        {
            var parentVersion = new LauncherContentVersion
            {
                Installation = new LauncherContentInstallation { IsSelected = true },
                Name = selectedParentName,
                Version = "1.0",
                ModificationType = ModificationType.Mod,
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
            Installation = new LauncherContentInstallation { ContentSourceKind = GenLauncherGO.Core.Integrity.Models.ContentSourceKind.Manual },
            Name = name,
            Version = version,
            ModificationType = modificationType,
            ParentContentName = parentContentName,
        };
    }

    private sealed class StubLauncherFilePicker : ILauncherFilePicker
    {
        public Task<string?> PickGameInstallationFolderAsync(
            Window owner,
            string? initialDirectory)
        {
            return Task.FromResult<string?>(null);
        }

        private readonly IReadOnlyList<string> _selectedFiles;

        public StubLauncherFilePicker(IReadOnlyList<string> selectedFiles)
        {
            _selectedFiles = selectedFiles;
        }

        public Task<IReadOnlyList<string>> PickManualPackageFilesAsync(Window owner)
        {
            return Task.FromResult(_selectedFiles);
        }

        public Task<string?> PickModificationImageFileAsync(
            Window owner,
            string imageFilterLabel)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> PickGameExecutableFileAsync(Window owner, string gameDirectory)
        {
            return Task.FromResult<string?>(null);
        }
    }
}
