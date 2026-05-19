using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Infrastructure.Mods.Models;
using GenLauncherGO.Infrastructure.Mods.Support;
using GenLauncherGO.Infrastructure.Remote;
using GenLauncherGO.Tests.Testing;

namespace GenLauncherGO.Tests.Infrastructure.Mods.Support;

public sealed class RemoteLauncherCatalogMapperTests
{
    [Fact]
    public async Task PublishedCatalogYamlMapsExactLegacyShapeAndLeavesVestigialGlobalAddonsUnpublishedAsync()
    {
        const string globalAddonUrl = "https://example.test/global-addon.yaml";
        LegacyLauncherCatalogDocument document = await ReadRemoteYamlAsync<LegacyLauncherCatalogDocument>(
            """
            AdvData:
            - ModName: Featured
              ModLink: https://example.test/featured.yaml
              ImagesData:
              - https://cdn.example.test/featured-1.png
              - https://cdn.example.test/featured-2.png
            modDatas:
            - ModName: ShockWave
              ModLink: https://example.test/shockwave.yaml
              ModPatches:
              - https://example.test/shockwave-patch.yaml
              ModAddons:
              - https://example.test/shockwave-addon.yaml
            - ModName: Contra
              ModLink: https://example.test/contra.yaml
            globalAddonsData:
            - https://example.test/global-addon.yaml
            originalGameAddons:
            - https://example.test/original-addon.yaml
            originalGamePatches:
            - https://example.test/original-patch.yaml
            LauncherVersion: 1.2.3
            """);

        document.globalAddonsData.Should().ContainSingle(globalAddonUrl);
        document.modDatas.Should().HaveCount(2);
        LegacyCatalogModificationReference defaultedReference = document.modDatas[1];
        defaultedReference.ModPatches.Should().BeEmpty();
        defaultedReference.ModAddons.Should().BeEmpty();

        RemoteLauncherCatalog catalog = RemoteLauncherCatalogMapper.ToRemoteCatalog(document);

        RemoteAdvertisingReference advertising = catalog.AdvertisingEntries.Should().ContainSingle().Subject;
        advertising.Name.Should().Be("Featured");
        advertising.ManifestUrl.Should().Be("https://example.test/featured.yaml");
        advertising.ImageUrls.Should().Equal(
            "https://cdn.example.test/featured-1.png",
            "https://cdn.example.test/featured-2.png");

        RemoteCatalogModificationReference modification = catalog.Modifications[0];
        modification.Name.Should().Be("ShockWave");
        modification.ManifestUrl.Should().Be("https://example.test/shockwave.yaml");
        modification.PatchManifestUrls.Should().ContainSingle("https://example.test/shockwave-patch.yaml");
        modification.AddonManifestUrls.Should().ContainSingle("https://example.test/shockwave-addon.yaml");
        catalog.Modifications[1].Name.Should().Be("Contra");
        catalog.Modifications[1].PatchManifestUrls.Should().BeEmpty();
        catalog.Modifications[1].AddonManifestUrls.Should().BeEmpty();
        catalog.OriginalGameAddonManifestUrls.Should().ContainSingle(
            "https://example.test/original-addon.yaml");
        catalog.OriginalGamePatchManifestUrls.Should().ContainSingle(
            "https://example.test/original-patch.yaml");

        catalog.Modifications.SelectMany(entry => entry.AddonManifestUrls)
            .Should().NotContain(globalAddonUrl);
        catalog.OriginalGameAddonManifestUrls.Should().NotContain(globalAddonUrl);
    }

    [Fact]
    public async Task PublishedContentYamlMapsSupportedFieldsAndIgnoresRetiredThemeBlockAsync()
    {
        LegacyContentManifest document = await ReadRemoteYamlAsync<LegacyContentManifest>(
            """
            ModificationType: Patch
            Name: ShockWave Patch
            Version: '2.4'
            SimpleDownloadLink: https://downloads.example.test/shockwave-patch.zip
            UIImageSourceLink: https://cdn.example.test/shockwave-patch.png
            DiscordLink: https://discord.example.test/shockwave
            ModDBLink: https://moddb.example.test/shockwave
            NewsLink: https://news.example.test/shockwave
            DependenceName: ShockWave
            S3HostLink: https://s3.example.test
            S3BucketName: launcher-content
            S3FolderName: shockwave/patch
            S3HostPublicKey: public-key
            S3HostSecretKey: secret-key
            NetworkInfo: Multiplayer requires the community service.
            Deprecated: true
            SupportLink: https://support.example.test/shockwave
            ColorsInformation:
              GenLauncherActiveColor: '#102030'
              GenLauncherBackgroundImageLink: https://cdn.example.test/background.png
            ContentSourceKind: Manual
            """);

        var version = RemoteLauncherCatalogMapper.ToLauncherContentVersion(document);

        version.Should().BeEquivalentTo(new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation { ContentSourceKind = ContentSourceKind.ManagedS3 },
            ModificationType = ModificationType.Patch,
            Name = "ShockWave Patch",
            Version = "2.4",
            SimpleDownloadLink = "https://downloads.example.test/shockwave-patch.zip",
            UIImageSourceLink = "https://cdn.example.test/shockwave-patch.png",
            DiscordLink = "https://discord.example.test/shockwave",
            ModDBLink = "https://moddb.example.test/shockwave",
            NewsLink = "https://news.example.test/shockwave",
            ParentContentName = "ShockWave",
            S3HostLink = "https://s3.example.test",
            S3BucketName = "launcher-content",
            S3FolderName = "shockwave/patch",
            S3HostPublicKey = "public-key",
            S3HostSecretKey = "secret-key",
            NetworkInfo = "Multiplayer requires the community service.",
            Deprecated = true,
            SupportLink = "https://support.example.test/shockwave",
        });
    }

    [Fact]
    public async Task PublishedContentYamlUsesDeclaredSourceKindWhenPackageMetadataIsAbsentAsync()
    {
        LegacyContentManifest document = await ReadRemoteYamlAsync<LegacyContentManifest>(
            """
            Name: Manually Installed
            ContentSourceKind: Manual
            """);

        var version = RemoteLauncherCatalogMapper.ToLauncherContentVersion(document);

        version.ModificationType.Should().Be(ModificationType.Mod);
        version.Name.Should().Be("Manually Installed");
        version.Version.Should().BeEmpty();
        version.Deprecated.Should().BeFalse();
        version.Installation.ContentSourceKind.Should().Be(ContentSourceKind.Manual);
    }

    [Fact]
    public void ToRemoteCatalogReturnsEmptyCatalogForNullManifest()
    {
        RemoteLauncherCatalog result = RemoteLauncherCatalogMapper.ToRemoteCatalog(null);

        result.Should().BeSameAs(RemoteLauncherCatalog.Empty);
    }

    private static async Task<T> ReadRemoteYamlAsync<T>(string yaml)
    {
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(yaml, Encoding.UTF8)
        });
        using HttpClient httpClient = new(handler);
        HttpRemoteYamlDocumentReader reader = new(httpClient);

        return await reader.ReadYamlAsync<T>(
            new Uri("https://example.test/catalog.yaml"),
            CancellationToken.None);
    }

}
