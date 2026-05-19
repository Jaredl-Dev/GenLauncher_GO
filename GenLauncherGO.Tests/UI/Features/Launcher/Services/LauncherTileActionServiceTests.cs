using System;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Mods;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Services;

public sealed class LauncherTileActionServiceTests
{
    [Fact]
    public void GetAdvertisingDownloadActionReturnsLinkAndThankYouForAdvertisingWithSimpleLink()
    {
        LauncherContent modification = CreateContent(
            ModificationType.Advertising,
            simpleDownloadLink: "https://example.test/donate");

        LauncherTileLinkAction result = LauncherTileActionService.GetAdvertisingDownloadAction(modification);

        result.Uri.Should().Be("https://example.test/donate");
        result.ShowThankYouMessage.Should().BeTrue();
    }

    [Fact]
    public void GetAdvertisingDownloadActionReturnsNoActionForNonAdvertisingMod()
    {
        LauncherContent modification = CreateContent(
            ModificationType.Mod,
            simpleDownloadLink: "https://example.test/download");

        LauncherTileLinkAction result = LauncherTileActionService.GetAdvertisingDownloadAction(modification);

        result.Uri.Should().BeNull();
        result.ShowThankYouMessage.Should().BeFalse();
    }

    [Fact]
    public void GetNewsActionShowsThankYouOnlyForAdvertising()
    {
        LauncherContent modification = CreateContent(
            ModificationType.Advertising,
            newsLink: "https://example.test/news");

        LauncherTileLinkAction result = LauncherTileActionService.GetNewsAction(modification);

        result.Uri.Should().Be("https://example.test/news");
        result.ShowThankYouMessage.Should().BeTrue();
    }

    [Fact]
    public void GetNetworkInfoActionReturnsNetworkLink()
    {
        LauncherContent modification = CreateContent(
            ModificationType.Mod,
            networkInfo: "https://example.test/network");

        LauncherTileLinkAction result = LauncherTileActionService.GetNetworkInfoAction(modification);

        result.Uri.Should().Be("https://example.test/network");
        result.ShowThankYouMessage.Should().BeFalse();
    }

    [Fact]
    public void GetSupportActionAlwaysShowsThankYou()
    {
        LauncherContent modification = CreateContent(
            ModificationType.Mod,
            supportLink: "https://example.test/support");

        LauncherTileLinkAction result = LauncherTileActionService.GetSupportAction(modification);

        result.Uri.Should().Be("https://example.test/support");
        result.ShowThankYouMessage.Should().BeTrue();
    }

    [Fact]
    public void DeleteVersionForModDiscardsContentAndRefreshesLocalCatalogWithoutPersisting()
    {
        var catalog = new FakeLauncherContentCatalog();
        LauncherTileActionService service = new(catalog);
        ModificationVersionSelection versionSelection = new()
        {
            VersionName = "2.0",
            SelectedVersion = new LauncherContentVersion
            {
                Name = "Shockwave",
                Version = "1.0",
                ModificationType = ModificationType.Mod,
                ParentContentName = "Zero Hour"
            }
        };

        bool removedContentCard = service.DeleteVersion(versionSelection);

        removedContentCard.Should().BeTrue();
        catalog.DiscardedContents.Should().ContainSingle().Which.Should().Match<LauncherContentKey>(contentKey =>
            contentKey.Name == "Shockwave" &&
            contentKey.Version == "2.0" &&
            contentKey.ContentType == ModificationType.Mod &&
            contentKey.ParentIdentity == "Zero Hour");
        catalog.UninstalledVersions.Should().BeEmpty();
        catalog.LocalDataUpdateCount.Should().Be(1);
        catalog.SaveCount.Should().Be(0);
    }

    [Fact]
    public void DeleteVersionForRemoteChildContentDeletesSelectedVersionAndRefreshesLocalCatalog()
    {
        var catalog = new FakeLauncherContentCatalog();
        LauncherTileActionService service = new(catalog);
        ModificationVersionSelection versionSelection = new()
        {
            VersionName = "1.1",
            SelectedVersion = new LauncherContentVersion
            {
                Installation = new LauncherContentInstallation { ContentSourceKind = ContentSourceKind.ManagedSingleFile },
                Name = "HD",
                Version = "1.0",
                ModificationType = ModificationType.Addon,
                ParentContentName = "Shockwave",
            }
        };

        bool removedContentCard = service.DeleteVersion(versionSelection);

        removedContentCard.Should().BeFalse();
        catalog.UninstalledVersions.Should().ContainSingle().Which.Should().Match<LauncherContentKey>(contentKey =>
            contentKey.Name == "HD" &&
            contentKey.Version == "1.1" &&
            contentKey.ContentType == ModificationType.Addon &&
            contentKey.ParentIdentity == "Shockwave");
        catalog.DiscardedContents.Should().BeEmpty();
        catalog.LocalDataUpdateCount.Should().Be(1);
        catalog.SaveCount.Should().Be(0);
    }

    [Theory]
    [InlineData(ModificationType.Addon)]
    [InlineData(ModificationType.Patch)]
    public void DeleteVersionForManualChildContentRemovesContentAndRefreshesLocalCatalogWithoutPersisting(
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
            ParentContentName = "Shockwave",
        };
        ModificationVersionSelection versionSelection = new()
        {
            VersionName = "1.0",
            SelectedVersion = selectedVersion,
            ModificationViewModel = CreateViewModel(selectedVersion, "Shockwave")
        };

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
    public void DiscardContentVersionDeletesLatestVersionAndRemovesCatalogEntryWithoutPersisting()
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
    public void UninstallContentVersionDeletesLatestVersionAndKeepsCatalogEntryWithoutPersisting()
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
        LauncherContentVersion contentVersion = String.IsNullOrWhiteSpace(containerParentContentName)
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
            new ModificationImageSourceFactory(Microsoft.Extensions.Logging.Abstractions.NullLogger<ModificationImageSourceFactory>.Instance),
            GenLauncherGO.Tests.Testing.TestLauncherRuntimeContext.Create(),
            Substitute.For<IModificationImageFileService>(),
            new GenLauncherGO.Tests.Testing.TestStringLocalizer(),
            new GenLauncherGO.UI.Features.Integrity.LauncherPackageActivityService(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ModificationViewModel>.Instance);
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
