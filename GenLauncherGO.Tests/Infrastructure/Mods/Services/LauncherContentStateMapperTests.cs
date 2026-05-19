using System.Linq;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Infrastructure.Mods.Models;
using GenLauncherGO.Infrastructure.Mods.Services;

namespace GenLauncherGO.Tests.Infrastructure.Mods.Services;

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
    public void ToLauncherData_RestoresPersistedEntryOrder()
    {
        var state = new LauncherContentState
        {
            Modifications =
            [
                CreateEntry("Second", string.Empty, ModificationType.Mod, "1.0", false, numberInList: 1),
                CreateEntry("First", string.Empty, ModificationType.Mod, "1.0", false, numberInList: 0)
            ]
        };

        var launcherData = LauncherContentStateMapper.ToLauncherData(state);

        launcherData.Modifications
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

    [Fact]
    public void ToLauncherData_UsesEntryTypeForIncompleteLegacyChildVersionRecords()
    {
        var state = new LauncherContentState
        {
            Addons =
            [
                new()
                {
                    Name = "Compatibility Addon",
                    DependenceName = "ShockWave",
                    ModificationType = ModificationType.Addon,
                    ModificationVersions =
                    [
                        new()
                        {
                            Version = "1.0",
                            Installed = true,
                            ContentSourceKind = ContentSourceKind.Manual
                        }
                    ]
                }
            ]
        };

        var launcherData = LauncherContentStateMapper.ToLauncherData(state);

        LauncherContentVersion version = launcherData.Addons.Should().ContainSingle().Subject
            .Versions.Should().ContainSingle().Subject;
        version.Name.Should().Be("Compatibility Addon");
        version.ParentContentName.Should().Be("ShockWave");
        version.ModificationType.Should().Be(ModificationType.Addon);
        version.Installation.ContentSourceKind.Should().Be(ContentSourceKind.Manual);
    }

    /// <summary>
    ///     Legacy records can leave both type slots at their default, which reads as "mod" for a record that is
    ///     stored under the add-ons section. The section it is stored in is what settles it.
    /// </summary>
    [Fact]
    public void ToLauncherData_UsesTheStoredSectionWhenNeitherEntryNorVersionRecordsAChildType()
    {
        var state = new LauncherContentState
        {
            Addons =
            [
                new()
                {
                    Name = "Compatibility Addon",
                    DependenceName = "ShockWave",
                    ModificationVersions =
                    [
                        new()
                        {
                            Version = "1.0",
                            Installed = true
                        }
                    ]
                }
            ]
        };

        var launcherData = LauncherContentStateMapper.ToLauncherData(state);

        launcherData.Addons.Should().ContainSingle().Subject
            .Versions.Should().ContainSingle().Subject
            .ModificationType.Should().Be(ModificationType.Addon);
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
    ///     Persisted state deliberately holds no remote metadata, so the palette a themed modification wears after an
    ///     offline restart can only come from the cache, looked up under the identity the entry carries.
    /// </summary>
    [Fact]
    public void ToLauncherData_RestoresCachedPaletteForEachVersion()
    {
        var themeCache = new FakeModificationThemeCache();
        var contraTheme = new LauncherContentTheme { GenLauncherActiveColor = "#baff0c" };
        var shockWaveTheme = new LauncherContentTheme { GenLauncherActiveColor = "#00e3ff" };
        themeCache.Save("Contra", "009", contraTheme);
        themeCache.Save("ShockWave", "1.2", shockWaveTheme);
        var state = new LauncherContentState
        {
            Modifications =
            [
                new()
                {
                    Name = "Contra",
                    ModificationType = ModificationType.Mod,
                    ModificationVersions = [new() { Version = "009", Installed = true }]
                },
                new()
                {
                    Name = "ShockWave",
                    ModificationType = ModificationType.Mod,
                    ModificationVersions = [new() { Version = "1.2", Installed = true }]
                }
            ]
        };

        var launcherData = LauncherContentStateMapper.ToLauncherData(state, themeCache);

        launcherData.Modifications
            .Select(modification => modification.Versions.Single().Theme)
            .Should().Equal(contraTheme, shockWaveTheme);
    }

    /// <summary>
    ///     A suspended download has partial content on disk but is neither installed nor selected, so it has to
    ///     persist on its own merit or the next session would forget it and start over.
    /// </summary>
    [Fact]
    public void ToLauncherContentState_PersistsSuspendedDownloadThatIsNeitherInstalledNorSelected()
    {
        var launcherData = new LauncherData();
        launcherData.AddOrUpdate(TestLauncherContent.Version(
            "Contra",
            "009",
            sourceKind: ContentSourceKind.Manual,
            downloadSuspended: true,
            suspendedProgressPercentage: 42));

        var state = LauncherContentStateMapper.ToLauncherContentState(launcherData);

        LauncherContentVersionState version = state.Modifications.Should().ContainSingle().Subject
            .ModificationVersions.Should().ContainSingle().Subject;
        version.Installed.Should().BeFalse();
        version.IsSelected.Should().BeFalse();
        version.DownloadSuspended.Should().BeTrue();
        version.SuspendedProgressPercentage.Should().Be(42);
    }

    [Fact]
    public void ToLauncherData_RestoresSuspendedDownloadProgress()
    {
        var state = new LauncherContentState
        {
            Modifications =
            [
                new()
                {
                    Name = "Contra",
                    ModificationType = ModificationType.Mod,
                    ModificationVersions =
                    [
                        new()
                        {
                            Version = "009",
                            DownloadSuspended = true,
                            SuspendedProgressPercentage = 42,
                            ContentSourceKind = ContentSourceKind.Manual
                        }
                    ]
                }
            ]
        };

        var launcherData = LauncherContentStateMapper.ToLauncherData(state);

        LauncherContentInstallation installation = launcherData.Modifications.Should().ContainSingle().Subject
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

    [Fact]
    public void ToLauncherContentState_DoesNotPersistVersionSelectionForUnselectedEntry()
    {
        var launcherData = new LauncherData();
        launcherData.AddOrUpdate(new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation { Installed = true, IsSelected = true },
            ModificationType = ModificationType.Mod,
            Name = "Installed",
            Version = "1.0"
        });
        launcherData.Modifications[0].IsSelected = false;

        var state = LauncherContentStateMapper.ToLauncherContentState(launcherData);

        LauncherContentEntryState entry = state.Modifications.Should().ContainSingle().Subject;
        entry.IsSelected.Should().BeFalse();
        entry.ModificationVersions.Should().ContainSingle().Which.IsSelected.Should().BeFalse();
    }

    [Fact]
    public void ToLauncherContentState_PersistsEntryOrder()
    {
        var launcherData = new LauncherData();
        launcherData.AddOrUpdate(new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation { Installed = true },
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = "1.0"
        });
        launcherData.Modifications[0].NumberInList = 7;

        var state = LauncherContentStateMapper.ToLauncherContentState(launcherData);

        state.Modifications.Should().ContainSingle().Which.NumberInList.Should().Be(7);
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

    [Fact]
    public void ToLauncherData_MapsAnEntryWithNullVersionRecordsToNoContent()
    {
        var state = new LauncherContentState
        {
            Modifications =
            [
                new()
                {
                    Name = "ShockWave",
                    ModificationType = ModificationType.Mod,
                    ModificationVersions = null!
                }
            ]
        };

        var launcherData = LauncherContentStateMapper.ToLauncherData(state);

        launcherData.Modifications.Should().BeEmpty();
    }

    /// <summary>
    ///     Legacy documents mix advertising records in among ordinary version records. Such a record maps to no
    ///     content card at all, and that must not cost the entry the order and selection it stored.
    /// </summary>
    [Fact]
    public void ToLauncherData_KeepsStoredEntryOrderWhenALegacyAdvertisingRecordFollowsTheContentRecord()
    {
        var state = new LauncherContentState
        {
            Modifications =
            [
                new()
                {
                    Name = "ShockWave",
                    ModificationType = ModificationType.Mod,
                    IsSelected = true,
                    NumberInList = 7,
                    ModificationVersions =
                    [
                        new() { Version = "1.0", Installed = true, IsSelected = true },
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

        LauncherContent modification = launcherData.Modifications.Should().ContainSingle().Subject;
        modification.NumberInList.Should().Be(7);
        modification.IsSelected.Should().BeTrue();
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

    /// <summary>
    ///     Any stored text key may be an explicit null. Identity text is what the catalog keys content by, so a null
    ///     has to restore as empty text rather than travel into a content key.
    /// </summary>
    [Fact]
    public void ToLauncherData_MapsNullStoredIdentityTextToEmptyValues()
    {
        var state = new LauncherContentState
        {
            Modifications =
            [
                new()
                {
                    Name = null!,
                    DependenceName = null!,
                    ModificationType = ModificationType.Mod,
                    ModificationVersions = [new() { Version = null!, Installed = true }]
                }
            ]
        };

        var launcherData = LauncherContentStateMapper.ToLauncherData(state);

        LauncherContentVersion version = launcherData.Modifications.Should().ContainSingle().Subject
            .Versions.Should().ContainSingle().Subject;
        version.Name.Should().BeEmpty();
        version.Version.Should().BeEmpty();
        version.ParentContentName.Should().BeEmpty();
    }

    /// <summary>
    ///     A record that never stored a version restores under the version-less identity, which is the identity the
    ///     mod folder itself is keyed by.
    /// </summary>
    [Fact]
    public void ToLauncherData_RestoresARecordWithNoStoredVersionUnderTheVersionlessIdentity()
    {
        var state = new LauncherContentState
        {
            Modifications =
            [
                new()
                {
                    Name = "ShockWave",
                    ModificationType = ModificationType.Mod,
                    ModificationVersions = [new() { Installed = true }]
                }
            ]
        };

        var launcherData = LauncherContentStateMapper.ToLauncherData(state);

        launcherData.Modifications.Should().ContainSingle().Subject
            .Versions.Should().ContainSingle().Subject
            .Version.Should().BeEmpty();
    }

    /// <summary>
    ///     Content type is settled by the version record first, then the entry header, then the section the entry is
    ///     stored in. A record that declares a type of its own therefore keeps it even when the section disagrees.
    /// </summary>
    [Fact]
    public void ToLauncherData_UsesTheVersionRecordTypeWhenItDisagreesWithTheStoredSection()
    {
        var state = new LauncherContentState
        {
            Patches =
            [
                new()
                {
                    Name = "HD Textures",
                    DependenceName = "ShockWave",
                    ModificationType = ModificationType.Patch,
                    ModificationVersions =
                    [
                        new() { Version = "1.0", ModificationType = ModificationType.Addon, Installed = true }
                    ]
                }
            ]
        };

        var launcherData = LauncherContentStateMapper.ToLauncherData(state);

        launcherData.Patches.Should().BeEmpty();
        launcherData.Addons.Should().ContainSingle().Subject
            .Versions.Should().ContainSingle().Subject
            .ModificationType.Should().Be(ModificationType.Addon);
    }

    /// <summary>
    ///     When the version record declares no type of its own, the entry header settles it, and the header still
    ///     outranks the section the entry is stored in.
    /// </summary>
    [Fact]
    public void ToLauncherData_UsesTheEntryHeaderTypeWhenTheVersionRecordDeclaresNoneAndTheSectionDisagrees()
    {
        var state = new LauncherContentState
        {
            Patches =
            [
                new()
                {
                    Name = "HD Textures",
                    DependenceName = "ShockWave",
                    ModificationType = ModificationType.Addon,
                    ModificationVersions = [new() { Version = "1.0", Installed = true }]
                }
            ]
        };

        var launcherData = LauncherContentStateMapper.ToLauncherData(state);

        launcherData.Patches.Should().BeEmpty();
        launcherData.Addons.Should().ContainSingle().Subject
            .Versions.Should().ContainSingle().Subject
            .ModificationType.Should().Be(ModificationType.Addon);
    }

    /// <summary>
    ///     Each persisted version record carries its own identity and type, because that record — not the entry
    ///     header — is what the next start rebuilds the version from.
    /// </summary>
    [Fact]
    public void ToLauncherContentState_PersistsEachVersionRecordWithItsOwnIdentityAndType()
    {
        var launcherData = new LauncherData();
        launcherData.AddOrUpdate(TestLauncherContent.Version(
            "Balance Patch",
            "2.0",
            ModificationType.Patch,
            "ShockWave",
            installed: true));

        var state = LauncherContentStateMapper.ToLauncherContentState(launcherData);

        LauncherContentEntryState entry = state.Patches.Should().ContainSingle().Subject;
        entry.ModificationType.Should().Be(ModificationType.Patch);
        LauncherContentVersionState version = entry.ModificationVersions.Should().ContainSingle().Subject;
        version.Name.Should().Be("Balance Patch");
        version.Version.Should().Be("2.0");
        version.DependenceName.Should().Be("ShockWave");
        version.ModificationType.Should().Be(ModificationType.Patch);
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
