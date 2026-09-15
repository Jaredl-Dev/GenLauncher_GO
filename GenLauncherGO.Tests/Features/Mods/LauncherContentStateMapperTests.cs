using System.Linq;
using GenLauncherGO.Features.Integrity;
using GenLauncherGO.Features.Mods;

namespace GenLauncherGO.Tests.Features.Mods;

public sealed class LauncherContentStateMapperTests
{
    [Fact]
    public void ToLauncherData_RestoresSelectedInstalledContentState()
    {
        var state = new LauncherContentState
        {
            Modifications =
            [
                CreateEntry("ShockWave", string.Empty, ModificationType.Mod, "1.0", true)
            ],
            Patches =
            [
                CreateEntry("ShockWave Patch", "ShockWave", ModificationType.Patch, "1.1", true)
            ],
            Addons =
            [
                CreateEntry("Music Pack", "ShockWave Patch", ModificationType.Addon, "2.0", true)
            ]
        };

        var launcherData = LauncherContentStateMapper.ToLauncherData(state);

        LauncherContentVersion modVersion = launcherData.Modifications.Should().ContainSingle().Subject
            .Versions.Should().ContainSingle().Subject;
        launcherData.Modifications[0].IsSelected.Should().BeTrue();
        launcherData.Modifications[0].NumberInList.Should().Be(4);
        modVersion.Name.Should().Be("ShockWave");
        modVersion.ModificationType.Should().Be(ModificationType.Mod);
        modVersion.Installation.Installed.Should().BeTrue();
        modVersion.Installation.IsSelected.Should().BeTrue();

        LauncherContentVersion patchVersion = launcherData.Patches.Should().ContainSingle().Subject
            .Versions.Should().ContainSingle().Subject;
        patchVersion.Name.Should().Be("ShockWave Patch");
        patchVersion.ParentContentName.Should().Be("ShockWave");
        patchVersion.ModificationType.Should().Be(ModificationType.Patch);

        LauncherContentVersion addonVersion = launcherData.Addons.Should().ContainSingle().Subject
            .Versions.Should().ContainSingle().Subject;
        addonVersion.Name.Should().Be("Music Pack");
        addonVersion.ParentContentName.Should().Be("ShockWave Patch");
        addonVersion.ModificationType.Should().Be(ModificationType.Addon);
    }

    [Fact]
    public void EntryOrder_RoundTripsThroughPersistedState()
    {
        var launcherData = new LauncherData();
        launcherData.AddOrUpdate(TestLauncherContent.Version("Second", installed: true));
        launcherData.AddOrUpdate(TestLauncherContent.Version("First", installed: true));
        launcherData.Modifications[0].NumberInList = 1;
        launcherData.Modifications[1].NumberInList = 0;

        var state = LauncherContentStateMapper.ToLauncherContentState(launcherData);
        var restoredData = LauncherContentStateMapper.ToLauncherData(state);

        state.Modifications.Select(entry => entry.NumberInList).Should().Equal(1, 0);
        restoredData.Modifications
            .OrderBy(modification => modification.NumberInList)
            .Select(modification => modification.Name)
            .Should()
            .Equal("First", "Second");
    }

    [Fact]
    public void ToLauncherData_DoesNotSelectEntryFromStaleVersionSelection()
    {
        var state = new LauncherContentState
        {
            Modifications =
            [
                CreateEntry("ShockWave", string.Empty, ModificationType.Mod, "1.0", false, true)
            ]
        };

        var launcherData = LauncherContentStateMapper.ToLauncherData(state);

        LauncherContent modification = launcherData.Modifications.Should().ContainSingle().Subject;
        modification.IsSelected.Should().BeFalse();
        modification.Versions.Should().ContainSingle().Which.Installation.IsSelected.Should().BeFalse();
    }

    [Theory]
    [InlineData(ModificationType.Addon, ModificationType.Addon, ModificationType.Mod)]
    [InlineData(ModificationType.Addon, ModificationType.Mod, ModificationType.Mod)]
    [InlineData(ModificationType.Patch, ModificationType.Patch, ModificationType.Addon)]
    [InlineData(ModificationType.Patch, ModificationType.Addon, ModificationType.Mod)]
    public void ToLauncherData_TypePrecedence_UsesVersionThenEntryThenStoredSection(
        object storedSectionValue,
        object entryTypeValue,
        object versionTypeValue)
    {
        var storedSection = (ModificationType)storedSectionValue;
        var entryType = (ModificationType)entryTypeValue;
        var versionType = (ModificationType)versionTypeValue;
        var entry = new LauncherContentEntryState
        {
            Name = "Compatibility Addon",
            DependenceName = "ShockWave",
            ModificationType = entryType,
            ModificationVersions =
            [
                new()
                {
                    Version = "1.0",
                    ModificationType = versionType,
                    Installed = true,
                    ContentSourceKind = ContentSourceKind.Manual
                }
            ]
        };
        LauncherContentState state = storedSection == ModificationType.Addon
            ? new LauncherContentState { Addons = [entry] }
            : new LauncherContentState { Patches = [entry] };

        var launcherData = LauncherContentStateMapper.ToLauncherData(state);

        LauncherContentVersion version = launcherData.AllContent.Should().ContainSingle().Subject
            .Versions.Should().ContainSingle().Subject;
        version.ModificationType.Should().Be(ModificationType.Addon);
        version.Name.Should().Be("Compatibility Addon");
        version.ParentContentName.Should().Be("ShockWave");
        version.Installation.ContentSourceKind.Should().Be(ContentSourceKind.Manual);
    }

    /// <summary>
    ///     Older saved data recorded installation on the card rather than on each version, so an entry that claims to
    ///     be installed still has to produce an installed version.
    /// </summary>
    [Fact]
    public void ToLauncherData_TreatsAVersionAsInstalledWhenOnlyItsEntrySaysSo()
    {
        var state = new LauncherContentState
        {
            Modifications =
            [
                new()
                {
                    Name = "ShockWave",
                    ModificationType = ModificationType.Mod,
                    Installed = true,
                    ModificationVersions =
                    [
                        new()
                        {
                            Version = "1.0",
                            Installed = false
                        }
                    ]
                }
            ]
        };

        var launcherData = LauncherContentStateMapper.ToLauncherData(state);

        launcherData.Modifications.Should().ContainSingle().Subject
            .Versions.Should().ContainSingle().Subject
            .Installation.Installed.Should().BeTrue();
    }

    /// <summary>
    ///     A suspended download has partial content on disk but is neither installed nor selected, so it has to
    ///     persist on its own merit or the next session would forget it and start over.
    /// </summary>
    [Fact]
    public void SuspendedDownload_RoundTripsWithoutBeingInstalledOrSelected()
    {
        var launcherData = new LauncherData();
        launcherData.AddOrUpdate(TestLauncherContent.Version(
            "Contra",
            "009",
            sourceKind: ContentSourceKind.Manual,
            downloadSuspended: true,
            suspendedProgressPercentage: 42));

        var state = LauncherContentStateMapper.ToLauncherContentState(launcherData);
        var restoredData = LauncherContentStateMapper.ToLauncherData(state);

        LauncherContentVersionState version = state.Modifications.Should().ContainSingle().Subject
            .ModificationVersions.Should().ContainSingle().Subject;
        version.Installed.Should().BeFalse();
        version.IsSelected.Should().BeFalse();
        version.DownloadSuspended.Should().BeTrue();
        version.SuspendedProgressPercentage.Should().Be(42);
        LauncherContentInstallation installation = restoredData.Modifications.Should().ContainSingle().Subject
            .Versions.Should().ContainSingle().Subject.Installation;
        installation.Installed.Should().BeFalse();
        installation.DownloadSuspended.Should().BeTrue();
        installation.SuspendedProgressPercentage.Should().Be(42);
    }

    [Fact]
    public void ToLauncherData_IgnoresLegacyAdvertisingVersionRecords()
    {
        var state = new LauncherContentState
        {
            Modifications =
            [
                new()
                {
                    Name = "Featured",
                    ModificationType = ModificationType.Advertising,
                    ModificationVersions =
                    [
                        new()
                        {
                            Name = "Featured",
                            Version = "2.0",
                            ModificationType = ModificationType.Advertising,
                            Installed = true
                        }
                    ]
                }
            ]
        };

        var launcherData = LauncherContentStateMapper.ToLauncherData(state);

        launcherData.Modifications.Should().BeEmpty();
        launcherData.Patches.Should().BeEmpty();
        launcherData.Addons.Should().BeEmpty();
    }

    [Fact]
    public void ToLauncherContentState_PersistsAddedRepositoryModsButFiltersUninstalledChildren()
    {
        var launcherData = new LauncherData();
        launcherData.AddOrUpdate(new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation { Installed = true },
            ModificationType = ModificationType.Mod,
            Name = "Installed",
            Version = "1.0"
        });
        launcherData.AddOrUpdate(new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation
            {
                ContentSourceKind = ContentSourceKind.ManagedSingleFile
            },
            ModificationType = ModificationType.Mod,
            Name = "Added",
            Version = "2.0"
        });
        launcherData.AddOrUpdate(new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation
            {
                ContentSourceKind = ContentSourceKind.ManagedSingleFile
            },
            ModificationType = ModificationType.Patch,
            ParentContentName = "Installed",
            Name = "Uninstalled Child",
            Version = "1.0"
        });

        var state = LauncherContentStateMapper.ToLauncherContentState(launcherData);

        state.Modifications.Select(entry => entry.Name).Should().Equal("Installed", "Added");
        state.Modifications[0].ModificationVersions.Should().ContainSingle()
            .Which.Version.Should().Be("1.0");
        state.Modifications[1].ModificationVersions.Should().ContainSingle()
            .Which.Version.Should().Be("2.0");
        state.Patches.Should().BeEmpty();
    }

    /// <summary>
    ///     A stored document can carry an explicit null for a whole section. That reads as "nothing stored here", not
    ///     as a reason to fail the restore and lose every other section with it.
    /// </summary>
    [Fact]
    public void ToLauncherData_MapsNullStoredSectionsToAnEmptyCatalog()
    {
        var state = new LauncherContentState
        {
            Modifications = null!,
            Addons = null!,
            Patches = null!
        };

        var launcherData = LauncherContentStateMapper.ToLauncherData(state);

        launcherData.Modifications.Should().BeEmpty();
        launcherData.Patches.Should().BeEmpty();
        launcherData.Addons.Should().BeEmpty();
    }

    /// <summary>
    ///     A version record carries its own identity, and it is the record each version is rebuilt from, so the name
    ///     and parent it declares win over the entry header it happens to be stored under.
    /// </summary>
    [Fact]
    public void ToLauncherData_PrefersTheVersionRecordIdentityOverTheEntryHeader()
    {
        var state = new LauncherContentState
        {
            Patches =
            [
                new()
                {
                    Name = "Stale Entry Name",
                    DependenceName = "Stale Parent",
                    ModificationType = ModificationType.Patch,
                    ModificationVersions =
                    [
                        new()
                        {
                            Name = "Balance Patch",
                            Version = "2.0",
                            DependenceName = "ShockWave",
                            Installed = true
                        }
                    ]
                }
            ]
        };

        var launcherData = LauncherContentStateMapper.ToLauncherData(state);

        LauncherContentVersion version = launcherData.Patches.Should().ContainSingle().Subject
            .Versions.Should().ContainSingle().Subject;
        version.Name.Should().Be("Balance Patch");
        version.ParentContentName.Should().Be("ShockWave");
    }

    private static LauncherContentEntryState CreateEntry(
        string name,
        string parentContentName,
        ModificationType contentType,
        string version,
        bool selected,
        bool? versionSelected = null,
        int numberInList = 4)
    {
        return new LauncherContentEntryState
        {
            Name = name,
            DependenceName = parentContentName,
            ModificationType = contentType,
            IsSelected = selected,
            NumberInList = numberInList,
            ModificationVersions =
            [
                new()
                {
                    Version = version,
                    Installed = true,
                    IsSelected = versionSelected ?? selected,
                    ContentSourceKind = ContentSourceKind.Manual
                }
            ]
        };
    }
}
