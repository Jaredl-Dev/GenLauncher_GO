using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Exceptions;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Mods.Models;
using GenLauncherGO.Infrastructure.Mods.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Mods.Services;

public sealed class LauncherContentCatalogServiceTests
{
    [Fact]
    public async Task InitDataAsyncWithDisconnectedCatalog_LoadsOnlyLocalStateAsync()
    {
        using var harness = new CatalogTestHarness();
        harness.StateStore.StateToLoad = CreateState(
            TestLauncherContent.Version("ShockWave", "1.0", installed: true));
        harness.LocalContent.InstalledVersions =
        [
            TestLauncherContent.Version("ShockWave", "1.0", installed: true)
        ];

        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, harness.Paths),
            CancellationToken.None);
        await harness.Service.ReadPatchesAndAddonsForModAsync(
            LauncherContentKey.ForModificationName("ShockWave"),
            CancellationToken.None);

        harness.Service.Data.Modifications.Select(modification => modification.Name).Should().Equal("ShockWave");
        harness.Service.RepositoryModificationNames.Should().BeNull();
        harness.YamlReader.GetReadCount<LegacyLauncherCatalogDocument>().Should().Be(0);
    }

    [Fact]
    public async Task InitDataAsyncSwitchesGameNamespaceAnd_ClearsPreviouslyCachedContentAsync()
    {
        using var harness = new CatalogTestHarness();
        (LauncherPaths generalsPaths, LauncherPaths zeroHourPaths) = harness.CreateBothGamePaths();
        harness.StateStore.StatesToLoadByGame[SupportedGame.Generals] =
            CreateState(TestLauncherContent.Version("Shared Mod", "Generals Version", installed: true));
        harness.StateStore.StatesToLoadByGame[SupportedGame.ZeroHour] =
            CreateState(TestLauncherContent.Version("Shared Mod", "Zero Hour Version", installed: true));
        harness.LocalContent.InstalledVersions =
        [
            TestLauncherContent.Version("Shared Mod", "Generals Version", installed: true)
        ];

        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, generalsPaths),
            CancellationToken.None);
        harness.Service.Data.Modifications.Should().ContainSingle()
            .Which.Versions.Should().ContainSingle()
            .Which.Version.Should().Be("Generals Version");

        harness.LocalContent.InstalledVersions =
        [
            TestLauncherContent.Version("Shared Mod", "Zero Hour Version", installed: true)
        ];
        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, zeroHourPaths),
            CancellationToken.None);

        harness.Service.Data.Modifications.Should().ContainSingle()
            .Which.Versions.Should().ContainSingle()
            .Which.Version.Should().Be("Zero Hour Version");
        harness.StateStore.LoadedPaths.Should().Equal(generalsPaths, zeroHourPaths);
    }

    [Fact]
    public async Task InitDataAsync_RestoresPreviousGameCatalogWhenSwitchInitializationFailsAsync()
    {
        using var harness = new CatalogTestHarness();
        (LauncherPaths generalsPaths, LauncherPaths zeroHourPaths) = harness.CreateBothGamePaths();
        var manifestUri = new Uri("https://example.test/unavailable.yaml");
        harness.YamlReader.SetException<LegacyLauncherCatalogDocument>(
            manifestUri,
            new IOException("Catalog unavailable."));
        harness.StateStore.StatesToLoadByGame[SupportedGame.Generals] =
            CreateState(TestLauncherContent.Version("Shared Mod", "Generals Version", installed: true));
        harness.LocalContent.InstalledVersions =
        [
            TestLauncherContent.Version("Shared Mod", "Generals Version", installed: true)
        ];

        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, generalsPaths),
            CancellationToken.None);
        Func<Task> switchGame = () => harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, zeroHourPaths),
            CancellationToken.None);

        await switchGame.Should().ThrowAsync<IOException>();
        harness.Service.Data.Modifications.Should().ContainSingle()
            .Which.Versions.Should().ContainSingle()
            .Which.Version.Should().Be("Generals Version");

        harness.Service.SaveLauncherData();
        harness.StateStore.SavedPaths.Should().ContainSingle().Which.Should().Be(generalsPaths);
    }

    [Fact]
    public async Task InitDataAsync_ReadsRemoteCatalogForInstalledModsAndDownloadsImagesAsync()
    {
        using var harness = new CatalogTestHarness();
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var modUri = new Uri("https://example.test/shockwave.yaml");
        var cardImageUri = new Uri("https://cdn.example.test/shockwave.jpg");
        harness.StateStore.StateToLoad = CreateState(
            TestLauncherContent.Version("ShockWave", "1.0", installed: true));
        harness.LocalContent.InstalledVersions =
        [
            TestLauncherContent.Version("ShockWave", "1.0", installed: true)
        ];
        harness.YamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
        {
            modDatas =
            [
                new()
                {
                    ModName = "ShockWave",
                    ModLink = modUri.ToString()
                }
            ]
        });
        harness.YamlReader.SetResult(modUri, new LegacyContentManifest
        {
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = "1.2",
            UIImageSourceLink = cardImageUri.ToString()
        });

        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, harness.Paths),
            CancellationToken.None);

        harness.Service.RepositoryModificationNames.Should().Equal("ShockWave");
        LauncherContent mod = harness.Service.Data.Modifications
            .Should()
            .ContainSingle(item => item.Name == "ShockWave")
            .Subject;
        mod.Versions.Should().Contain(version => version.Version == "1.2");
        harness.AssetDownloader.Calls.Should().Equal(
            (cardImageUri, harness.Paths.GetModificationImageFilePath("ShockWave", "1.2.jpg")));
    }

    /// <summary>
    ///     Persisted state carries no remote metadata, so a themed launcher only survives an offline restart because
    ///     the palette that came down with the manifest was cached beside the artwork it belongs to.
    /// </summary>
    [Fact]
    public async Task InitDataAsync_RestoresCachedPaletteWhenReopenedWithoutTheCatalogAsync()
    {
        using var harness = new CatalogTestHarness();
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var modUri = new Uri("https://example.test/contra.yaml");
        harness.StateStore.StateToLoad = CreateState(
            TestLauncherContent.Version("Contra", "009", installed: true));
        harness.LocalContent.InstalledVersions =
        [
            TestLauncherContent.Version("Contra", "009", installed: true)
        ];
        harness.YamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
        {
            modDatas =
            [
                new()
                {
                    ModName = "Contra",
                    ModLink = modUri.ToString()
                }
            ]
        });
        harness.YamlReader.SetResult(modUri, new LegacyContentManifest
        {
            ModificationType = ModificationType.Mod,
            Name = "Contra",
            Version = "009",
            ColorsInformation = new LegacyContentThemeManifest { GenLauncherActiveColor = "#baff0c" }
        });
        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, harness.Paths),
            CancellationToken.None);
        harness.Service.SaveLauncherData();
        harness.StateStore.StateToLoad = harness.StateStore.SavedStates.Single();

        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, harness.Paths),
            CancellationToken.None);

        LauncherContentVersion restored = harness.Service.Data.Modifications.Should().ContainSingle().Subject
            .Versions.Should().ContainSingle().Subject;
        restored.Theme.Should().NotBeNull();
        restored.Theme!.GenLauncherActiveColor.Should().Be("#baff0c");
    }

    [Fact]
    public async Task InitDataAsync_LoadsSelectedModPatchesAndAddonsAsync()
    {
        using var harness = new CatalogTestHarness();
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var modUri = new Uri("https://example.test/shockwave.yaml");
        var patchUri = new Uri("https://example.test/patch.yaml");
        var addonUri = new Uri("https://example.test/addon.yaml");
        harness.StateStore.StateToLoad = LauncherContentStateMapper.ToLauncherContentState(
            TestLauncherContent.Catalog()
                .WithMod("ShockWave", "1.0")
                .Selected("ShockWave")
                .Build()
                .Data);
        harness.LocalContent.InstalledVersions =
        [
            TestLauncherContent.Version("ShockWave", "1.0", installed: true)
        ];
        harness.YamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
        {
            modDatas =
            [
                new()
                {
                    ModName = "ShockWave",
                    ModLink = modUri.ToString(),
                    ModPatches = [patchUri.ToString()],
                    ModAddons = [addonUri.ToString()]
                }
            ]
        });
        harness.YamlReader.SetResult(
            modUri,
            CreateRemoteVersion("ShockWave", "1.2", ModificationType.Mod));
        harness.YamlReader.SetResult(
            patchUri,
            CreateRemoteVersion("Balance", "2.0", ModificationType.Patch, "ShockWave"));
        harness.YamlReader.SetResult(
            addonUri,
            CreateRemoteVersion("HD", "1.0", ModificationType.Addon, "ShockWave"));

        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, harness.Paths),
            CancellationToken.None);
        await harness.Service.ReadPatchesAndAddonsForModAsync(
            LauncherContentKey.ForModificationName("ShockWave"),
            CancellationToken.None);

        LauncherContent shockWave = harness.Service.Data.Modifications.Should().ContainSingle().Subject;
        harness.Service.Data.GetPatchesFor(shockWave).Select(patch => patch.Name).Should().Equal("Balance");
        harness.Service.Data.GetAddonsFor(shockWave, null).Select(addon => addon.Name).Should().Equal("HD");
        harness.YamlReader.GetReadCount<LegacyContentManifest>(patchUri).Should().Be(1);
        harness.YamlReader.GetReadCount<LegacyContentManifest>(addonUri).Should().Be(1);
    }

    [Fact]
    public async Task ReadPatchesAndAddonsForModAsync_RetriesAfterPartialLoadFailureAsync()
    {
        using var harness = new CatalogTestHarness();
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var modUri = new Uri("https://example.test/shockwave.yaml");
        var patchUri = new Uri("https://example.test/patch.yaml");
        harness.YamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
        {
            modDatas =
            {
                new LegacyCatalogModificationReference
                {
                    ModName = "ShockWave",
                    ModLink = modUri.ToString(),
                    ModPatches = { patchUri.ToString() }
                }
            }
        });
        harness.YamlReader.SetResult(
            modUri,
            CreateRemoteVersion("ShockWave", "1.2", ModificationType.Mod));
        harness.YamlReader.SetHandler<LegacyContentManifest>(
            patchUri,
            (callIndex, _) => callIndex == 1
                ? Task.FromException<LegacyContentManifest>(new IOException("Temporary failure."))
                : Task.FromResult(CreateRemoteVersion(
                    "Balance",
                    "2.0",
                    ModificationType.Patch,
                    "ShockWave")));

        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, harness.Paths),
            CancellationToken.None);
        await harness.Service.AddRepositoryModificationAsync("ShockWave", CancellationToken.None);
        var modification = LauncherContentKey.ForModificationName("ShockWave");

        await harness.Service.ReadPatchesAndAddonsForModAsync(modification, CancellationToken.None);
        await harness.Service.ReadPatchesAndAddonsForModAsync(modification, CancellationToken.None);
        await harness.Service.ReadPatchesAndAddonsForModAsync(modification, CancellationToken.None);

        harness.Service.Data.Patches.SelectMany(patch => patch.Versions).Should()
            .ContainSingle(version => version.Name == "Balance" && version.Version == "2.0");
        harness.YamlReader.GetReadCount<LegacyContentManifest>(patchUri).Should().Be(2);
    }

    [Fact]
    public async Task ReadPatchesAndAddonsForModAsyncCoalescesConcurrent_LoadsAsync()
    {
        using var harness = new CatalogTestHarness();
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var modUri = new Uri("https://example.test/shockwave.yaml");
        var patchUri = new Uri("https://example.test/patch.yaml");
        var patchReadStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePatchRead =
            new TaskCompletionSource<LegacyContentManifest>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.YamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
        {
            modDatas =
            {
                new LegacyCatalogModificationReference
                {
                    ModName = "ShockWave",
                    ModLink = modUri.ToString(),
                    ModPatches = { patchUri.ToString() }
                }
            }
        });
        harness.YamlReader.SetResult(
            modUri,
            CreateRemoteVersion("ShockWave", "1.2", ModificationType.Mod));
        harness.YamlReader.SetHandler<LegacyContentManifest>(
            patchUri,
            (_, _) =>
            {
                patchReadStarted.TrySetResult(true);
                return releasePatchRead.Task;
            });

        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, harness.Paths),
            CancellationToken.None);
        await harness.Service.AddRepositoryModificationAsync("ShockWave", CancellationToken.None);
        var modification = LauncherContentKey.ForModificationName("ShockWave");

        Task firstLoad = harness.Service.ReadPatchesAndAddonsForModAsync(modification, CancellationToken.None);
        await patchReadStarted.Task.WaitAsync(TestTimeouts.Wait);
        Task secondLoad = harness.Service.ReadPatchesAndAddonsForModAsync(modification, CancellationToken.None);
        releasePatchRead.SetResult(
            CreateRemoteVersion("Balance", "2.0", ModificationType.Patch, "ShockWave"));
        await Task.WhenAll(firstLoad, secondLoad);

        harness.Service.Data.Patches.SelectMany(patch => patch.Versions).Should()
            .ContainSingle(version => version.Name == "Balance" && version.Version == "2.0");
        harness.YamlReader.GetReadCount<LegacyContentManifest>(patchUri).Should().Be(1);
    }

    [Fact]
    public async Task ReadOriginalGameAddonsAndPatchesAsync_LoadsChildContentOnceAsync()
    {
        using var harness = new CatalogTestHarness();
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var patchUri = new Uri("https://example.test/original-patch.yaml");
        var addonUri = new Uri("https://example.test/original-addon.yaml");
        harness.YamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
        {
            originalGamePatches = [patchUri.ToString()],
            originalGameAddons = [addonUri.ToString()]
        });
        harness.YamlReader.SetResult(
            patchUri,
            CreateRemoteVersion("GenPatcher", "1.0", ModificationType.Patch));
        harness.YamlReader.SetResult(
            addonUri,
            CreateRemoteVersion("ControlBar", "1.0", ModificationType.Addon));

        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, harness.Paths),
            CancellationToken.None);
        await harness.Service.ReadOriginalGameAddonsAndPatchesAsync(CancellationToken.None);
        await harness.Service.ReadOriginalGameAddonsAndPatchesAsync(CancellationToken.None);
        harness.Service.UpdateLocalModificationsData();

        harness.Service.Data.GetPatchesFor(null).Select(patch => patch.Name).Should().Equal("GenPatcher");
        harness.Service.Data.GetAddonsFor(null, null).Select(addon => addon.Name).Should().Equal("ControlBar");
        harness.YamlReader.GetReadCount<LegacyContentManifest>(patchUri).Should().Be(1);
        harness.YamlReader.GetReadCount<LegacyContentManifest>(addonUri).Should().Be(1);
    }

    [Fact]
    public async Task ReadOriginalGameAddonsAndPatchesAsync_RetriesAfterPartialLoadFailureAsync()
    {
        using var harness = new CatalogTestHarness();
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var patchUri = new Uri("https://example.test/original-patch.yaml");
        harness.YamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
        {
            originalGamePatches = { patchUri.ToString() }
        });
        harness.YamlReader.SetHandler<LegacyContentManifest>(
            patchUri,
            (callIndex, _) => callIndex == 1
                ? Task.FromException<LegacyContentManifest>(new IOException("Temporary failure."))
                : Task.FromResult(CreateRemoteVersion("GenPatcher", "1.0", ModificationType.Patch)));
        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, harness.Paths),
            CancellationToken.None);

        await harness.Service.ReadOriginalGameAddonsAndPatchesAsync(CancellationToken.None);
        await harness.Service.ReadOriginalGameAddonsAndPatchesAsync(CancellationToken.None);
        await harness.Service.ReadOriginalGameAddonsAndPatchesAsync(CancellationToken.None);

        harness.Service.Data.Patches.SelectMany(patch => patch.Versions).Should()
            .ContainSingle(version => version.Name == "GenPatcher");
        harness.YamlReader.GetReadCount<LegacyContentManifest>(patchUri).Should().Be(2);
    }

    [Fact]
    public async Task ReadOriginalGameAddonsAndPatchesAsync_ReturnsWhenCatalogIsDisconnectedAsync()
    {
        using var harness = new CatalogTestHarness();
        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, harness.Paths),
            CancellationToken.None);

        await harness.Service.ReadOriginalGameAddonsAndPatchesAsync(CancellationToken.None);

        harness.YamlReader.GetReadCount<LegacyContentManifest>().Should().Be(0);
    }

    [Fact]
    public async Task ReadPatchesAndAddonsForModAsync_ReturnsWhenManifestLookupIsMissingAsync()
    {
        using var harness = new CatalogTestHarness();
        var manifestUri = new Uri("https://example.test/repos.yaml");
        harness.YamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument());
        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, harness.Paths),
            CancellationToken.None);

        await harness.Service.ReadPatchesAndAddonsForModAsync(
            LauncherContentKey.ForModificationName("Missing"),
            CancellationToken.None);

        harness.YamlReader.GetReadCount<LegacyContentManifest>().Should().Be(0);
    }

    [Fact]
    public async Task InitDataAsync_DownloadsAdvertisingMetadataAndImagesAsync()
    {
        using var harness = new CatalogTestHarness();
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var advertisingUri = new Uri("https://example.test/advertising.yaml");
        var imageUri = new Uri("https://cdn.example.test/advertising.jpg");
        harness.YamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
        {
            AdvData =
            [
                new()
                {
                    ModName = "RiseOfTheReds",
                    ModLink = advertisingUri.ToString(),
                    ImagesData = [imageUri.ToString()]
                }
            ]
        });
        harness.YamlReader.SetResult(advertisingUri, new LegacyContentManifest
        {
            ModificationType = ModificationType.Advertising,
            Name = "RiseOfTheReds",
            Version = "1.87"
        });

        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, harness.Paths),
            CancellationToken.None);

        LauncherContentVersion? advertising = harness.Service.Advertising;
        advertising.Should().NotBeNull();
        advertising!.Name.Should().Be("RiseOfTheReds");
        advertising.Version.Should().Be("1.87");
        harness.AssetDownloader.Calls.Should().ContainSingle(call =>
            call.SourceUri == imageUri &&
            call.DestinationFilePath ==
            harness.Paths.GetModificationImageFilePath("RiseOfTheReds", "0.jpg"));
    }

    [Fact]
    public async Task InitDataAsync_LeavesAdvertisingEmptyWhenManifestDownloadFailsAsync()
    {
        using var harness = new CatalogTestHarness();
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var advertisingUri = new Uri("https://example.test/advertising.yaml");
        harness.YamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
        {
            AdvData =
            [
                new()
                {
                    ModName = "RiseOfTheReds",
                    ModLink = advertisingUri.ToString(),
                    ImagesData = ["https://cdn.example.test/advertising.jpg"]
                }
            ]
        });
        harness.YamlReader.SetException<LegacyContentManifest>(
            advertisingUri,
            new IOException("Manifest unavailable."));

        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, harness.Paths),
            CancellationToken.None);

        harness.Service.Advertising.Should().BeNull();
        harness.AssetDownloader.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task PersistedSelectionState_IsLoadedAsync()
    {
        using var harness = new CatalogTestHarness();
        LauncherData persistedCatalog = TestLauncherContent.Catalog()
            .WithMod("ShockWave", "1.2")
            .WithMod("Contra", "009")
            .WithPatch("ShockWave", "BalancePatch", "2.0")
            .WithAddon("ShockWave", "HDTextures", "1.0")
            .WithAddon("BalancePatch", "PatchAddon", "1.1")
            .Selected("ShockWave")
            .Selected("BalancePatch", ModificationType.Patch, "ShockWave")
            .Selected("HDTextures", ModificationType.Addon, "ShockWave")
            .Selected("PatchAddon", ModificationType.Addon, "BalancePatch")
            .Build()
            .Data;
        var catalogState = LauncherContentStateMapper.ToLauncherContentState(persistedCatalog);
        harness.StateStore.StateToLoad = catalogState;
        harness.LocalContent.InstalledVersions = GetVersions(catalogState);

        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, harness.Paths),
            CancellationToken.None);

        LauncherContent selectedModification = harness.Service.Data.Modifications.Single(modification =>
            modification.IsSelected);
        LauncherContent selectedPatch = harness.Service.Data.Patches.Single(patch => patch.IsSelected);
        selectedModification.Versions.Should().ContainSingle(version =>
            version.Name == "ShockWave" && version.Version == "1.2" && version.Installation.IsSelected);
        selectedPatch.Versions.Should().ContainSingle(version =>
            version.Name == "BalancePatch" && version.Version == "2.0" && version.Installation.IsSelected);
        harness.Service.Data.GetPatchesFor(selectedModification)
            .Should().ContainSingle(patch => patch.Name == "BalancePatch");
        harness.Service.Data.GetAddonsFor(selectedModification, selectedPatch)
            .Should().Contain(addon => addon.Name == "HDTextures")
            .And.Contain(addon => addon.Name == "PatchAddon");
        harness.Service.Data.Addons.Should().OnlyContain(addon =>
            addon.IsSelected &&
            addon.Versions.Count(version => version.Installation.IsSelected) == 1);
        harness.Service.Data.GetAllModificationVersions().Should().HaveCount(2);
    }

    [Fact]
    public async Task UninstallVersion_DeletesLocalFilesAndReconcilesCatalogAsync()
    {
        using var harness = new CatalogTestHarness();
        LauncherContentVersion version = TestLauncherContent.Version("ShockWave", "1.0", installed: true);
        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, harness.Paths),
            CancellationToken.None);

        harness.Service.UninstallVersion(version.ContentKey);

        harness.LocalContent.DeletedVersions.Should().ContainSingle(request =>
            request.Paths == harness.Paths &&
            request.ContentKey.ContentType == ModificationType.Mod &&
            request.ContentKey.Name == "ShockWave" &&
            request.ContentKey.Version == "1.0");
        harness.LocalContent.ImageDeletionRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task AddRepositoryModificationAsync_AddsRemoteModAndCachesImagesExactlyOnceAsync()
    {
        using var harness = new CatalogTestHarness();
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var modUri = new Uri("https://example.test/contra.yaml");
        var imageUri = new Uri("https://cdn.example.test/contra.png");
        harness.YamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
        {
            modDatas =
            [
                new()
                {
                    ModName = "Contra",
                    ModLink = modUri.ToString()
                }
            ]
        });
        harness.YamlReader.SetResult(modUri, new LegacyContentManifest
        {
            ModificationType = ModificationType.Mod,
            Name = "Contra",
            Version = "009",
            UIImageSourceLink = imageUri.ToString()
        });
        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, harness.Paths),
            CancellationToken.None);

        LauncherContentVersion downloadedVersion = await harness.Service.AddRepositoryModificationAsync(
            "Contra",
            CancellationToken.None);

        downloadedVersion.Name.Should().Be("Contra");
        downloadedVersion.Version.Should().Be("009");
        LauncherContent addedModification = harness.Service.Data.Modifications.Should().ContainSingle().Subject;
        addedModification.Name.Should().Be("Contra");
        addedModification.Versions.Should().ContainSingle().Which.Should().BeSameAs(downloadedVersion);
        harness.AssetDownloader.Calls.Should().ContainSingle(call =>
            call.SourceUri == imageUri &&
            call.DestinationFilePath == harness.Paths.GetModificationImageFilePath("Contra", "009.png"));
    }

    [Fact]
    public async Task InitDataAsync_WaitsForOldGameMetadataLoadBeforeSwitchingCatalogAsync()
    {
        using var harness = new CatalogTestHarness();
        (LauncherPaths generalsPaths, LauncherPaths zeroHourPaths) = harness.CreateBothGamePaths();
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var modUri = new Uri("https://example.test/contra.yaml");
        var imageUri = new Uri("https://cdn.example.test/contra.png");
        var metadataReadStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMetadataRead = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        harness.YamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
        {
            modDatas =
            {
                new LegacyCatalogModificationReference
                {
                    ModName = "Contra",
                    ModLink = modUri.ToString()
                }
            }
        });
        harness.YamlReader.SetHandler<LegacyContentManifest>(
            modUri,
            async (_, cancellationToken) =>
            {
                metadataReadStarted.SetResult();
                await releaseMetadataRead.Task.WaitAsync(cancellationToken);
                return new LegacyContentManifest
                {
                    ModificationType = ModificationType.Mod,
                    Name = "Contra",
                    Version = "009",
                    UIImageSourceLink = imageUri.ToString()
                };
            });

        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, generalsPaths),
            CancellationToken.None);
        Task<LauncherContentVersion> oldGameDownload =
            harness.Service.AddRepositoryModificationAsync("Contra", CancellationToken.None);
        await metadataReadStarted.Task;
        Task switchGame = harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, zeroHourPaths),
            CancellationToken.None);

        releaseMetadataRead.SetResult();
        await oldGameDownload;
        await switchGame;

        harness.Service.Data.Modifications.Should().BeEmpty();
        harness.AssetDownloader.Calls.Should().ContainSingle(call =>
            call.SourceUri == imageUri &&
            call.DestinationFilePath == generalsPaths.GetModificationImageFilePath("Contra", "009.png"));
    }

    [Fact]
    public async Task InitDataAsync_WaitsForDirectMetadataReadBeforeSwitchingCatalogAsync()
    {
        using var harness = new CatalogTestHarness();
        (LauncherPaths generalsPaths, LauncherPaths zeroHourPaths) = harness.CreateBothGamePaths();
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var modUri = new Uri("https://example.test/contra.yaml");
        var metadataReadStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMetadataRead = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        harness.YamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
        {
            modDatas =
            {
                new LegacyCatalogModificationReference
                {
                    ModName = "Contra",
                    ModLink = modUri.ToString()
                }
            }
        });
        harness.YamlReader.SetHandler<LegacyContentManifest>(
            modUri,
            async (_, cancellationToken) =>
            {
                metadataReadStarted.SetResult();
                await releaseMetadataRead.Task.WaitAsync(cancellationToken);
                return new LegacyContentManifest
                {
                    ModificationType = ModificationType.Mod,
                    Name = "Contra",
                    Version = "009"
                };
            });

        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, generalsPaths),
            CancellationToken.None);
        Task<LauncherContentVersion> oldGameMetadata = harness.Service.GetRepositoryModificationMetadataAsync(
            "Contra",
            CancellationToken.None);
        await metadataReadStarted.Task;
        Task switchGame = harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, zeroHourPaths),
            CancellationToken.None);

        bool switchCompletedBeforeMetadataRead = switchGame.IsCompleted;
        releaseMetadataRead.SetResult();
        LauncherContentVersion metadata = await oldGameMetadata;
        await switchGame;

        switchCompletedBeforeMetadataRead.Should().BeFalse();
        metadata.Version.Should().Be("009");
        harness.Service.Data.Modifications.Should().BeEmpty();
        Func<Task> readOldMetadataFromNewSession = () => harness.Service.GetRepositoryModificationMetadataAsync(
            "Contra",
            CancellationToken.None);
        await readOldMetadataFromNewSession.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DiscardVersion_DeletesFolderAndCatalogVersionAsync()
    {
        using var harness = new CatalogTestHarness();
        LauncherContentVersion version = TestLauncherContent.Version("ShockWave", "1.0", installed: true);
        harness.StateStore.StateToLoad = CreateState(version);
        harness.LocalContent.InstalledVersions = [version];
        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, harness.Paths),
            CancellationToken.None);
        harness.LocalContent.InstalledVersions = [];

        harness.Service.DiscardVersion(version.ContentKey);

        harness.Service.Data.Modifications.Select(modification => modification.Name).Should()
            .NotContain("ShockWave");
        harness.LocalContent.DeletedVersions.Should().ContainSingle(request =>
            request.Paths == harness.Paths &&
            request.ContentKey.ContentType == ModificationType.Mod &&
            request.ContentKey.Name == "ShockWave" &&
            request.ContentKey.Version == "1.0");
        harness.LocalContent.ImageDeletionRequests.Should().ContainSingle(request =>
            request.Paths == harness.Paths &&
            request.ContentKey == version.ContentKey &&
            !request.ContentNames.Contains("ShockWave") &&
            ReferenceEquals(request.Data, harness.Service.Data));
    }

    [Fact]
    public async Task DiscardContent_DeletesEveryVersionFolderAndCatalogEntryAsync()
    {
        using var harness = new CatalogTestHarness();
        LauncherContentVersion version = TestLauncherContent.Version("ShockWave", "1.0", installed: true);
        LauncherContentVersion secondVersion = TestLauncherContent.Version("ShockWave", "2.0", installed: true);
        harness.StateStore.StateToLoad = CreateState(version, secondVersion);
        harness.LocalContent.InstalledVersions = [version, secondVersion];
        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, harness.Paths),
            CancellationToken.None);
        harness.LocalContent.InstalledVersions = [];

        harness.Service.DiscardContent(version.ContentKey);

        harness.Service.Data.Modifications.Select(modification => modification.Name).Should()
            .NotContain("ShockWave");
        harness.LocalContent.DeletedContents.Should().ContainSingle(request =>
            request.Paths == harness.Paths &&
            request.ContentKey.ContentType == ModificationType.Mod &&
            request.ContentKey.Name == "ShockWave" &&
            request.ContentKey.Version == "1.0");
        harness.LocalContent.DeletedVersions.Should().BeEmpty();
        harness.LocalContent.ImageDeletionRequests.Should().ContainSingle(request =>
            request.Paths == harness.Paths &&
            request.ContentKey == version.ContentKey &&
            !request.ContentNames.Contains("ShockWave") &&
            ReferenceEquals(request.Data, harness.Service.Data));
    }

    [Fact]
    public async Task SaveLauncherData_PersistsInstalledAndAddedRepositoryModsAsync()
    {
        using var harness = new CatalogTestHarness();
        LauncherContentVersion installedVersion = TestLauncherContent.Version(
            "ShockWave",
            "1.0",
            installed: true,
            isSelected: true);
        LauncherContentVersion repositoryVersion = TestLauncherContent.Version(
            "Contra",
            "2.0",
            sourceKind: ContentSourceKind.ManagedSingleFile);
        harness.StateStore.StateToLoad = CreateState(installedVersion, repositoryVersion);
        harness.LocalContent.InstalledVersions = [installedVersion];
        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, harness.Paths),
            CancellationToken.None);

        harness.Service.SaveLauncherData();

        harness.StateStore.SavedStates.Should().ContainSingle(state =>
            state.Modifications.Count == 2 &&
            state.Modifications[0].Name == "ShockWave" &&
            state.Modifications[0].ModificationVersions.Count == 1 &&
            state.Modifications[0].ModificationVersions[0].Version == "1.0" &&
            state.Modifications[0].ModificationVersions[0].Installed &&
            state.Modifications[0].ModificationVersions[0].IsSelected &&
            state.Modifications[1].Name == "Contra" &&
            state.Modifications[1].ModificationVersions.Count == 1 &&
            state.Modifications[1].ModificationVersions[0].Version == "2.0" &&
            !state.Modifications[1].ModificationVersions[0].Installed);
    }

    [Fact]
    public async Task PersistedManagedRepositoryMod_SurvivesDisconnectedCatalogReloadAsync()
    {
        using var harness = new CatalogTestHarness();
        harness.StateStore.StateToLoad = CreateState(TestLauncherContent.Version(
            "Contra",
            "2.0",
            sourceKind: ContentSourceKind.ManagedSingleFile));
        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, harness.Paths),
            CancellationToken.None);
        harness.Service.SaveLauncherData();
        harness.StateStore.StateToLoad = harness.StateStore.SavedStates.Single();

        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, harness.Paths),
            CancellationToken.None);

        harness.Service.Data.Modifications.Should().ContainSingle()
            .Which.Name.Should().Be("Contra");
    }

    [Fact]
    public async Task SaveLauncherData_PersistsOriginalGameSelectionWithoutModificationCardsAsync()
    {
        using var harness = new CatalogTestHarness();
        LauncherContentVersion patch = TestLauncherContent.Version(
            "Original Patch",
            "1.0",
            ModificationType.Patch,
            LauncherContentKey.OriginalGame.Name,
            installed: true,
            isSelected: true);
        harness.StateStore.StateToLoad = CreateState(patch);
        harness.LocalContent.InstalledVersions = [patch];
        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, harness.Paths),
            CancellationToken.None);

        harness.Service.SaveLauncherData();

        LauncherContentState savedState = harness.StateStore.SavedStates.Should().ContainSingle().Subject;
        savedState.Modifications.Should().BeEmpty();
        LauncherContentEntryState savedPatch = savedState.Patches.Should().ContainSingle().Subject;
        savedPatch.Name.Should().Be("Original Patch");
        savedPatch.IsSelected.Should().BeTrue();
        savedPatch.ModificationVersions.Should().ContainSingle().Which.IsSelected.Should().BeTrue();
    }

    [Fact]
    public async Task SaveLauncherDataWhenPersistence_FailsPreservesCatalogForRetryAsync()
    {
        using var harness = new CatalogTestHarness();
        int saveAttempts = 0;
        harness.StateStore.SaveHandler = _ =>
        {
            saveAttempts++;
            if (saveAttempts == 1)
            {
                throw new IOException("Catalog file is locked.");
            }
        };
        LauncherContentVersion version = TestLauncherContent.Version(
            "ShockWave",
            "1.2",
            installed: true,
            sourceKind: ContentSourceKind.Manual);
        harness.StateStore.StateToLoad = CreateState(version);
        harness.LocalContent.InstalledVersions = [version];
        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, harness.Paths),
            CancellationToken.None);
        Action firstSave = harness.Service.SaveLauncherData;

        firstSave.Should().Throw<LauncherContentPersistenceException>()
            .WithInnerException<IOException>();
        harness.Service.Data.Modifications.Should().ContainSingle(modification =>
            modification.Name == "ShockWave" &&
            modification.Versions.Single().Installation.Installed);

        harness.Service.SaveLauncherData();

        saveAttempts.Should().Be(2);
        harness.StateStore.SavedStates.Should().HaveCount(2);
        harness.StateStore.SavedStates.Should().OnlyContain(state =>
            state.Modifications.Count == 1 &&
            state.Modifications[0].Name == "ShockWave" &&
            state.Modifications[0].ModificationVersions[0].ContentSourceKind == ContentSourceKind.Manual);
    }

    /// <summary>
    ///     Saved state is only half the picture. A modification whose folder is on disk but which the state file never
    ///     recorded — a crash between installing and saving is enough — still has to appear in the list.
    /// </summary>
    [Fact]
    public async Task InitDataAsync_AddsInstalledContentThatSavedStateDoesNotRecordAsync()
    {
        using var harness = new CatalogTestHarness();
        harness.LocalContent.InstalledVersions =
        [
            TestLauncherContent.Version("ShockWave", "1.0", installed: true)
        ];

        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, harness.Paths),
            CancellationToken.None);

        harness.Service.Data.Modifications.Should().ContainSingle()
            .Which.Versions.Should().ContainSingle()
            .Which.Version.Should().Be("1.0");
    }

    /// <summary>
    ///     Removing a version rescans the mods folder in the same pass, so what the list shows afterwards is what is
    ///     actually on disk rather than what was there when the session started.
    /// </summary>
    [Fact]
    public async Task UninstallVersion_RescansLocalContentAsync()
    {
        using var harness = new CatalogTestHarness();
        LauncherContentVersion version = TestLauncherContent.Version("ShockWave", "1.0", installed: true);
        harness.StateStore.StateToLoad = CreateState(version);
        harness.LocalContent.InstalledVersions = [version];
        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, harness.Paths),
            CancellationToken.None);
        harness.LocalContent.InstalledVersions =
        [
            TestLauncherContent.Version("Contra", "009", installed: true)
        ];

        harness.Service.UninstallVersion(version.ContentKey);

        harness.Service.Data.Modifications.Select(modification => modification.Name).Should().Equal("Contra");
    }

    [Fact]
    public async Task DiscardVersion_RescansLocalContentAsync()
    {
        using var harness = new CatalogTestHarness();
        LauncherContentVersion version = TestLauncherContent.Version("ShockWave", "1.0", installed: true);
        harness.StateStore.StateToLoad = CreateState(version);
        harness.LocalContent.InstalledVersions = [version];
        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, harness.Paths),
            CancellationToken.None);
        harness.LocalContent.InstalledVersions =
        [
            TestLauncherContent.Version("Contra", "009", installed: true)
        ];

        harness.Service.DiscardVersion(version.ContentKey);

        harness.Service.Data.Modifications.Select(modification => modification.Name).Should().Equal("Contra");
    }

    [Fact]
    public async Task DiscardContent_RescansLocalContentAsync()
    {
        using var harness = new CatalogTestHarness();
        LauncherContentVersion version = TestLauncherContent.Version("ShockWave", "1.0", installed: true);
        harness.StateStore.StateToLoad = CreateState(version);
        harness.LocalContent.InstalledVersions = [version];
        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, harness.Paths),
            CancellationToken.None);
        harness.LocalContent.InstalledVersions =
        [
            TestLauncherContent.Version("Contra", "009", installed: true)
        ];

        harness.Service.DiscardContent(version.ContentKey);

        harness.Service.Data.Modifications.Select(modification => modification.Name).Should().Equal("Contra");
    }

    /// <summary>
    ///     A version the catalog still publishes stays on its card as "not installed" when its folder is gone, so the
    ///     user can download it again. Only content nobody publishes any more is dropped from the list outright.
    /// </summary>
    [Fact]
    public async Task UninstallVersion_KeepsARemotelyPublishedVersionAsNotInstalledAsync()
    {
        using var harness = new CatalogTestHarness();
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var modUri = new Uri("https://example.test/shockwave.yaml");
        LauncherContentVersion version = TestLauncherContent.Version("ShockWave", "1.2", installed: true);
        harness.StateStore.StateToLoad = CreateState(version);
        harness.LocalContent.InstalledVersions = [version];
        harness.YamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
        {
            modDatas =
            [
                new()
                {
                    ModName = "ShockWave",
                    ModLink = modUri.ToString()
                }
            ]
        });
        harness.YamlReader.SetResult(modUri, new LegacyContentManifest
        {
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = "1.2"
        });
        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, harness.Paths),
            CancellationToken.None);
        harness.LocalContent.InstalledVersions = [];

        harness.Service.UninstallVersion(version.ContentKey);

        LauncherContentVersion remaining = harness.Service.Data.Modifications.Should().ContainSingle().Subject
            .Versions.Should().ContainSingle().Subject;
        remaining.Version.Should().Be("1.2");
        remaining.Installation.Installed.Should().BeFalse();
    }

    /// <summary>
    ///     Repository metadata is reused once read. The details pane asks for it every time it is opened, and each
    ///     read is a round trip to a third-party backend.
    /// </summary>
    [Fact]
    public async Task GetRepositoryModificationMetadataAsync_ReadsTheManifestOnceForRepeatedRequestsAsync()
    {
        using var harness = new CatalogTestHarness();
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var modUri = new Uri("https://example.test/contra.yaml");
        harness.YamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
        {
            modDatas =
            [
                new()
                {
                    ModName = "Contra",
                    ModLink = modUri.ToString()
                }
            ]
        });
        harness.YamlReader.SetResult(modUri, new LegacyContentManifest
        {
            ModificationType = ModificationType.Mod,
            Name = "Contra",
            Version = "009"
        });
        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, harness.Paths),
            CancellationToken.None);

        LauncherContentVersion firstRead = await harness.Service.GetRepositoryModificationMetadataAsync(
            "Contra",
            CancellationToken.None);
        LauncherContentVersion secondRead = await harness.Service.GetRepositoryModificationMetadataAsync(
            "Contra",
            CancellationToken.None);

        secondRead.Should().BeSameAs(firstRead);
        harness.YamlReader.GetReadCount<LegacyContentManifest>(modUri).Should().Be(1);
    }

    /// <summary>
    ///     Image caching runs a bounded number of downloads at a time so a first start does not saturate the
    ///     connection. The bound limits how many run at once, not how many are cached.
    /// </summary>
    [Fact]
    public async Task InitDataAsync_CachesImagesForMoreModificationsThanItDownloadsConcurrentlyAsync()
    {
        using var harness = new CatalogTestHarness();
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var catalogReferences = new List<LegacyCatalogModificationReference>();
        var installedVersions = new List<LauncherContentVersion>();
        for (int index = 0; index < 12; index++)
        {
            var modUri = new Uri($"https://example.test/mod-{index}.yaml");
            harness.YamlReader.SetResult(modUri, new LegacyContentManifest
            {
                ModificationType = ModificationType.Mod,
                Name = $"Mod {index}",
                Version = "1.0",
                UIImageSourceLink = $"https://cdn.example.test/mod-{index}.png"
            });
            catalogReferences.Add(new LegacyCatalogModificationReference
            {
                ModName = $"Mod {index}",
                ModLink = modUri.ToString()
            });
            installedVersions.Add(TestLauncherContent.Version($"Mod {index}", "1.0", installed: true));
        }

        harness.StateStore.StateToLoad = CreateState([.. installedVersions]);
        harness.LocalContent.InstalledVersions = installedVersions;
        harness.YamlReader.SetResult(
            manifestUri,
            new LegacyLauncherCatalogDocument { modDatas = catalogReferences });

        await harness.Service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, harness.Paths),
            CancellationToken.None);

        harness.AssetDownloader.Calls.Should().HaveCount(12);
    }

    private static LauncherContentState CreateState(params LauncherContentVersion[] versions)
    {
        var data = new LauncherData();
        foreach (LauncherContentVersion version in versions)
        {
            data.AddOrUpdate(version);
        }

        return LauncherContentStateMapper.ToLauncherContentState(data);
    }

    private static LegacyContentManifest CreateRemoteVersion(
        string name,
        string version,
        ModificationType modificationType,
        string parentContentName = "")
    {
        return new LegacyContentManifest
        {
            ModificationType = modificationType,
            Name = name,
            Version = version,
            DependenceName = parentContentName
        };
    }

    private static IReadOnlyList<LauncherContentVersion> GetVersions(LauncherContentState state)
    {
        var launcherData = LauncherContentStateMapper.ToLauncherData(state);
        return launcherData.Modifications
            .Concat(launcherData.Patches)
            .Concat(launcherData.Addons)
            .SelectMany(modification => modification.Versions)
            .ToList();
    }
}

/// <summary>
///     Owns one catalog session: its temporary directory, its collaborators, and the service built from them, so a
///     test arranges only the part it is actually about.
/// </summary>
/// <remarks>
///     The service is built on first use, which is what lets a test finish configuring the stubs it needs first. One
///     palette cache instance is shared between the image cache and the catalog, as in production: what the download
///     path writes is what a later offline start reads back.
/// </remarks>
file sealed class CatalogTestHarness : IDisposable
{
    private readonly TestDirectory _directory = new();

    private readonly Lazy<LauncherPaths> _paths;

    private readonly Lazy<LauncherContentCatalogService> _service;

    public CatalogTestHarness()
    {
        _paths = new Lazy<LauncherPaths>(() => TestLauncherPaths.Create(_directory));
        _service = new Lazy<LauncherContentCatalogService>(BuildService);
    }

    public RecordingLauncherContentStateStore StateStore { get; } = new();

    public RecordingLocalLauncherContentService LocalContent { get; } = new();

    public StubRemoteYamlDocumentReader YamlReader { get; } = new();

    public RecordingRemoteAssetDownloader AssetDownloader { get; } = new();

    public IModificationThemeCache ThemeCache { get; } = new FakeModificationThemeCache();

    public LauncherPaths Paths => _paths.Value;

    public LauncherContentCatalogService Service => _service.Value;

    public void Dispose()
    {
        _directory.Dispose();
    }

    /// <summary>
    ///     Builds both supported games from one storage root, which is the only arrangement a game switch is valid in.
    /// </summary>
    public (LauncherPaths Generals, LauncherPaths ZeroHour) CreateBothGamePaths()
    {
        (_, LauncherPaths generalsPaths, LauncherPaths zeroHourPaths) =
            TestLauncherPaths.CreateTwoGameRuntime(_directory);
        return (generalsPaths, zeroHourPaths);
    }

    private LauncherContentCatalogService BuildService()
    {
        return new LauncherContentCatalogService(
            StateStore,
            new RemoteLauncherCatalogClient(
                YamlReader,
                NullLogger<RemoteLauncherCatalogClient>.Instance),
            new LauncherCatalogImageCache(
                AssetDownloader,
                ThemeCache,
                NullLogger<LauncherCatalogImageCache>.Instance),
            new LauncherLocalContentReconciler(
                LocalContent,
                NullLogger<LauncherLocalContentReconciler>.Instance),
            ThemeCache,
            NullLogger<LauncherContentCatalogService>.Instance);
    }
}
