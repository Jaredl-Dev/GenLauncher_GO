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

namespace GenLauncherGO.Tests.Infrastructure.Mods.Support;

public sealed class RemoteLauncherCatalogMapperTests
{
    [Fact]
    public async Task PublishedCatalogYaml_MapsExactLegacyShapeAndLeavesVestigialGlobalAddonsUnpublishedAsync()
    {
        const string GlobalAddonUrl = "https://example.test/global-addon.yaml";
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
              - ~
              ModAddons:
              - https://example.test/shockwave-addon.yaml
            - ModName: Contra
              ModLink: https://example.test/contra.yaml
            globalAddonsData:
            - https://example.test/global-addon.yaml
            originalGameAddons:
            - https://example.test/original-addon.yaml
            -
            originalGamePatches:
            - https://example.test/original-patch.yaml
            LauncherVersion: 1.2.3
            """);

        document.globalAddonsData.Should().Equal(GlobalAddonUrl);
        document.modDatas.Should().HaveCount(2);
        document.modDatas[0].ModPatches.Should().HaveCount(2)
            .And.Contain(url => url == null);
        document.originalGameAddons.Should().HaveCount(2)
            .And.Contain(url => url == null);
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
        modification.PatchManifestUrls.Should().Equal("https://example.test/shockwave-patch.yaml");
        modification.AddonManifestUrls.Should().Equal("https://example.test/shockwave-addon.yaml");
        catalog.Modifications[1].Name.Should().Be("Contra");
        catalog.Modifications[1].PatchManifestUrls.Should().BeEmpty();
        catalog.Modifications[1].AddonManifestUrls.Should().BeEmpty();
        catalog.OriginalGameAddonManifestUrls.Should().Equal(
            "https://example.test/original-addon.yaml");
        catalog.OriginalGamePatchManifestUrls.Should().Equal(
            "https://example.test/original-patch.yaml");

        catalog.Modifications.SelectMany(entry => entry.AddonManifestUrls)
            .Should().NotContain(GlobalAddonUrl);
        catalog.OriginalGameAddonManifestUrls.Should().NotContain(GlobalAddonUrl);
    }

    [Fact]
    public async Task PublishedContentYaml_MapsSupportedFieldsAndThemeBlockAsync()
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
            Theme = new LauncherContentTheme
            {
                GenLauncherActiveColor = "#102030",
                GenLauncherBackgroundImageLink = "https://cdn.example.test/background.png"
            }
        });
    }

    /// <summary>
    ///     The backend publishes a key with an explicit null whenever it has nothing for it. Every consumer of a
    ///     mapped version reads these as plain text, so a published null has to arrive as empty text and never as a
    ///     null that would fault the first caller to touch it.
    /// </summary>
    [Fact]
    public async Task PublishedContentYaml_MapsExplicitlyNullTextKeysToEmptyValuesAsync()
    {
        LegacyContentManifest document = await ReadRemoteYamlAsync<LegacyContentManifest>(
            """
            Name: ~
            Version: ~
            SimpleDownloadLink: ~
            UIImageSourceLink: ~
            DiscordLink: ~
            ModDBLink: ~
            NewsLink: ~
            DependenceName: ~
            S3HostLink: ~
            S3BucketName: ~
            S3FolderName: ~
            S3HostPublicKey: ~
            S3HostSecretKey: ~
            NetworkInfo: ~
            SupportLink: ~
            """);

        var version = RemoteLauncherCatalogMapper.ToLauncherContentVersion(document);

        version.Name.Should().BeEmpty();
        version.Version.Should().BeEmpty();
        version.SimpleDownloadLink.Should().BeEmpty();
        version.UIImageSourceLink.Should().BeEmpty();
        version.DiscordLink.Should().BeEmpty();
        version.ModDBLink.Should().BeEmpty();
        version.NewsLink.Should().BeEmpty();
        version.ParentContentName.Should().BeEmpty();
        version.S3HostLink.Should().BeEmpty();
        version.S3BucketName.Should().BeEmpty();
        version.S3FolderName.Should().BeEmpty();
        version.S3HostPublicKey.Should().BeEmpty();
        version.S3HostSecretKey.Should().BeEmpty();
        version.NetworkInfo.Should().BeEmpty();
        version.SupportLink.Should().BeEmpty();
    }

    /// <summary>
    ///     An explicitly null palette slot means the same as an undeclared one: the launcher fills it from the active
    ///     game's palette instead of carrying a null colour into presentation.
    /// </summary>
    [Fact]
    public async Task PublishedContentTheme_MapsAnExplicitlyNullSlotToEmptyTextAsync()
    {
        LegacyContentManifest document = await ReadRemoteYamlAsync<LegacyContentManifest>(
            """
            Name: Contra
            ColorsInformation:
              GenLauncherActiveColor: '#baff0c'
              GenLauncherBorderColor: ~
            """);

        LauncherContentTheme? theme = RemoteLauncherCatalogMapper.ToLauncherContentVersion(document).Theme;

        theme.Should().NotBeNull();
        theme!.GenLauncherBorderColor.Should().BeEmpty();
        theme.GenLauncherActiveColor.Should().Be("#baff0c");
    }

    /// <summary>
    ///     A catalog reference may publish null for its name or its link. The name decides whether the reference is
    ///     matched against installed content and the link is handed to <see cref="Uri" />, so neither may arrive null.
    /// </summary>
    [Fact]
    public async Task PublishedCatalogYaml_MapsAnExplicitlyNullModificationReferenceToEmptyTextAsync()
    {
        LegacyLauncherCatalogDocument document = await ReadRemoteYamlAsync<LegacyLauncherCatalogDocument>(
            """
            modDatas:
            - ModName: ~
              ModLink: ~
            """);

        RemoteCatalogModificationReference reference = RemoteLauncherCatalogMapper.ToRemoteCatalog(document)
            .Modifications.Should().ContainSingle().Subject;

        reference.Name.Should().BeEmpty();
        reference.ManifestUrl.Should().BeEmpty();
    }

    /// <summary>
    ///     Modifications publish whichever slots they care about, so a partial block has to survive mapping intact.
    /// </summary>
    [Fact]
    public async Task PublishedContentYaml_KeepsEveryThemeSlotItDeclaresAsync()
    {
        LegacyContentManifest document = await ReadRemoteYamlAsync<LegacyContentManifest>(
            """
            Name: Contra
            ColorsInformation:
              GenLauncherBorderColor: '#00e3ff'
              GenLauncherInactiveBorder: DarkGray
              GenLauncherInactiveBorder2: '#7a7db0'
              GenLauncherActiveColor: '#baff0c'
              GenLauncherDarkFillColor: '#232977'
              GenLauncherDarkBackGround: '#090502'
              GenLauncherLightBackGround: '#B3000000'
              GenLauncherDefaultTextColor: White
              GenLauncherDownloadTextColor: '#090502'
              GenLauncherListBoxSelectionColor1: '#E61d2057'
              GenLauncherListBoxSelectionColor2: '#F21d2057'
              GenLauncherButtonSelectionColor: '#2534ff'
              GenLauncherBackgroundImageLink: https://cdn.example.test/contra.png
            """);

        var version = RemoteLauncherCatalogMapper.ToLauncherContentVersion(document);

        version.Theme.Should().BeEquivalentTo(new LauncherContentTheme
        {
            GenLauncherBorderColor = "#00e3ff",
            GenLauncherInactiveBorder = "DarkGray",
            GenLauncherInactiveBorder2 = "#7a7db0",
            GenLauncherActiveColor = "#baff0c",
            GenLauncherDarkFillColor = "#232977",
            GenLauncherDarkBackGround = "#090502",
            GenLauncherLightBackGround = "#B3000000",
            GenLauncherDefaultTextColor = "White",
            GenLauncherDownloadTextColor = "#090502",
            GenLauncherListBoxSelectionColor1 = "#E61d2057",
            GenLauncherListBoxSelectionColor2 = "#F21d2057",
            GenLauncherButtonSelectionColor = "#2534ff",
            GenLauncherBackgroundImageLink = "https://cdn.example.test/contra.png"
        });
    }

    /// <summary>
    ///     A child manifest is reached through the parent that lists it, and that link is authoritative: a manifest
    ///     shared between parents declares only one of them, so the caller's parent has to win.
    /// </summary>
    [Fact]
    public async Task PublishedContentYaml_PrefersCallerSuppliedParentOverDeclaredDependenceAsync()
    {
        LegacyContentManifest document = await ReadRemoteYamlAsync<LegacyContentManifest>(
            """
            ModificationType: Addon
            Name: HD Textures
            Version: '1.0'
            DependenceName: ShockWave
            """);

        var version = RemoteLauncherCatalogMapper.ToLauncherContentVersion(document, "Contra");

        version.ParentContentName.Should().Be("Contra");
    }

    /// <summary>
    ///     An empty block carries no intent, so it must not be mistaken for a modification asking to be re-skinned.
    /// </summary>
    [Fact]
    public async Task PublishedContentYamlWithoutUsableThemeValues_MapsToNoThemeAsync()
    {
        LegacyContentManifest document = await ReadRemoteYamlAsync<LegacyContentManifest>(
            """
            Name: Contra
            ColorsInformation:
              GenLauncherActiveColor: '   '
            """);

        RemoteLauncherCatalogMapper.ToLauncherContentVersion(document).Theme.Should().BeNull();
    }

    [Theory]
    [InlineData(nameof(LegacyContentThemeManifest.GenLauncherBorderColor))]
    [InlineData(nameof(LegacyContentThemeManifest.GenLauncherInactiveBorder))]
    [InlineData(nameof(LegacyContentThemeManifest.GenLauncherInactiveBorder2))]
    [InlineData(nameof(LegacyContentThemeManifest.GenLauncherActiveColor))]
    [InlineData(nameof(LegacyContentThemeManifest.GenLauncherDarkFillColor))]
    [InlineData(nameof(LegacyContentThemeManifest.GenLauncherDarkBackGround))]
    [InlineData(nameof(LegacyContentThemeManifest.GenLauncherLightBackGround))]
    [InlineData(nameof(LegacyContentThemeManifest.GenLauncherDefaultTextColor))]
    [InlineData(nameof(LegacyContentThemeManifest.GenLauncherDownloadTextColor))]
    [InlineData(nameof(LegacyContentThemeManifest.GenLauncherListBoxSelectionColor1))]
    [InlineData(nameof(LegacyContentThemeManifest.GenLauncherListBoxSelectionColor2))]
    [InlineData(nameof(LegacyContentThemeManifest.GenLauncherButtonSelectionColor))]
    [InlineData(nameof(LegacyContentThemeManifest.GenLauncherBackgroundImageLink))]
    public void PublishedContentTheme_KeepsAnySingleDeclaredSlot(string propertyName)
    {
        LegacyContentThemeManifest theme = CreateThemeWithSingleValue(propertyName);
        var document = new LegacyContentManifest { ColorsInformation = theme };

        var result = RemoteLauncherCatalogMapper.ToLauncherContentVersion(document);

        result.Theme.Should().NotBeNull();
    }

    [Fact]
    public void ToRemoteCatalog_NullCollections_MapsEmptyCollections()
    {
        var document = new LegacyLauncherCatalogDocument
        {
            AdvData = null!,
            modDatas = null!,
            originalGameAddons = null!,
            originalGamePatches = null!
        };

        RemoteLauncherCatalog result = RemoteLauncherCatalogMapper.ToRemoteCatalog(document);

        result.AdvertisingEntries.Should().BeEmpty();
        result.Modifications.Should().BeEmpty();
        result.OriginalGameAddonManifestUrls.Should().BeEmpty();
        result.OriginalGamePatchManifestUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishedContentYaml_UsesDeclaredSourceKindWhenPackageMetadataIsAbsentAsync()
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
    public void ToRemoteCatalog_ReturnsEmptyCatalogForNullManifest()
    {
        RemoteLauncherCatalog result = RemoteLauncherCatalogMapper.ToRemoteCatalog(null);

        result.Should().BeSameAs(RemoteLauncherCatalog.Empty);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("ShockWave", "ShockWave")]
    public void ToLauncherContentVersion_NullManifest_MapsParentWithEmptyDefaults(
        string? parentContentName,
        string expectedParentContentName)
    {
        var result = RemoteLauncherCatalogMapper.ToLauncherContentVersion(null, parentContentName);

        result.Should().BeEquivalentTo(new LauncherContentVersion
        {
            ParentContentName = expectedParentContentName
        });
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

    private static LegacyContentThemeManifest CreateThemeWithSingleValue(string propertyName)
    {
        var theme = new LegacyContentThemeManifest();
        typeof(LegacyContentThemeManifest).GetProperty(propertyName)!.SetValue(theme, "published");
        return theme;
    }
}
