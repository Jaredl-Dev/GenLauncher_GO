using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Tests.Core.Mods.Models;

public sealed class LauncherContentTests
{
    [Fact]
    public void AddOrUpdateKeepsOneCanonicalVersionAndCombinesLocalStateWithRemoteMetadata()
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
            ModificationType = ModificationType.Mod
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
            ModDBLink = "https://example.test/moddb"
        };
        LauncherContent content = CreateContent(localVersion, remoteVersion);

        LauncherContentVersion merged = content.Versions.Should().ContainSingle().Which;
        merged.ContentKey.Should().Be(localVersion.ContentKey);
        merged.SimpleDownloadLink.Should().Be(remoteVersion.SimpleDownloadLink);
        merged.ModDBLink.Should().Be(remoteVersion.ModDBLink);
        merged.Installation.Should().BeSameAs(localState);
        merged.Installation.Installed.Should().BeTrue();
        merged.Installation.IsSelected.Should().BeTrue();
        merged.EffectiveContentSourceKind.Should().Be(ContentSourceKind.ManagedSingleFile);
        content.IsSelected.Should().BeTrue();
        content.Installed.Should().BeTrue();
    }

    [Fact]
    public void LatestVersionIsTheCardPresentationMetadataAuthority()
    {
        LauncherContentVersion latest = CreateVersion("2.0", supportLink: "https://example.test/current");
        LauncherContent content = CreateContent(
            CreateVersion("1.0", supportLink: "https://example.test/old"),
            latest);

        content.LatestVersion.Should().BeSameAs(latest);
        content.LatestVersion.SupportLink.Should().Be("https://example.test/current");
    }

    [Fact]
    public void SelectedVersionUsesPersistedInstalledSelectionBeforeFallbacks()
    {
        LauncherContentVersion earliestInstalled = CreateVersion("1.0", installed: true);
        LauncherContentVersion selectedInstalled = CreateVersion("2.0", installed: true, isSelected: true);
        LauncherContentVersion latestRemote = CreateVersion("3.0");
        LauncherContent content = CreateContent(
            earliestInstalled,
            latestRemote,
            selectedInstalled);

        LauncherContentVersion? selectedVersion = content.GetSelectedVersion();

        selectedVersion.Should().BeSameAs(selectedInstalled);
    }

    [Fact]
    public void SelectedVersionFallsBackToEarliestInstalledThenEarliestKnownVersion()
    {
        LauncherContentVersion latestRemote = CreateVersion("3.0");
        LauncherContentVersion earliestInstalled = CreateVersion("1.0", installed: true);
        LauncherContentVersion middleRemote = CreateVersion("2.0");
        LauncherContent installedContent = CreateContent(
            latestRemote,
            earliestInstalled,
            middleRemote);
        LauncherContent remoteContent = CreateContent(latestRemote, middleRemote);

        installedContent.GetSelectedVersion().Should().BeSameAs(earliestInstalled);
        remoteContent.GetSelectedVersion().Should().BeSameAs(middleRemote);
    }

    [Fact]
    public void LatestInstalledVersionUsesCanonicalVersionOrdering()
    {
        LauncherContentVersion latestInstalled = CreateVersion("2.0", installed: true);
        LauncherContentVersion earliestInstalled = CreateVersion("1.0", installed: true);
        LauncherContentVersion remoteUpdate = CreateVersion("3.0");
        LauncherContent content = CreateContent(
            latestInstalled,
            remoteUpdate,
            earliestInstalled);

        content.LatestInstalledVersion.Should().BeSameAs(latestInstalled);
    }

    private static LauncherContent CreateContent(params LauncherContentVersion[] versions)
    {
        var data = new LauncherData();
        foreach (LauncherContentVersion version in versions)
        {
            data.AddOrUpdate(version);
        }

        return data.FindContent(versions[0].ContentKey)!;
    }

    private static LauncherContentVersion CreateVersion(
        string version,
        string supportLink = "",
        bool installed = false,
        bool isSelected = false)
    {
        return new LauncherContentVersion(new LauncherContentInstallation
        {
            Installed = installed,
            IsSelected = isSelected,
        })
        {
            Name = "ShockWave",
            Version = version,
            ModificationType = ModificationType.Mod,
            SupportLink = supportLink
        };
    }
}
