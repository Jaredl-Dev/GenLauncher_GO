using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Mods;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Services;

public sealed class LauncherTileActionServiceTests
{
    [Fact]
    public void GetAdvertisingDownloadAction_ReturnsLinkAndThankYouForAdvertisingWithSimpleLink()
    {
        LauncherContent modification = CreateContent(
            ModificationType.Advertising,
            "https://example.test/donate");

        LauncherTileLinkAction result = LauncherTileActionService.GetAdvertisingDownloadAction(modification);

        result.Uri.Should().Be("https://example.test/donate");
        result.ShowThankYouMessage.Should().BeTrue();
    }

    [Fact]
    public void GetAdvertisingDownloadAction_ReturnsNoActionForNonAdvertisingMod()
    {
        LauncherContent modification = CreateContent(
            ModificationType.Mod,
            "https://example.test/download");

        LauncherTileLinkAction result = LauncherTileActionService.GetAdvertisingDownloadAction(modification);

        result.Uri.Should().BeNull();
        result.ShowThankYouMessage.Should().BeFalse();
    }

    /// <summary>
    ///     Both informational links open the same way; only advertising thanks the user for following one.
    /// </summary>
    [Theory]
    [InlineData(true, ModificationType.Mod, false)]
    [InlineData(true, ModificationType.Advertising, true)]
    [InlineData(false, ModificationType.Mod, false)]
    [InlineData(false, ModificationType.Advertising, true)]
    public void GetLinkAction_InformationalLink_ThanksOnlyForAdvertising(
        bool isChangeLogLink,
        ModificationType modificationType,
        bool expectedThankYouMessage)
    {
        const string LinkUri = "https://example.test/link";
        LauncherContent modification = isChangeLogLink
            ? CreateContent(modificationType, newsLink: LinkUri)
            : CreateContent(modificationType, networkInfo: LinkUri);

        LauncherTileLinkAction result = isChangeLogLink
            ? LauncherTileActionService.GetLinkAction(modification, LauncherTileLinkKind.ChangeLog)
            : LauncherTileActionService.GetLinkAction(modification, LauncherTileLinkKind.NetworkInfo);

        result.Uri.Should().Be(LinkUri);
        result.ShowThankYouMessage.Should().Be(expectedThankYouMessage);
    }

    [Fact]
    public void GetLinkAction_SupportLink_AlwaysShowsThankYou()
    {
        LauncherContent modification = CreateContent(
            ModificationType.Mod,
            supportLink: "https://example.test/support");

        LauncherTileLinkAction result = LauncherTileActionService.GetLinkAction(modification, LauncherTileLinkKind.Support);

        result.Uri.Should().Be("https://example.test/support");
        result.ShowThankYouMessage.Should().BeTrue();
    }

    [Fact]
    public void DeleteVersionForModDiscardsContentAnd_RefreshesLocalCatalogWithoutPersisting()
    {
        var catalog = new FakeLauncherContentCatalog();
        LauncherTileActionService service = new(catalog);
        LauncherContentVersion selectedVersion = new()
        {
            Name = "Shockwave",
            Version = "1.0",
            ModificationType = ModificationType.Mod,
            ParentContentName = "Zero Hour"
        };
        ModificationVersionSelection versionSelection = new(
            selectedVersion,
            CreateViewModel(selectedVersion));

        bool removedContentCard = service.DeleteVersion(versionSelection);

        removedContentCard.Should().BeTrue();
        catalog.DiscardedContents.Should().ContainSingle().Which.Should().Match<LauncherContentKey>(contentKey =>
            contentKey.Name == "Shockwave" &&
            contentKey.Version == "1.0" &&
            contentKey.ContentType == ModificationType.Mod &&
            contentKey.ParentIdentity == "Zero Hour");
        catalog.UninstalledVersions.Should().BeEmpty();
        catalog.LocalDataUpdateCount.Should().Be(1);
        catalog.SaveCount.Should().Be(0);
    }

    [Fact]
    public void DeleteVersionForRemoteChildContent_DeletesSelectedVersionAndRefreshesLocalCatalog()
    {
        var catalog = new FakeLauncherContentCatalog();
        LauncherTileActionService service = new(catalog);
        LauncherContentVersion selectedVersion = new()
        {
            Installation = new LauncherContentInstallation
            { ContentSourceKind = ContentSourceKind.ManagedSingleFile },
            Name = "HD",
            Version = "1.0",
            ModificationType = ModificationType.Addon,
            ParentContentName = "Shockwave"
        };
        ModificationVersionSelection versionSelection = new(
            selectedVersion,
            CreateViewModel(selectedVersion));

        bool removedContentCard = service.DeleteVersion(versionSelection);

        removedContentCard.Should().BeFalse();
        catalog.UninstalledVersions.Should().ContainSingle().Which.Should().Match<LauncherContentKey>(contentKey =>
            contentKey.Name == "HD" &&
            contentKey.Version == "1.0" &&
            contentKey.ContentType == ModificationType.Addon &&
            contentKey.ParentIdentity == "Shockwave");
        catalog.DiscardedContents.Should().BeEmpty();
        catalog.LocalDataUpdateCount.Should().Be(1);
        catalog.SaveCount.Should().Be(0);
    }

    /// <summary>
    ///     A child version carries no parent of its own until the launcher writes one, so the tile it was deleted
    ///     from is the only place its owner can come from.
    /// </summary>
    [Fact]
    public void DeleteVersionForChildContentWithoutAParent_TakesTheParentFromItsTile()
    {
        var catalog = new FakeLauncherContentCatalog();
        LauncherTileActionService service = new(catalog);
        LauncherContentVersion selectedVersion = new()
        {
            Installation = new LauncherContentInstallation
            { ContentSourceKind = ContentSourceKind.ManagedSingleFile },
            Name = "HD",
            Version = "1.0",
            ModificationType = ModificationType.Addon,
            ParentContentName = string.Empty
        };
        ModificationVersionSelection versionSelection = new(
            selectedVersion,
            CreateViewModel(selectedVersion, "Shockwave"));

        bool removedContentCard = service.DeleteVersion(versionSelection);

        removedContentCard.Should().BeFalse();
        catalog.UninstalledVersions.Should().ContainSingle().Which.Should().Match<LauncherContentKey>(contentKey =>
            contentKey.Name == "HD" &&
            contentKey.Version == "1.0" &&
            contentKey.ContentType == ModificationType.Addon &&
            contentKey.ParentIdentity == "Shockwave");
    }

    [Theory]
    [InlineData(ModificationType.Addon)]
    [InlineData(ModificationType.Patch)]
    public void DeleteVersionForManualChildContent_RemovesContentAndRefreshesLocalCatalogWithoutPersisting(
        ModificationType modificationType)
    {
        var catalog = new FakeLauncherContentCatalog();
        LauncherTileActionService service = new(catalog);
        LauncherContentVersion selectedVersion = new()
        {
            Installation = new LauncherContentInstallation { ContentSourceKind = ContentSourceKind.Manual },
            Name = "Manual Content",
            Version = "1.0",
            ModificationType = modificationType,
            ParentContentName = "Shockwave"
        };
        ModificationVersionSelection versionSelection = new(
            selectedVersion,
            CreateViewModel(selectedVersion, "Shockwave"));

        bool removedContentCard = service.DeleteVersion(versionSelection);

        removedContentCard.Should().BeTrue();
        catalog.DiscardedContents.Should().ContainSingle().Which.Should().Match<LauncherContentKey>(contentKey =>
            contentKey.Name == "Manual Content" &&
            contentKey.Version == "1.0" &&
            contentKey.ContentType == modificationType &&
            contentKey.ParentIdentity == "Shockwave");
        catalog.UninstalledVersions.Should().BeEmpty();
        catalog.LocalDataUpdateCount.Should().Be(1);
        catalog.SaveCount.Should().Be(0);
    }

    [Fact]
    public void DiscardContentVersion_DeletesLatestVersionAndRemovesCatalogEntryWithoutPersisting()
    {
        var catalog = new FakeLauncherContentCatalog();
        LauncherTileActionService service = new(catalog);
        LauncherContentVersion latestVersion = new()
        {
            Name = "Shockwave",
            Version = "2.0",
            ModificationType = ModificationType.Mod,
            ParentContentName = "Zero Hour"
        };
        ModificationViewModel viewModel = CreateViewModel(latestVersion);

        service.DiscardContentVersion(viewModel);

        catalog.DiscardedVersions.Should().ContainSingle().Which.Should().Match<LauncherContentKey>(contentKey =>
            contentKey.Name == "Shockwave" &&
            contentKey.Version == "2.0" &&
            contentKey.ContentType == ModificationType.Mod &&
            contentKey.ParentIdentity == "Zero Hour");
        catalog.LocalDataUpdateCount.Should().Be(1);
        catalog.SaveCount.Should().Be(0);
    }

    [Fact]
    public void UninstallContentVersion_DeletesLatestVersionAndKeepsCatalogEntryWithoutPersisting()
    {
        var catalog = new FakeLauncherContentCatalog();
        LauncherTileActionService service = new(catalog);
        LauncherContentVersion latestVersion = new()
        {
            Name = "HD",
            Version = "1.0",
            ModificationType = ModificationType.Addon
        };
        ModificationViewModel viewModel = CreateViewModel(latestVersion, "Shockwave");

        service.UninstallContentVersion(viewModel);

        catalog.UninstalledVersions.Should().ContainSingle().Which.Should().Match<LauncherContentKey>(contentKey =>
            contentKey.Name == "HD" &&
            contentKey.Version == "1.0" &&
            contentKey.ContentType == ModificationType.Addon &&
            contentKey.ParentIdentity == "Shockwave");
        catalog.DiscardedVersions.Should().BeEmpty();
        catalog.LocalDataUpdateCount.Should().Be(1);
        catalog.SaveCount.Should().Be(0);
    }

    private static ModificationViewModel CreateViewModel(
        LauncherContentVersion version,
        string? containerParentContentName = null)
    {
        LauncherContentVersion contentVersion = string.IsNullOrWhiteSpace(containerParentContentName)
            ? version
            : new LauncherContentVersion(version.Installation)
            {
                Name = version.Name,
                Version = version.Version,
                ModificationType = version.ModificationType,
                ParentContentName = containerParentContentName
            };
        return new ModificationViewModel(
            new LauncherContent(contentVersion),
            new ModificationImageSourceFactory(NullLogger<ModificationImageSourceFactory>.Instance),
            TestLauncherRuntimeContext.Create(),
            Substitute.For<IModificationImageFileService>(),
            new FakeStringLocalizer(),
            new LauncherPackageActivityService(),
            NullLogger<ModificationViewModel>.Instance);
    }

    private static LauncherContent CreateContent(
        ModificationType modificationType,
        string simpleDownloadLink = "",
        string newsLink = "",
        string networkInfo = "",
        string supportLink = "")
    {
        return new LauncherContent(new LauncherContentVersion
        {
            ModificationType = modificationType,
            Name = "Content",
            Version = "1.0",
            SimpleDownloadLink = simpleDownloadLink,
            NewsLink = newsLink,
            NetworkInfo = networkInfo,
            SupportLink = supportLink
        });
    }
}
