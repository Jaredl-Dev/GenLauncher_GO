using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Infrastructure.Mods.Models;
using GenLauncherGO.Infrastructure.Mods.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Mods.Services;

public sealed class RemoteLauncherCatalogClientTests
{
    [Fact]
    public async Task DownloadInstalledModDataAsync_ReadsInstalledModsAndPreservesPartialFailuresAsync()
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

    /// <summary>
    ///     The backend publishes unnamed references for content the launcher cannot match against installed folders,
    ///     so they are read on every refresh instead of being filtered out with the mods nobody installed.
    /// </summary>
    [Fact]
    public async Task DownloadInstalledModDataAsync_ReadsUnnamedReferenceRegardlessOfInstalledContentAsync()
    {
        var yamlReader = new StubRemoteYamlDocumentReader();
        var client = new RemoteLauncherCatalogClient(
            yamlReader,
            NullLogger<RemoteLauncherCatalogClient>.Instance);
        var unnamedUri = new Uri("https://example.test/unnamed.yaml");
        var contraUri = new Uri("https://example.test/contra.yaml");
        var catalog = new RemoteLauncherCatalog(
            Array.Empty<RemoteAdvertisingReference>(),
            new List<RemoteCatalogModificationReference>
            {
                new(string.Empty, unnamedUri.ToString(), Array.Empty<string>(), Array.Empty<string>()),
                new("Contra", contraUri.ToString(), Array.Empty<string>(), Array.Empty<string>())
            },
            Array.Empty<string>(),
            Array.Empty<string>());
        yamlReader.SetResult(unnamedUri, new LegacyContentManifest
        {
            Name = "Community Pack",
            Version = "3.0"
        });

        IReadOnlyList<RemoteModificationManifest> result =
            await client.DownloadInstalledModDataAsync(
                catalog,
                new[] { "ShockWave" },
                CancellationToken.None);

        result.Should().ContainSingle().Which.Content.Name.Should().Be("Community Pack");
        yamlReader.GetReadCount<LegacyContentManifest>(contraUri).Should().Be(0);
    }

    [Fact]
    public async Task ReadChildManifestsAsync_ReturnsSuccessfulChildrenWhenOneChildFailsAsync()
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
            null,
            CancellationToken.None);

        result.ContentVersions.Should().ContainSingle().Which.Name.Should().Be("Patch");
        result.FailedCount.Should().Be(1);
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task ReadCatalogAsync_MapsThirdPartyManifestToNormalizedCatalogAsync()
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
    public void GetModificationNames_ReturnsCatalogModificationNames()
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
    public async Task DownloadModDataByNameAsync_ReadsReferenceCaseInsensitivelyAsync()
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
    public async Task DownloadAdvertisingInfoAsync_ReturnsManifestWhenReadSucceedsAsync()
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
    public async Task DownloadAdvertisingInfoAsync_ReturnsNullWhenReadFailsAsync()
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

    /// <summary>
    ///     A manifest read that fails is tolerated and reported as absent content, but a cancelled one is not a
    ///     failure of the manifest: swallowing it would let a torn-down session go on to publish a catalog assembled
    ///     from whatever happened to arrive first.
    /// </summary>
    [Fact]
    public async Task DownloadInstalledModDataAsync_PropagatesCancellationRaisedWhileReadingAsync()
    {
        var yamlReader = new StubRemoteYamlDocumentReader();
        var client = new RemoteLauncherCatalogClient(
            yamlReader,
            NullLogger<RemoteLauncherCatalogClient>.Instance);
        var shockwaveUri = new Uri("https://example.test/shockwave.yaml");
        var catalog = new RemoteLauncherCatalog(
            Array.Empty<RemoteAdvertisingReference>(),
            new List<RemoteCatalogModificationReference>
            {
                new("ShockWave", shockwaveUri.ToString(), Array.Empty<string>(), Array.Empty<string>())
            },
            Array.Empty<string>(),
            Array.Empty<string>());
        using var cancellation = new CancellationTokenSource();
        yamlReader.SetHandler<LegacyContentManifest>(shockwaveUri, (_, _) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<LegacyContentManifest>(cancellation.Token);
        });

        Func<Task> act = () => client.DownloadInstalledModDataAsync(
            catalog,
            new[] { "ShockWave" },
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReadChildManifestsAsync_PropagatesCancellationRaisedWhileReadingAsync()
    {
        var yamlReader = new StubRemoteYamlDocumentReader();
        var client = new RemoteLauncherCatalogClient(
            yamlReader,
            NullLogger<RemoteLauncherCatalogClient>.Instance);
        var patchUri = new Uri("https://example.test/patch.yaml");
        using var cancellation = new CancellationTokenSource();
        yamlReader.SetHandler<LegacyContentManifest>(patchUri, (_, _) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<LegacyContentManifest>(cancellation.Token);
        });

        Func<Task> act = () => client.ReadChildManifestsAsync(
            new[] { patchUri.ToString() },
            null,
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DownloadAdvertisingInfoAsync_PropagatesCancellationRaisedWhileReadingAsync()
    {
        var yamlReader = new StubRemoteYamlDocumentReader();
        var client = new RemoteLauncherCatalogClient(
            yamlReader,
            NullLogger<RemoteLauncherCatalogClient>.Instance);
        var manifestUri = new Uri("https://example.test/featured.yaml");
        using var cancellation = new CancellationTokenSource();
        yamlReader.SetHandler<LegacyContentManifest>(manifestUri, (_, _) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<LegacyContentManifest>(cancellation.Token);
        });

        Func<Task> act = () => client.DownloadAdvertisingInfoAsync(
            manifestUri.ToString(),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    ///     Manifest reads are capped at a handful in flight so a refresh does not flood the backend. The cap bounds
    ///     how many run at once, not how many are read: a catalog listing more than the cap still gets read whole.
    /// </summary>
    [Fact]
    public async Task ReadChildManifestsAsync_ReadsEveryManifestBeyondItsConcurrencyLimitAsync()
    {
        var yamlReader = new StubRemoteYamlDocumentReader();
        var client = new RemoteLauncherCatalogClient(
            yamlReader,
            NullLogger<RemoteLauncherCatalogClient>.Instance);
        var manifestUrls = new List<string>();
        for (int index = 0; index < 12; index++)
        {
            var childUri = new Uri($"https://example.test/patch-{index}.yaml");
            yamlReader.SetResult(childUri, new LegacyContentManifest
            {
                ModificationType = ModificationType.Patch,
                Name = $"Patch {index}",
                Version = "1.0"
            });
            manifestUrls.Add(childUri.ToString());
        }

        RemoteChildManifestLoadResult result = await client.ReadChildManifestsAsync(
            manifestUrls,
            "ShockWave",
            CancellationToken.None);

        result.ContentVersions.Should().HaveCount(12);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task DownloadInstalledModDataAsync_ReadsEveryManifestBeyondItsConcurrencyLimitAsync()
    {
        var yamlReader = new StubRemoteYamlDocumentReader();
        var client = new RemoteLauncherCatalogClient(
            yamlReader,
            NullLogger<RemoteLauncherCatalogClient>.Instance);
        var references = new List<RemoteCatalogModificationReference>();
        var installedModNames = new List<string>();
        for (int index = 0; index < 12; index++)
        {
            var modUri = new Uri($"https://example.test/mod-{index}.yaml");
            yamlReader.SetResult(modUri, new LegacyContentManifest
            {
                ModificationType = ModificationType.Mod,
                Name = $"Mod {index}",
                Version = "1.0"
            });
            references.Add(new RemoteCatalogModificationReference(
                $"Mod {index}",
                modUri.ToString(),
                Array.Empty<string>(),
                Array.Empty<string>()));
            installedModNames.Add($"Mod {index}");
        }

        IReadOnlyList<RemoteModificationManifest> result = await client.DownloadInstalledModDataAsync(
            new RemoteLauncherCatalog(
                Array.Empty<RemoteAdvertisingReference>(),
                references,
                Array.Empty<string>(),
                Array.Empty<string>()),
            installedModNames,
            CancellationToken.None);

        result.Should().HaveCount(12);
    }
}
