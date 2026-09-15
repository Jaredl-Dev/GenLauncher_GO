using GenLauncherGO.Features.Integrity;
using GenLauncherGO.Features.Mods;

namespace GenLauncherGO.Tests.Features.Mods;

public sealed class LauncherContentTests
{
    [Fact]
    public void AddOrUpdate_KeepsOneCanonicalVersionAndCombinesLocalStateWithRemoteMetadata()
    {
        var localState = new LauncherContentInstallation
        {
            Installed = true,
            ContentSourceKind = ContentSourceKind.UnknownLegacy
        };
        var localVersion = new LauncherContentVersion(localState)
        {
            Name = "ShockWave",
            Version = "1.2",
            ModificationType = ModificationType.Mod,
            SimpleDownloadLink = "https://example.test/local-package.zip",
            ModDBLink = "https://example.test/local-moddb",
            S3BucketName = "local-mods"
        };
        var remoteState = new LauncherContentInstallation
        {
            IsSelected = true,
            ContentSourceKind = ContentSourceKind.ManagedSingleFile
        };
        var remoteVersion = new LauncherContentVersion(remoteState)
        {
            Name = "shockwave",
            Version = "1.2",
            ModificationType = ModificationType.Mod,
            SimpleDownloadLink = "https://example.test/package.zip",
            ModDBLink = "https://example.test/moddb",
            S3BucketName = "remote-mods"
        };

        LauncherContent content = TestLauncherContent.From(localVersion, remoteVersion);

        LauncherContentVersion merged = content.Versions.Should().ContainSingle().Which;
        merged.ContentKey.Should().Be(localVersion.ContentKey);
        merged.Name.Should().Be("ShockWave");
        merged.SimpleDownloadLink.Should().Be("https://example.test/local-package.zip");
        merged.ModDBLink.Should().Be("https://example.test/local-moddb");
        merged.S3BucketName.Should().Be("local-mods");
        merged.Installation.Should().BeSameAs(localState);
        merged.Installation.Installed.Should().BeTrue();
        merged.Installation.IsSelected.Should().BeTrue();
        merged.EffectiveContentSourceKind.Should().Be(ContentSourceKind.ManagedSingleFile);
        content.IsSelected.Should().BeTrue();
        content.Installed.Should().BeTrue();
    }

    [Theory]
    [InlineData(ContentSourceKind.UnknownLegacy, ContentSourceKind.Manual, ContentSourceKind.Manual)]
    [InlineData(ContentSourceKind.Manual, ContentSourceKind.UnknownLegacy, ContentSourceKind.Manual)]
    [InlineData(ContentSourceKind.Manual, ContentSourceKind.ManagedSingleFile, ContentSourceKind.Manual)]
    public void AddOrUpdate_SourceKindPrecedence_PreservesClassifiedLocalStateOrAdoptsIncoming(
        object localSourceKindValue,
        object incomingSourceKindValue,
        object expectedSourceKindValue)
    {
        var localSourceKind = (ContentSourceKind)localSourceKindValue;
        var incomingSourceKind = (ContentSourceKind)incomingSourceKindValue;
        var expectedSourceKind = (ContentSourceKind)expectedSourceKindValue;
        LauncherContentVersion localVersion = TestLauncherContent.Version(
            version: "1.2",
            installed: true,
            sourceKind: localSourceKind);
        LauncherContentVersion remoteVersion = TestLauncherContent.Version(
            version: "1.2",
            sourceKind: incomingSourceKind);

        LauncherContent content = TestLauncherContent.From(localVersion, remoteVersion);

        content.Versions.Should().ContainSingle()
            .Which.EffectiveContentSourceKind.Should().Be(expectedSourceKind);
    }

    [Fact]
    public void AddOrUpdate_WhenLocalRecordHasNoCatalogMetadata_PreservesEveryIncomingFieldAndLocalState()
    {
        var localInstallation = new LauncherContentInstallation
        {
            Installed = true,
            ContentSourceKind = ContentSourceKind.UnknownLegacy
        };
        var localVersion = new LauncherContentVersion(localInstallation)
        {
            ModificationType = ModificationType.Patch,
            Name = "Patch",
            Version = "2.0",
            ParentContentName = "ShockWave"
        };
        var theme = new LauncherContentTheme { GenLauncherActiveColor = "#FF123456" };
        var incomingVersion = new LauncherContentVersion
        {
            ModificationType = ModificationType.Patch,
            Name = "Patch",
            Version = "2.0",
            ParentContentName = "ShockWave",
            SimpleDownloadLink = "https://example.test/package.zip",
            UIImageSourceLink = "https://example.test/image.png",
            DiscordLink = "https://example.test/discord",
            ModDBLink = "https://example.test/moddb",
            NewsLink = "https://example.test/news",
            S3HostLink = "https://s3.example.test",
            S3BucketName = "packages",
            S3FolderName = "shockwave/patch/2.0",
            S3HostPublicKey = "public-key",
            S3HostSecretKey = "secret-key",
            NetworkInfo = "Community service required.",
            Deprecated = true,
            SupportLink = "https://example.test/support",
            Theme = theme
        };

        LauncherContent content = TestLauncherContent.From(localVersion, incomingVersion);

        LauncherContentVersion merged = content.Versions.Should().ContainSingle().Which;
        merged.Should().BeEquivalentTo(
            incomingVersion,
            options => options.Excluding(version => version.Installation));
        merged.Installation.Should().BeSameAs(localInstallation);
        merged.Installation.Installed.Should().BeTrue();
        merged.EffectiveContentSourceKind.Should().Be(ContentSourceKind.ManagedS3);
    }

    [Fact]
    public void SelectedVersion_UsesPersistedInstalledSelectionBeforeFallbacks()
    {
        LauncherContentVersion earliestInstalled = TestLauncherContent.Version(version: "1.0", installed: true);
        LauncherContentVersion selectedInstalled = TestLauncherContent.Version(
            version: "2.0",
            installed: true,
            isSelected: true);
        LauncherContentVersion latestRemote = TestLauncherContent.Version(version: "3.0", isSelected: true);
        LauncherContent content = TestLauncherContent.From(
            earliestInstalled,
            latestRemote,
            selectedInstalled);

        LauncherContentVersion? selectedVersion = content.GetSelectedVersion();

        selectedVersion.Should().BeSameAs(selectedInstalled);
    }

    [Fact]
    public void SelectedVersion_FallsBackToEarliestInstalledThenEarliestKnownVersion()
    {
        LauncherContentVersion latestRemote = TestLauncherContent.Version(version: "3.0");
        LauncherContentVersion earliestInstalled = TestLauncherContent.Version(version: "1.0", installed: true);
        LauncherContentVersion laterInstalled = TestLauncherContent.Version(version: "2.5", installed: true);
        LauncherContentVersion middleRemote = TestLauncherContent.Version(version: "2.0");
        LauncherContent installedContent = TestLauncherContent.From(
            latestRemote,
            earliestInstalled,
            laterInstalled,
            middleRemote);
        LauncherContent remoteContent = TestLauncherContent.From(latestRemote, middleRemote);

        installedContent.GetSelectedVersion().Should().BeSameAs(earliestInstalled);
        remoteContent.GetSelectedVersion().Should().BeSameAs(middleRemote);
    }

    [Fact]
    public void LatestInstalledVersion_UsesCanonicalVersionOrdering()
    {
        LauncherContentVersion latestInstalled = TestLauncherContent.Version(version: "2.0", installed: true);
        LauncherContentVersion earliestInstalled = TestLauncherContent.Version(version: "1.0", installed: true);
        LauncherContentVersion remoteUpdate = TestLauncherContent.Version(version: "3.0");
        LauncherContent content = TestLauncherContent.From(
            latestInstalled,
            remoteUpdate,
            earliestInstalled);

        content.LatestInstalledVersion.Should().BeSameAs(latestInstalled);
    }
}
