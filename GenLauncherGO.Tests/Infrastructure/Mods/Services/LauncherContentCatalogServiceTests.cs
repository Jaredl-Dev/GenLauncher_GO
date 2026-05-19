using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Exceptions;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Mods.Contracts;
using GenLauncherGO.Infrastructure.Mods.Models;
using GenLauncherGO.Infrastructure.Mods.Services;
using GenLauncherGO.Infrastructure.Remote.Contracts;
using GenLauncherGO.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Mods.Services;

public sealed class LauncherContentCatalogServiceTests
{
    [Fact]
    public async Task InitDataAsyncWithDisconnectedCatalogLoadsOnlyLocalStateAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var stateStore = new StubLauncherContentStateStore();
        var localContentService = new RecordingLocalLauncherContentService();
        var yamlReader = new StubRemoteYamlDocumentReader();
        var assetDownloader = new RecordingRemoteAssetDownloader();
        LauncherContentCatalogService service = CreateService(
            stateStore,
            localContentService,
            yamlReader,
            assetDownloader);
        stateStore.StateToLoad = new LauncherContentState
        {
            Modifications = new List<LauncherContentEntryState>
            {
                new LauncherContentEntryState
                {
                    Name = "ShockWave",
                    ModificationVersions = new List<LauncherContentVersionState>
                    {
                        new LauncherContentVersionState
                        {
                            Name = "ShockWave",
                            Version = "1.0",
                            Installed = true
                        }
                    }
                }
            }
        };
        localContentService.InstalledVersions = new List<LauncherContentVersion>
            {
                new LauncherContentVersion
                {
                    Installation = new LauncherContentInstallation { Installed = true },
                    ModificationType = ModificationType.Mod,
                    Name = "ShockWave",
                    Version = "1.0",
                }
            };

        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, paths),
            CancellationToken.None);
        await service.ReadPatchesAndAddonsForModAsync(LauncherContentKey.ForModificationName("ShockWave"), CancellationToken.None);

        service.Data.Modifications.Select(modification => modification.Name).Should().Equal("ShockWave");
        service.RepositoryModificationNames.Should().BeNull();
        yamlReader.GetReadCount<LegacyLauncherCatalogDocument>().Should().Be(0);
    }

    [Fact]
    public async Task InitDataAsyncSwitchesGameNamespaceAndClearsPreviouslyCachedContentAsync()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        var storagePaths = new LauncherStoragePaths(executableDirectory);
        LauncherPaths generalsPaths = storagePaths.CreateGamePaths(
            SupportedGame.Generals,
            directory.CreateDirectory("GeneralsGame"));
        LauncherPaths zeroHourPaths = storagePaths.CreateGamePaths(
            SupportedGame.ZeroHour,
            directory.CreateDirectory("ZeroHourGame"));
        var stateStore = new StubLauncherContentStateStore();
        var localContentService = new RecordingLocalLauncherContentService();
        LauncherContentCatalogService service = CreateService(
            stateStore,
            localContentService,
            new StubRemoteYamlDocumentReader(),
            new RecordingRemoteAssetDownloader());
        stateStore.StatesToLoadByGame[SupportedGame.Generals] =
            CreateSingleInstalledModificationState("Shared Mod", "Generals Version");
        stateStore.StatesToLoadByGame[SupportedGame.ZeroHour] =
            CreateSingleInstalledModificationState("Shared Mod", "Zero Hour Version");
        localContentService.InstalledVersions = new List<LauncherContentVersion>
        {
            CreateInstalledModificationVersion("Shared Mod", "Generals Version"),
        };

        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, generalsPaths),
            CancellationToken.None);
        service.Data.Modifications.Should().ContainSingle()
            .Which.Versions.Should().ContainSingle()
            .Which.Version.Should().Be("Generals Version");

        localContentService.InstalledVersions = new List<LauncherContentVersion>
        {
            CreateInstalledModificationVersion("Shared Mod", "Zero Hour Version"),
        };
        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, zeroHourPaths),
            CancellationToken.None);

        service.Data.Modifications.Should().ContainSingle()
            .Which.Versions.Should().ContainSingle()
            .Which.Version.Should().Be("Zero Hour Version");
        stateStore.LoadedPaths.Should().Equal(generalsPaths, zeroHourPaths);
    }

    [Fact]
    public async Task InitDataAsyncRestoresPreviousGameCatalogWhenSwitchInitializationFailsAsync()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        var storagePaths = new LauncherStoragePaths(executableDirectory);
        LauncherPaths generalsPaths = storagePaths.CreateGamePaths(
            SupportedGame.Generals,
            directory.CreateDirectory("GeneralsGame"));
        LauncherPaths zeroHourPaths = storagePaths.CreateGamePaths(
            SupportedGame.ZeroHour,
            directory.CreateDirectory("ZeroHourGame"));
        var manifestUri = new Uri("https://example.test/unavailable.yaml");
        var stateStore = new StubLauncherContentStateStore();
        var localContentService = new RecordingLocalLauncherContentService
        {
            InstalledVersions = new List<LauncherContentVersion>
            {
                CreateInstalledModificationVersion("Shared Mod", "Generals Version"),
            },
        };
        var yamlReader = new StubRemoteYamlDocumentReader();
        yamlReader.SetException<LegacyLauncherCatalogDocument>(
            manifestUri,
            new IOException("Catalog unavailable."));
        LauncherContentCatalogService service = CreateService(
            stateStore,
            localContentService,
            yamlReader,
            new RecordingRemoteAssetDownloader());
        stateStore.StatesToLoadByGame[SupportedGame.Generals] =
            CreateSingleInstalledModificationState("Shared Mod", "Generals Version");

        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, generalsPaths),
            CancellationToken.None);
        Func<Task> switchGame = () => service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, zeroHourPaths),
            CancellationToken.None);

        await switchGame.Should().ThrowAsync<IOException>();
        service.Data.Modifications.Should().ContainSingle()
            .Which.Versions.Should().ContainSingle()
            .Which.Version.Should().Be("Generals Version");

        service.SaveLauncherData();
        stateStore.SavedPaths.Should().ContainSingle().Which.Should().Be(generalsPaths);
    }

    [Fact]
    public async Task InitDataAsyncReadsRemoteCatalogForInstalledModsAndDownloadsImagesAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var modUri = new Uri("https://example.test/shockwave.yaml");
        var cardImageUri = new Uri("https://cdn.example.test/shockwave.jpg");
        var stateStore = new StubLauncherContentStateStore();
        var localContentService = new RecordingLocalLauncherContentService();
        var yamlReader = new StubRemoteYamlDocumentReader();
        var assetDownloader = new RecordingRemoteAssetDownloader();
        LauncherContentCatalogService service = CreateService(
            stateStore,
            localContentService,
            yamlReader,
            assetDownloader);

        stateStore.StateToLoad = new LauncherContentState
        {
            Modifications = new List<LauncherContentEntryState>
            {
                new LauncherContentEntryState
                {
                    Name = "ShockWave",
                    Installed = true,
                    ModificationVersions = new List<LauncherContentVersionState>
                    {
                        new LauncherContentVersionState
                        {
                            Name = "ShockWave",
                            Version = "1.0",
                            Installed = true
                        }
                    }
                }
            }
        };
        localContentService.InstalledVersions = new List<LauncherContentVersion>
            {
                new LauncherContentVersion
                {
                    Installation = new LauncherContentInstallation { Installed = true },
                    ModificationType = ModificationType.Mod,
                    Name = "ShockWave",
                    Version = "1.0",
                }
            };
        yamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
        {
            modDatas = new List<LegacyCatalogModificationReference>
                {
                    new LegacyCatalogModificationReference
                    {
                        ModName = "ShockWave",
                        ModLink = modUri.ToString()
                    }
                }
        });
        yamlReader.SetResult(modUri, new LegacyContentManifest
        {
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = "1.2",
            UIImageSourceLink = cardImageUri.ToString(),
        });

        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, paths),
            CancellationToken.None);

        service.RepositoryModificationNames.Should().Equal("ShockWave");
        LauncherContent mod = service.Data.Modifications
            .Should()
            .ContainSingle(item => item.Name == "ShockWave")
            .Subject;
        mod.Versions.Should().Contain(version => version.Version == "1.2");
        assetDownloader.Calls.Should().Equal(
            (cardImageUri, paths.GetModificationImageFilePath("ShockWave", "1.2.jpg")));
    }

    [Fact]
    public async Task InitDataAsyncLoadsSelectedModPatchesAndAddonsAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var modUri = new Uri("https://example.test/shockwave.yaml");
        var patchUri = new Uri("https://example.test/patch.yaml");
        var addonUri = new Uri("https://example.test/addon.yaml");
        var stateStore = new StubLauncherContentStateStore();
        var localContentService = new RecordingLocalLauncherContentService();
        var yamlReader = new StubRemoteYamlDocumentReader();
        var assetDownloader = new RecordingRemoteAssetDownloader();
        LauncherContentCatalogService service = CreateService(
            stateStore,
            localContentService,
            yamlReader,
            assetDownloader);

        stateStore.StateToLoad = new LauncherContentState
        {
            Modifications = new List<LauncherContentEntryState>
            {
                new LauncherContentEntryState
                {
                    Name = "ShockWave",
                    IsSelected = true,
                    ModificationVersions = new List<LauncherContentVersionState>
                    {
                        new LauncherContentVersionState
                        {
                            Name = "ShockWave",
                            Version = "1.0",
                            Installed = true,
                            IsSelected = true
                        }
                    }
                }
            }
        };
        localContentService.InstalledVersions = new List<LauncherContentVersion>
            {
                new LauncherContentVersion
                {
                    Installation = new LauncherContentInstallation { Installed = true },
                    ModificationType = ModificationType.Mod,
                    Name = "ShockWave",
                    Version = "1.0",
                }
            };
        yamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
        {
            modDatas = new List<LegacyCatalogModificationReference>
                {
                    new LegacyCatalogModificationReference
                    {
                        ModName = "ShockWave",
                        ModLink = modUri.ToString(),
                        ModPatches = new List<string> { patchUri.ToString() },
                        ModAddons = new List<string> { addonUri.ToString() }
                    }
                }
        });
        yamlReader.SetResult(
            modUri,
            CreateRemoteVersion("ShockWave", "1.2", ModificationType.Mod));
        yamlReader.SetResult(
            patchUri,
            CreateRemoteVersion("Balance", "2.0", ModificationType.Patch, "ShockWave"));
        yamlReader.SetResult(
            addonUri,
            CreateRemoteVersion("HD", "1.0", ModificationType.Addon, "ShockWave"));

        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, paths),
            CancellationToken.None);
        await service.ReadPatchesAndAddonsForModAsync(LauncherContentKey.ForModificationName("ShockWave"), CancellationToken.None);

        service.Data.Patches.SelectMany(patch => patch.Versions).Should()
            .ContainSingle(version => version.Name == "Balance" && version.Version == "2.0");
        service.Data.Addons.SelectMany(addon => addon.Versions).Should()
            .ContainSingle(version => version.Name == "HD" && version.Version == "1.0");
        yamlReader.GetReadCount<LegacyContentManifest>(patchUri).Should().Be(1);
        yamlReader.GetReadCount<LegacyContentManifest>(addonUri).Should().Be(1);
    }

    [Fact]
    public async Task ReadPatchesAndAddonsForModAsyncRetriesAfterPartialLoadFailureAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var modUri = new Uri("https://example.test/shockwave.yaml");
        var patchUri = new Uri("https://example.test/patch.yaml");
        var stateStore = new StubLauncherContentStateStore();
        var localContentService = new RecordingLocalLauncherContentService();
        var yamlReader = new StubRemoteYamlDocumentReader();
        LauncherContentCatalogService service = CreateService(
            stateStore,
            localContentService,
            yamlReader,
            new RecordingRemoteAssetDownloader());

        stateStore.StateToLoad = new LauncherContentState();
        localContentService.InstalledVersions = Array.Empty<LauncherContentVersion>();
        yamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
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
        yamlReader.SetResult(
            modUri,
            CreateRemoteVersion("ShockWave", "1.2", ModificationType.Mod));
        yamlReader.SetHandler<LegacyContentManifest>(
            patchUri,
            (callIndex, _) => callIndex == 1
                ? Task.FromException<LegacyContentManifest>(new IOException("Temporary failure."))
                : Task.FromResult(CreateRemoteVersion(
                    "Balance",
                    "2.0",
                    ModificationType.Patch,
                    "ShockWave")));

        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, paths),
            CancellationToken.None);
        await service.AddRepositoryModificationAsync("ShockWave", CancellationToken.None);
        var modification = LauncherContentKey.ForModificationName("ShockWave");

        await service.ReadPatchesAndAddonsForModAsync(modification, CancellationToken.None);
        await service.ReadPatchesAndAddonsForModAsync(modification, CancellationToken.None);
        await service.ReadPatchesAndAddonsForModAsync(modification, CancellationToken.None);

        service.Data.Patches.SelectMany(patch => patch.Versions).Should()
            .ContainSingle(version => version.Name == "Balance" && version.Version == "2.0");
        yamlReader.GetReadCount<LegacyContentManifest>(patchUri).Should().Be(2);
    }

    [Fact]
    public async Task ReadPatchesAndAddonsForModAsyncCoalescesConcurrentLoadsAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var modUri = new Uri("https://example.test/shockwave.yaml");
        var patchUri = new Uri("https://example.test/patch.yaml");
        var stateStore = new StubLauncherContentStateStore();
        var localContentService = new RecordingLocalLauncherContentService();
        var yamlReader = new StubRemoteYamlDocumentReader();
        LauncherContentCatalogService service = CreateService(
            stateStore,
            localContentService,
            yamlReader,
            new RecordingRemoteAssetDownloader());
        var patchReadStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePatchRead =
            new TaskCompletionSource<LegacyContentManifest>(TaskCreationOptions.RunContinuationsAsynchronously);

        stateStore.StateToLoad = new LauncherContentState();
        localContentService.InstalledVersions = Array.Empty<LauncherContentVersion>();
        yamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
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
        yamlReader.SetResult(
            modUri,
            CreateRemoteVersion("ShockWave", "1.2", ModificationType.Mod));
        yamlReader.SetHandler<LegacyContentManifest>(
            patchUri,
            (_, _) =>
            {
                patchReadStarted.TrySetResult(true);
                return releasePatchRead.Task;
            });

        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, paths),
            CancellationToken.None);
        await service.AddRepositoryModificationAsync("ShockWave", CancellationToken.None);
        var modification = LauncherContentKey.ForModificationName("ShockWave");

        Task firstLoad = service.ReadPatchesAndAddonsForModAsync(modification, CancellationToken.None);
        await patchReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task secondLoad = service.ReadPatchesAndAddonsForModAsync(modification, CancellationToken.None);
        releasePatchRead.SetResult(
            CreateRemoteVersion("Balance", "2.0", ModificationType.Patch, "ShockWave"));
        await Task.WhenAll(firstLoad, secondLoad);

        service.Data.Patches.SelectMany(patch => patch.Versions).Should()
            .ContainSingle(version => version.Name == "Balance" && version.Version == "2.0");
        yamlReader.GetReadCount<LegacyContentManifest>(patchUri).Should().Be(1);
    }

    [Fact]
    public async Task ReadOriginalGameAddonsAndPatchesAsyncLoadsChildContentOnceAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var patchUri = new Uri("https://example.test/original-patch.yaml");
        var addonUri = new Uri("https://example.test/original-addon.yaml");
        var stateStore = new StubLauncherContentStateStore();
        var localContentService = new RecordingLocalLauncherContentService();
        var yamlReader = new StubRemoteYamlDocumentReader();
        var assetDownloader = new RecordingRemoteAssetDownloader();
        LauncherContentCatalogService service = CreateService(
            stateStore,
            localContentService,
            yamlReader,
            assetDownloader);

        stateStore.StateToLoad = new LauncherContentState();
        localContentService.InstalledVersions = Array.Empty<LauncherContentVersion>();
        yamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
        {
            originalGamePatches = new List<string> { patchUri.ToString() },
            originalGameAddons = new List<string> { addonUri.ToString() }
        });
        yamlReader.SetResult(
            patchUri,
            CreateRemoteVersion("GenPatcher", "1.0", ModificationType.Patch));
        yamlReader.SetResult(
            addonUri,
            CreateRemoteVersion("ControlBar", "1.0", ModificationType.Addon));

        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, paths),
            CancellationToken.None);
        await service.ReadOriginalGameAddonsAndPatchesAsync(CancellationToken.None);
        await service.ReadOriginalGameAddonsAndPatchesAsync(CancellationToken.None);
        service.UpdateLocalModificationsData();

        service.Data.Patches.SelectMany(patch => patch.Versions).Should()
            .ContainSingle(version => version.Name == "GenPatcher");
        service.Data.Addons.SelectMany(addon => addon.Versions).Should()
            .ContainSingle(version => version.Name == "ControlBar");
        yamlReader.GetReadCount<LegacyContentManifest>(patchUri).Should().Be(1);
        yamlReader.GetReadCount<LegacyContentManifest>(addonUri).Should().Be(1);
    }

    [Fact]
    public async Task ReadOriginalGameAddonsAndPatchesAsyncRetriesAfterPartialLoadFailureAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var patchUri = new Uri("https://example.test/original-patch.yaml");
        var stateStore = new StubLauncherContentStateStore();
        var localContentService = new RecordingLocalLauncherContentService();
        var yamlReader = new StubRemoteYamlDocumentReader();
        LauncherContentCatalogService service = CreateService(
            stateStore,
            localContentService,
            yamlReader,
            new RecordingRemoteAssetDownloader());

        stateStore.StateToLoad = new LauncherContentState();
        localContentService.InstalledVersions = Array.Empty<LauncherContentVersion>();
        yamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
        {
            originalGamePatches = { patchUri.ToString() }
        });
        yamlReader.SetHandler<LegacyContentManifest>(
            patchUri,
            (callIndex, _) => callIndex == 1
                ? Task.FromException<LegacyContentManifest>(new IOException("Temporary failure."))
                : Task.FromResult(CreateRemoteVersion("GenPatcher", "1.0", ModificationType.Patch)));

        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, paths),
            CancellationToken.None);

        await service.ReadOriginalGameAddonsAndPatchesAsync(CancellationToken.None);
        await service.ReadOriginalGameAddonsAndPatchesAsync(CancellationToken.None);
        await service.ReadOriginalGameAddonsAndPatchesAsync(CancellationToken.None);

        service.Data.Patches.SelectMany(patch => patch.Versions).Should()
            .ContainSingle(version => version.Name == "GenPatcher");
        yamlReader.GetReadCount<LegacyContentManifest>(patchUri).Should().Be(2);
    }

    [Fact]
    public async Task ReadOriginalGameAddonsAndPatchesAsyncReturnsWhenCatalogIsDisconnectedAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var stateStore = new StubLauncherContentStateStore();
        var localContentService = new RecordingLocalLauncherContentService();
        var yamlReader = new StubRemoteYamlDocumentReader();
        LauncherContentCatalogService service = CreateService(
            stateStore,
            localContentService,
            yamlReader,
            new RecordingRemoteAssetDownloader());

        stateStore.StateToLoad = new LauncherContentState();
        localContentService.InstalledVersions = Array.Empty<LauncherContentVersion>();
        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, paths),
            CancellationToken.None);

        await service.ReadOriginalGameAddonsAndPatchesAsync(CancellationToken.None);

        yamlReader.GetReadCount<LegacyContentManifest>().Should().Be(0);
    }

    [Fact]
    public async Task ReadPatchesAndAddonsForModAsyncReturnsWhenManifestLookupIsMissingAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var stateStore = new StubLauncherContentStateStore();
        var localContentService = new RecordingLocalLauncherContentService();
        var yamlReader = new StubRemoteYamlDocumentReader();
        LauncherContentCatalogService service = CreateService(
            stateStore,
            localContentService,
            yamlReader,
            new RecordingRemoteAssetDownloader());

        stateStore.StateToLoad = new LauncherContentState();
        localContentService.InstalledVersions = Array.Empty<LauncherContentVersion>();
        yamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument());

        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, paths),
            CancellationToken.None);

        await service.ReadPatchesAndAddonsForModAsync(LauncherContentKey.ForModificationName("Missing"), CancellationToken.None);

        yamlReader.GetReadCount<LegacyContentManifest>().Should().Be(0);
    }

    [Fact]
    public async Task InitDataAsyncDownloadsAdvertisingMetadataAndImagesAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var advertisingUri = new Uri("https://example.test/advertising.yaml");
        var imageUri = new Uri("https://cdn.example.test/advertising.jpg");
        var stateStore = new StubLauncherContentStateStore();
        var localContentService = new RecordingLocalLauncherContentService();
        var yamlReader = new StubRemoteYamlDocumentReader();
        var assetDownloader = new RecordingRemoteAssetDownloader();
        LauncherContentCatalogService service = CreateService(
            stateStore,
            localContentService,
            yamlReader,
            assetDownloader);

        stateStore.StateToLoad = new LauncherContentState();
        localContentService.InstalledVersions = Array.Empty<LauncherContentVersion>();
        yamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
        {
            AdvData = new List<LegacyCatalogAdvertisingReference>
                {
                    new LegacyCatalogAdvertisingReference
                    {
                        ModName = "RiseOfTheReds",
                        ModLink = advertisingUri.ToString(),
                        ImagesData = new List<string> { imageUri.ToString() }
                    }
                }
        });
        yamlReader.SetResult(advertisingUri, new LegacyContentManifest
        {
            ModificationType = ModificationType.Advertising,
            Name = "RiseOfTheReds",
            Version = "1.87"
        });

        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, paths),
            CancellationToken.None);

        LauncherContentVersion? advertising = service.Advertising;
        advertising.Should().NotBeNull();
        advertising!.Name.Should().Be("RiseOfTheReds");
        advertising.Version.Should().Be("1.87");
        assetDownloader.Calls.Should().ContainSingle(call =>
            call.SourceUri == imageUri &&
            call.DestinationFilePath ==
            paths.GetModificationImageFilePath("RiseOfTheReds", "0.jpg"));
    }

    [Fact]
    public async Task InitDataAsyncLeavesAdvertisingEmptyWhenManifestDownloadFailsAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var advertisingUri = new Uri("https://example.test/advertising.yaml");
        var stateStore = new StubLauncherContentStateStore();
        var localContentService = new RecordingLocalLauncherContentService();
        var yamlReader = new StubRemoteYamlDocumentReader();
        var assetDownloader = new RecordingRemoteAssetDownloader();
        LauncherContentCatalogService service = CreateService(
            stateStore,
            localContentService,
            yamlReader,
            assetDownloader);

        stateStore.StateToLoad = new LauncherContentState();
        localContentService.InstalledVersions = Array.Empty<LauncherContentVersion>();
        yamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
        {
            AdvData = new List<LegacyCatalogAdvertisingReference>
                {
                    new LegacyCatalogAdvertisingReference
                    {
                        ModName = "RiseOfTheReds",
                        ModLink = advertisingUri.ToString(),
                        ImagesData = new List<string> { "https://cdn.example.test/advertising.jpg" }
                    }
                }
        });
        yamlReader.SetException<LegacyContentManifest>(
            advertisingUri,
            new IOException("Manifest unavailable."));

        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, paths),
            CancellationToken.None);

        service.Advertising.Should().BeNull();
        assetDownloader.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task PersistedSelectionStateIsLoadedAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var stateStore = new StubLauncherContentStateStore();
        var localContentService = new RecordingLocalLauncherContentService();
        LauncherContentCatalogService service = CreateService(
            stateStore,
            localContentService,
            new StubRemoteYamlDocumentReader(),
            new RecordingRemoteAssetDownloader());

        LauncherContentState catalogState = new()
        {
            Modifications = new List<LauncherContentEntryState>
            {
                CreateEntry(
                    ModificationType.Mod,
                    "ShockWave",
                    parentContentName: string.Empty,
                    isSelected: true,
                    version: "1.2",
                    versionSelected: true),
                CreateEntry(
                    ModificationType.Mod,
                    "Contra",
                    parentContentName: string.Empty,
                    isSelected: false,
                    version: "009",
                    versionSelected: false)
            },
            Patches = new List<LauncherContentEntryState>
            {
                CreateEntry(
                    ModificationType.Patch,
                    "BalancePatch",
                    parentContentName: "ShockWave",
                    isSelected: true,
                    version: "2.0",
                    versionSelected: true)
            },
            Addons = new List<LauncherContentEntryState>
            {
                CreateEntry(
                    ModificationType.Addon,
                    "HDTextures",
                    parentContentName: "ShockWave",
                    isSelected: true,
                    version: "1.0",
                    versionSelected: true),
                CreateEntry(
                    ModificationType.Addon,
                    "PatchAddon",
                    parentContentName: "BalancePatch",
                    isSelected: true,
                    version: "1.1",
                    versionSelected: true)
            }
        };
        stateStore.StateToLoad = catalogState;
        localContentService.InstalledVersions = GetVersions(catalogState);

        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, paths),
            CancellationToken.None);

        LauncherContent selectedModification = service.Data.Modifications.Single(modification =>
            modification.IsSelected);
        LauncherContent selectedPatch = service.Data.Patches.Single(patch => patch.IsSelected);
        selectedModification.Versions.Should().ContainSingle(version =>
            version.Name == "ShockWave" && version.Version == "1.2" && version.Installation.IsSelected);
        selectedPatch.Versions.Should().ContainSingle(version =>
            version.Name == "BalancePatch" && version.Version == "2.0" && version.Installation.IsSelected);
        service.Data.GetPatchesFor(selectedModification)
            .Should().ContainSingle(patch => patch.Name == "BalancePatch");
        service.Data.GetAddonsFor(selectedModification, selectedPatch)
            .Should().Contain(addon => addon.Name == "HDTextures")
            .And.Contain(addon => addon.Name == "PatchAddon");
        service.Data.Addons.Should().OnlyContain(addon =>
            addon.IsSelected &&
            addon.Versions.Count(version => version.Installation.IsSelected) == 1);
        service.Data.GetAllModsVersionsList().Should().HaveCount(2);
    }

    [Fact]
    public async Task UninstallVersionDeletesLocalFilesAndReconcilesCatalogAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var stateStore = new StubLauncherContentStateStore();
        var localContentService = new RecordingLocalLauncherContentService();
        LauncherContentCatalogService service = CreateService(
            stateStore,
            localContentService,
            new StubRemoteYamlDocumentReader(),
            new RecordingRemoteAssetDownloader());
        LauncherContentVersion version = new()
        {
            Installation = new LauncherContentInstallation { Installed = true },
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = "1.0",
        };

        stateStore.StateToLoad = new LauncherContentState();
        localContentService.InstalledVersions = Array.Empty<LauncherContentVersion>();

        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, paths),
            CancellationToken.None);

        service.UninstallVersion(version.ContentKey);

        localContentService.DeletedVersions.Should().ContainSingle(request =>
            request.Paths == paths &&
            request.ContentKey.ContentType == ModificationType.Mod &&
            request.ContentKey.Name == "ShockWave" &&
            request.ContentKey.Version == "1.0");
    }

    [Fact]
    public async Task AddRepositoryModificationAsyncAddsRemoteModAndCachesImagesExactlyOnceAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var modUri = new Uri("https://example.test/contra.yaml");
        var imageUri = new Uri("https://cdn.example.test/contra.png");
        var stateStore = new StubLauncherContentStateStore();
        var localContentService = new RecordingLocalLauncherContentService();
        var yamlReader = new StubRemoteYamlDocumentReader();
        var assetDownloader = new RecordingRemoteAssetDownloader();
        LauncherContentCatalogService service = CreateService(
            stateStore,
            localContentService,
            yamlReader,
            assetDownloader);

        stateStore.StateToLoad = new LauncherContentState();
        localContentService.InstalledVersions = Array.Empty<LauncherContentVersion>();
        yamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
        {
            modDatas = new List<LegacyCatalogModificationReference>
                {
                    new LegacyCatalogModificationReference
                    {
                        ModName = "Contra",
                        ModLink = modUri.ToString()
                    }
                }
        });
        yamlReader.SetResult(modUri, new LegacyContentManifest
        {
            ModificationType = ModificationType.Mod,
            Name = "Contra",
            Version = "009",
            UIImageSourceLink = imageUri.ToString()
        });

        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, paths),
            CancellationToken.None);

        LauncherContentVersion downloadedVersion = await service.AddRepositoryModificationAsync(
            "Contra",
            CancellationToken.None);

        downloadedVersion.Name.Should().Be("Contra");
        downloadedVersion.Version.Should().Be("009");
        LauncherContent addedModification = service.Data.Modifications.Should().ContainSingle().Subject;
        addedModification.Name.Should().Be("Contra");
        addedModification.Versions.Should().ContainSingle().Which.Should().BeSameAs(downloadedVersion);
        assetDownloader.Calls.Should().ContainSingle(call =>
            call.SourceUri == imageUri &&
            call.DestinationFilePath == paths.GetModificationImageFilePath("Contra", "009.png"));
    }

    [Fact]
    public async Task InitDataAsyncWaitsForOldGameMetadataLoadBeforeSwitchingCatalogAsync()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        var storagePaths = new LauncherStoragePaths(executableDirectory);
        LauncherPaths generalsPaths = storagePaths.CreateGamePaths(
            SupportedGame.Generals,
            directory.CreateDirectory("GeneralsGame"));
        LauncherPaths zeroHourPaths = storagePaths.CreateGamePaths(
            SupportedGame.ZeroHour,
            directory.CreateDirectory("ZeroHourGame"));
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var modUri = new Uri("https://example.test/contra.yaml");
        var imageUri = new Uri("https://cdn.example.test/contra.png");
        var stateStore = new StubLauncherContentStateStore();
        var yamlReader = new StubRemoteYamlDocumentReader();
        var assetDownloader = new RecordingRemoteAssetDownloader();
        var metadataReadStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMetadataRead = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        yamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
        {
            modDatas =
            {
                new LegacyCatalogModificationReference
                {
                    ModName = "Contra",
                    ModLink = modUri.ToString(),
                },
            },
        });
        yamlReader.SetHandler<LegacyContentManifest>(
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
                    UIImageSourceLink = imageUri.ToString(),
                };
            });
        LauncherContentCatalogService service = CreateService(
            stateStore,
            new RecordingLocalLauncherContentService(),
            yamlReader,
            assetDownloader);

        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, generalsPaths),
            CancellationToken.None);
        Task<LauncherContentVersion> oldGameDownload =
            service.AddRepositoryModificationAsync("Contra", CancellationToken.None);
        await metadataReadStarted.Task;
        Task switchGame = service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, zeroHourPaths),
            CancellationToken.None);

        releaseMetadataRead.SetResult();
        await oldGameDownload;
        await switchGame;

        service.Data.Modifications.Should().BeEmpty();
        assetDownloader.Calls.Should().ContainSingle(call =>
            call.SourceUri == imageUri &&
            call.DestinationFilePath == generalsPaths.GetModificationImageFilePath("Contra", "009.png"));
    }

    [Fact]
    public async Task InitDataAsyncWaitsForDirectMetadataReadBeforeSwitchingCatalogAsync()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        var storagePaths = new LauncherStoragePaths(executableDirectory);
        LauncherPaths generalsPaths = storagePaths.CreateGamePaths(
            SupportedGame.Generals,
            directory.CreateDirectory("GeneralsGame"));
        LauncherPaths zeroHourPaths = storagePaths.CreateGamePaths(
            SupportedGame.ZeroHour,
            directory.CreateDirectory("ZeroHourGame"));
        var manifestUri = new Uri("https://example.test/repos.yaml");
        var modUri = new Uri("https://example.test/contra.yaml");
        var yamlReader = new StubRemoteYamlDocumentReader();
        var metadataReadStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMetadataRead = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        yamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
        {
            modDatas =
            {
                new LegacyCatalogModificationReference
                {
                    ModName = "Contra",
                    ModLink = modUri.ToString(),
                },
            },
        });
        yamlReader.SetHandler<LegacyContentManifest>(
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
                };
            });
        LauncherContentCatalogService service = CreateService(
            new StubLauncherContentStateStore(),
            new RecordingLocalLauncherContentService(),
            yamlReader,
            new RecordingRemoteAssetDownloader());

        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(manifestUri, generalsPaths),
            CancellationToken.None);
        Task<LauncherContentVersion> oldGameMetadata = service.GetRepositoryModificationMetadataAsync(
            "Contra",
            CancellationToken.None);
        await metadataReadStarted.Task;
        Task switchGame = service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, zeroHourPaths),
            CancellationToken.None);

        bool switchCompletedBeforeMetadataRead = switchGame.IsCompleted;
        releaseMetadataRead.SetResult();
        LauncherContentVersion metadata = await oldGameMetadata;
        await switchGame;

        switchCompletedBeforeMetadataRead.Should().BeFalse();
        metadata.Version.Should().Be("009");
        service.Data.Modifications.Should().BeEmpty();
        Func<Task> readOldMetadataFromNewSession = () => service.GetRepositoryModificationMetadataAsync(
            "Contra",
            CancellationToken.None);
        await readOldMetadataFromNewSession.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DiscardVersionDeletesFolderAndCatalogVersionAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var stateStore = new StubLauncherContentStateStore();
        var localContentService = new RecordingLocalLauncherContentService();
        LauncherContentCatalogService service = CreateService(
            stateStore,
            localContentService,
            new StubRemoteYamlDocumentReader(),
            new RecordingRemoteAssetDownloader());
        LauncherContentVersion version = new()
        {
            Installation = new LauncherContentInstallation { Installed = true },
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = "1.0",
        };
        stateStore.StateToLoad = CreateState(version);
        localContentService.InstalledVersions = [version];

        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, paths),
            CancellationToken.None);
        localContentService.InstalledVersions = [];

        service.DiscardVersion(version.ContentKey);

        service.Data.Modifications.Select(modification => modification.Name).Should().NotContain("ShockWave");
        localContentService.DeletedVersions.Should().ContainSingle(request =>
            request.Paths == paths &&
            request.ContentKey.ContentType == ModificationType.Mod &&
            request.ContentKey.Name == "ShockWave" &&
            request.ContentKey.Version == "1.0");
    }

    [Fact]
    public async Task DiscardContentDeletesEveryVersionFolderAndCatalogEntryAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var stateStore = new StubLauncherContentStateStore();
        var localContentService = new RecordingLocalLauncherContentService();
        LauncherContentCatalogService service = CreateService(
            stateStore,
            localContentService,
            new StubRemoteYamlDocumentReader(),
            new RecordingRemoteAssetDownloader());
        LauncherContentVersion version = new()
        {
            Installation = new LauncherContentInstallation { Installed = true },
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = "1.0",
        };
        LauncherContentVersion secondVersion = new()
        {
            Installation = new LauncherContentInstallation { Installed = true },
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = "2.0",
        };
        stateStore.StateToLoad = CreateState(version, secondVersion);
        localContentService.InstalledVersions = [version, secondVersion];

        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, paths),
            CancellationToken.None);
        localContentService.InstalledVersions = [];

        service.DiscardContent(version.ContentKey);

        service.Data.Modifications.Select(modification => modification.Name).Should().NotContain("ShockWave");
        localContentService.DeletedContents.Should().ContainSingle(request =>
            request.Paths == paths &&
            request.ContentKey.ContentType == ModificationType.Mod &&
            request.ContentKey.Name == "ShockWave" &&
            request.ContentKey.Version == "1.0");
        localContentService.DeletedVersions.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveLauncherDataPersistsInstalledAndAddedRepositoryModsAsync()
    {
        using var directory = new TestDirectory();
        var stateStore = new StubLauncherContentStateStore();
        var localContentService = new RecordingLocalLauncherContentService();
        var yamlReader = new StubRemoteYamlDocumentReader();
        var assetDownloader = new RecordingRemoteAssetDownloader();
        LauncherContentCatalogService service = CreateService(
            stateStore,
            localContentService,
            yamlReader,
            assetDownloader);
        LauncherContentVersion installedVersion = new()
        {
            Installation = new LauncherContentInstallation { Installed = true, IsSelected = true },
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = "1.0",
        };
        LauncherContentVersion repositoryVersion = new()
        {
            Installation = new LauncherContentInstallation
            {
                ContentSourceKind = ContentSourceKind.ManagedSingleFile
            },
            ModificationType = ModificationType.Mod,
            Name = "Contra",
            Version = "2.0"
        };
        stateStore.StateToLoad = CreateState(installedVersion, repositoryVersion);
        localContentService.InstalledVersions = [installedVersion];

        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(
                null,
                CreatePaths(directory.Path)),
            CancellationToken.None);

        service.SaveLauncherData();

        stateStore.SavedStates.Should().ContainSingle(state =>
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
    public async Task PersistedManagedRepositoryModSurvivesDisconnectedCatalogReloadAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        var stateStore = new StubLauncherContentStateStore();
        var localContentService = new RecordingLocalLauncherContentService
        {
            InstalledVersions = []
        };
        LauncherContentCatalogService service = CreateService(
            stateStore,
            localContentService,
            new StubRemoteYamlDocumentReader(),
            new RecordingRemoteAssetDownloader());
        LauncherContentVersion repositoryVersion = new()
        {
            Installation = new LauncherContentInstallation
            {
                ContentSourceKind = ContentSourceKind.ManagedSingleFile
            },
            ModificationType = ModificationType.Mod,
            Name = "Contra",
            Version = "2.0"
        };
        stateStore.StateToLoad = CreateState(repositoryVersion);
        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, paths),
            CancellationToken.None);
        service.SaveLauncherData();
        stateStore.StateToLoad = stateStore.SavedStates.Single();

        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(null, paths),
            CancellationToken.None);

        service.Data.Modifications.Should().ContainSingle()
            .Which.Name.Should().Be("Contra");
    }

    [Fact]
    public async Task SaveLauncherDataPersistsOriginalGameSelectionWithoutModificationCardsAsync()
    {
        using var directory = new TestDirectory();
        var stateStore = new StubLauncherContentStateStore();
        var localContentService = new RecordingLocalLauncherContentService();
        LauncherContentCatalogService service = CreateService(
            stateStore,
            localContentService,
            new StubRemoteYamlDocumentReader(),
            new RecordingRemoteAssetDownloader());
        LauncherContentVersion patch = new()
        {
            Installation = new LauncherContentInstallation { Installed = true, IsSelected = true },
            ModificationType = ModificationType.Patch,
            Name = "Original Patch",
            Version = "1.0",
            ParentContentName = LauncherContentKey.OriginalGame.Name,
        };
        stateStore.StateToLoad = CreateState(patch);
        localContentService.InstalledVersions = [patch];
        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(
                null,
                CreatePaths(directory.Path)),
            CancellationToken.None);
        service.SaveLauncherData();

        LauncherContentState savedState = stateStore.SavedStates.Should().ContainSingle().Subject;
        savedState.Modifications.Should().BeEmpty();
        LauncherContentEntryState savedPatch = savedState.Patches.Should().ContainSingle().Subject;
        savedPatch.Name.Should().Be("Original Patch");
        savedPatch.IsSelected.Should().BeTrue();
        savedPatch.ModificationVersions.Should().ContainSingle().Which.IsSelected.Should().BeTrue();
    }

    [Fact]
    public async Task SaveLauncherDataWhenPersistenceFailsPreservesCatalogForRetryAsync()
    {
        using var directory = new TestDirectory();
        var stateStore = new StubLauncherContentStateStore();
        int saveAttempts = 0;
        stateStore.SaveHandler = _ =>
        {
            saveAttempts++;
            if (saveAttempts == 1)
            {
                throw new IOException("Catalog file is locked.");
            }
        };
        var localContentService = new RecordingLocalLauncherContentService();
        LauncherContentVersion version = new()
        {
            Installation = new LauncherContentInstallation
            {
                Installed = true,
                ContentSourceKind = ContentSourceKind.Manual
            },
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = "1.2",
        };
        stateStore.StateToLoad = CreateState(version);
        localContentService.InstalledVersions = [version];
        LauncherContentCatalogService service = CreateService(
            stateStore,
            localContentService,
            new StubRemoteYamlDocumentReader(),
            new RecordingRemoteAssetDownloader());
        await service.InitDataAsync(
            new LauncherContentCatalogInitializationRequest(
                null,
                CreatePaths(directory.Path)),
            CancellationToken.None);
        Action firstSave = service.SaveLauncherData;

        firstSave.Should().Throw<LauncherContentPersistenceException>()
            .WithInnerException<IOException>();
        service.Data.Modifications.Should().ContainSingle(modification =>
            modification.Name == "ShockWave" &&
            modification.Versions.Single().Installation.Installed);

        service.SaveLauncherData();

        saveAttempts.Should().Be(2);
        stateStore.SavedStates.Should().HaveCount(2);
        stateStore.SavedStates.Should().OnlyContain(state =>
            state.Modifications.Count == 1 &&
            state.Modifications[0].Name == "ShockWave" &&
            state.Modifications[0].ModificationVersions[0].ContentSourceKind == ContentSourceKind.Manual);
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

    private static LauncherContentCatalogService CreateService(
        ILauncherContentStateStore stateStore,
        ILocalLauncherContentService localContentService,
        IRemoteYamlDocumentReader yamlReader,
        IRemoteAssetDownloader assetDownloader)
    {
        return new LauncherContentCatalogService(
            stateStore,
            new RemoteLauncherCatalogClient(
                yamlReader,
                NullLogger<RemoteLauncherCatalogClient>.Instance),
            new LauncherCatalogImageCache(
                assetDownloader,
                NullLogger<LauncherCatalogImageCache>.Instance),
            new LauncherLocalContentReconciler(
                localContentService,
                NullLogger<LauncherLocalContentReconciler>.Instance),
            NullLogger<LauncherContentCatalogService>.Instance);
    }

    private static LauncherPaths CreatePaths(string root)
    {
        return TestLauncherPaths.Create(root);
    }

    private static LauncherContentState CreateSingleInstalledModificationState(
        string name,
        string version)
    {
        return new LauncherContentState
        {
            Modifications =
            {
                CreateEntry(
                    ModificationType.Mod,
                    name,
                    string.Empty,
                    isSelected: false,
                    version,
                    versionSelected: false),
            },
        };
    }

    private static LauncherContentVersion CreateInstalledModificationVersion(
        string name,
        string version)
    {
        return new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation { Installed = true },
            ModificationType = ModificationType.Mod,
            Name = name,
            Version = version,
        };
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

    private static LauncherContentEntryState CreateEntry(
        ModificationType type,
        string name,
        string parentContentName,
        bool isSelected,
        string version,
        bool versionSelected)
    {
        return new LauncherContentEntryState
        {
            ModificationType = type,
            Name = name,
            DependenceName = parentContentName,
            Installed = true,
            IsSelected = isSelected,
            ModificationVersions = new List<LauncherContentVersionState>
            {
                new LauncherContentVersionState
                {
                    ModificationType = type,
                    Name = name,
                    Version = version,
                    DependenceName = parentContentName,
                    Installed = true,
                    IsSelected = versionSelected
                }
            }
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
