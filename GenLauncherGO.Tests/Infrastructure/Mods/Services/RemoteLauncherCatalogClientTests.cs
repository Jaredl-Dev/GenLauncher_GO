using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Infrastructure.Mods.Models;
using GenLauncherGO.Infrastructure.Mods.Services;
using GenLauncherGO.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Mods.Services;

public sealed class RemoteLauncherCatalogClientTests
{
    [Fact]
    public async Task DownloadInstalledModDataAsyncReadsInstalledModsAndPreservesPartialFailuresAsync()
    {
        var yamlReader = new StubRemoteYamlDocumentReader();
        var client = new RemoteLauncherCatalogClient(
            yamlReader,
            NullLogger<RemoteLauncherCatalogClient>.Instance);
        var shockwaveUri = new Uri("https://example.test/shockwave.yaml");
        var brokenUri = new Uri("https://example.test/broken.yaml");
        var contraUri = new Uri("https://example.test/contra.yaml");
        var catalog = new RemoteLauncherCatalog(
            Array.Empty<RemoteAdvertisingReference>(),
            new List<RemoteCatalogModificationReference>
            {
                new("ShockWave", shockwaveUri.ToString(), Array.Empty<string>(), Array.Empty<string>()),
                new("Broken", brokenUri.ToString(), Array.Empty<string>(), Array.Empty<string>()),
                new("Contra", contraUri.ToString(), Array.Empty<string>(), Array.Empty<string>())
            },
            Array.Empty<string>(),
            Array.Empty<string>());
        yamlReader.SetResult(shockwaveUri, new LegacyContentManifest
        {
            Name = "ShockWave",
            Version = "1.2"
        });
        yamlReader.SetException<LegacyContentManifest>(
            brokenUri,
            new InvalidOperationException("Broken manifest"));

        IReadOnlyList<RemoteModificationManifest> result =
            await client.DownloadInstalledModDataAsync(
                catalog,
                new[] { "shockwave", "broken" },
                CancellationToken.None);

        RemoteModificationManifest entry = result.Should().ContainSingle().Subject;
        entry.Content.Name.Should().Be("ShockWave");
        entry.Content.Version.Should().Be("1.2");
        entry.PatchManifestUrls.Should().BeEmpty();
        yamlReader.GetReadCount<LegacyContentManifest>(contraUri).Should().Be(0);
    }

    [Fact]
    public async Task ReadChildManifestsAsyncReturnsSuccessfulChildrenWhenOneChildFailsAsync()
    {
        var yamlReader = new StubRemoteYamlDocumentReader();
        var client = new RemoteLauncherCatalogClient(
            yamlReader,
            NullLogger<RemoteLauncherCatalogClient>.Instance);
        var patchUri = new Uri("https://example.test/patch.yaml");
        var missingUri = new Uri("https://example.test/missing.yaml");
        yamlReader.SetResult(patchUri, new LegacyContentManifest
        {
            Name = "Patch",
            Version = "1.0"
        });
        yamlReader.SetException<LegacyContentManifest>(
            missingUri,
            new InvalidOperationException("Missing manifest"));

        RemoteChildManifestLoadResult result = await client.ReadChildManifestsAsync(
            new[] { patchUri.ToString(), missingUri.ToString() },
            parentContentName: null,
            CancellationToken.None);

        result.ContentVersions.Should().ContainSingle().Which.Name.Should().Be("Patch");
        result.FailedCount.Should().Be(1);
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task ReadCatalogAsyncMapsThirdPartyManifestToNormalizedCatalogAsync()
    {
        var yamlReader = new StubRemoteYamlDocumentReader();
        var client = new RemoteLauncherCatalogClient(
            yamlReader,
            NullLogger<RemoteLauncherCatalogClient>.Instance);
        var manifestUri = new Uri("https://example.test/repos.yaml");
        yamlReader.SetResult(manifestUri, new LegacyLauncherCatalogDocument
        {
            AdvData =
                {
                    new LegacyCatalogAdvertisingReference
                    {
                        ModName = "Featured",
                        ModLink = "https://example.test/featured.yaml",
                        ImagesData = { "https://cdn.example.test/featured.png" }
                    }
                },
            modDatas =
                {
                    new LegacyCatalogModificationReference
                    {
                        ModName = "ShockWave",
                        ModLink = "https://example.test/shockwave.yaml",
                        ModPatches = { "https://example.test/shockwave-patch.yaml" },
                        ModAddons = { "https://example.test/shockwave-addon.yaml" }
                    }
                },
            originalGameAddons = { "https://example.test/original-addon.yaml" },
            originalGamePatches = { "https://example.test/original-patch.yaml" },
            LauncherVersion = "1.2.3"
        });

        RemoteLauncherCatalog catalog = await client.ReadCatalogAsync(manifestUri, CancellationToken.None);

        catalog.AdvertisingEntries.Should().ContainSingle().Which.ImageUrls.Should()
            .ContainSingle("https://cdn.example.test/featured.png");
        catalog.Modifications.Should().ContainSingle().Which.PatchManifestUrls.Should()
            .ContainSingle("https://example.test/shockwave-patch.yaml");
        catalog.OriginalGameAddonManifestUrls.Should().ContainSingle("https://example.test/original-addon.yaml");
        catalog.OriginalGamePatchManifestUrls.Should().ContainSingle("https://example.test/original-patch.yaml");
    }

    [Fact]
    public void GetModificationNamesReturnsCatalogModificationNames()
    {
        var yamlReader = new StubRemoteYamlDocumentReader();
        var client = new RemoteLauncherCatalogClient(
            yamlReader,
            NullLogger<RemoteLauncherCatalogClient>.Instance);
        var catalog = new RemoteLauncherCatalog(
            Array.Empty<RemoteAdvertisingReference>(),
            new List<RemoteCatalogModificationReference>
            {
                new("ShockWave", "https://example.test/shockwave.yaml", Array.Empty<string>(), Array.Empty<string>()),
                new("Contra", "https://example.test/contra.yaml", Array.Empty<string>(), Array.Empty<string>())
            },
            Array.Empty<string>(),
            Array.Empty<string>());

        IReadOnlyList<string> names = client.GetModificationNames(catalog);

        names.Should().Equal("ShockWave", "Contra");
    }

    [Fact]
    public async Task DownloadModDataByNameAsyncReadsReferenceCaseInsensitivelyAsync()
    {
        var yamlReader = new StubRemoteYamlDocumentReader();
        var client = new RemoteLauncherCatalogClient(
            yamlReader,
            NullLogger<RemoteLauncherCatalogClient>.Instance);
        var shockwaveUri = new Uri("https://example.test/shockwave.yaml");
        string patchUrl = "https://example.test/shockwave-patch.yaml";
        string addonUrl = "https://example.test/shockwave-addon.yaml";
        var catalog = new RemoteLauncherCatalog(
            Array.Empty<RemoteAdvertisingReference>(),
            new List<RemoteCatalogModificationReference>
            {
                new("ShockWave", shockwaveUri.ToString(), new[] { patchUrl }, new[] { addonUrl })
            },
            Array.Empty<string>(),
            Array.Empty<string>());
        yamlReader.SetResult(shockwaveUri, new LegacyContentManifest
        {
            Name = "ShockWave",
            Version = "1.2"
        });

        RemoteModificationManifest result = await client.DownloadModDataByNameAsync(
            catalog,
            "shockwave",
            CancellationToken.None);

        result.Content.Name.Should().Be("ShockWave");
        result.Content.Version.Should().Be("1.2");
        result.PatchManifestUrls.Should().ContainSingle(patchUrl);
        result.AddonManifestUrls.Should().ContainSingle(addonUrl);
    }

    [Fact]
    public async Task DownloadAdvertisingInfoAsyncReturnsManifestWhenReadSucceedsAsync()
    {
        var yamlReader = new StubRemoteYamlDocumentReader();
        var client = new RemoteLauncherCatalogClient(
            yamlReader,
            NullLogger<RemoteLauncherCatalogClient>.Instance);
        var manifestUri = new Uri("https://example.test/featured.yaml");
        yamlReader.SetResult(manifestUri, new LegacyContentManifest
        {
            Name = "Featured",
            Version = "2.0",
            UIImageSourceLink = "https://cdn.example.test/featured.png"
        });

        LauncherContentVersion? result = await client.DownloadAdvertisingInfoAsync(
            manifestUri.ToString(),
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Featured");
        result.Version.Should().Be("2.0");
        result.UIImageSourceLink.Should().Be("https://cdn.example.test/featured.png");
    }

    [Fact]
    public async Task DownloadAdvertisingInfoAsyncReturnsNullWhenReadFailsAsync()
    {
        var yamlReader = new StubRemoteYamlDocumentReader();
        var client = new RemoteLauncherCatalogClient(
            yamlReader,
            NullLogger<RemoteLauncherCatalogClient>.Instance);
        var manifestUri = new Uri("https://example.test/featured.yaml");
        yamlReader.SetException<LegacyContentManifest>(
            manifestUri,
            new InvalidOperationException("Missing manifest."));

        LauncherContentVersion? result = await client.DownloadAdvertisingInfoAsync(
            manifestUri.ToString(),
            CancellationToken.None);

        result.Should().BeNull();
    }
}
