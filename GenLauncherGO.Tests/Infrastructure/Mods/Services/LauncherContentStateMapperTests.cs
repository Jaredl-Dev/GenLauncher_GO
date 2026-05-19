using System.Collections.Generic;
using System.Linq;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Infrastructure.Mods.Models;
using GenLauncherGO.Infrastructure.Mods.Services;

namespace GenLauncherGO.Tests.Infrastructure.Mods.Services;

public sealed class LauncherContentStateMapperTests
{
    [Fact]
    public void ToLauncherDataRestoresSelectedInstalledContentState()
    {
        var state = new LauncherContentState
        {
            Modifications = new List<LauncherContentEntryState>
            {
                CreateEntry("ShockWave", string.Empty, ModificationType.Mod, "1.0", true)
            },
            Patches = new List<LauncherContentEntryState>
            {
                CreateEntry("ShockWave Patch", "ShockWave", ModificationType.Patch, "1.1", true)
            },
            Addons = new List<LauncherContentEntryState>
            {
                CreateEntry("Music Pack", "ShockWave Patch", ModificationType.Addon, "2.0", true)
            }
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
    public void ToLauncherDataRestoresPersistedEntryOrder()
    {
        var state = new LauncherContentState
        {
            Modifications = new List<LauncherContentEntryState>
            {
                CreateEntry("Second", string.Empty, ModificationType.Mod, "1.0", false, numberInList: 1),
                CreateEntry("First", string.Empty, ModificationType.Mod, "1.0", false, numberInList: 0)
            }
        };

        var launcherData = LauncherContentStateMapper.ToLauncherData(state);

        launcherData.Modifications
            .OrderBy(modification => modification.NumberInList)
            .Select(modification => modification.Name)
            .Should()
            .Equal("First", "Second");
    }

    [Fact]
    public void ToLauncherDataDoesNotSelectEntryFromStaleVersionSelection()
    {
        var state = new LauncherContentState
        {
            Modifications = new List<LauncherContentEntryState>
            {
                CreateEntry("ShockWave", string.Empty, ModificationType.Mod, "1.0", false, versionSelected: true)
            }
        };

        var launcherData = LauncherContentStateMapper.ToLauncherData(state);

        LauncherContent modification = launcherData.Modifications.Should().ContainSingle().Subject;
        modification.IsSelected.Should().BeFalse();
        modification.Versions.Should().ContainSingle().Which.Installation.IsSelected.Should().BeFalse();
    }

    [Fact]
    public void ToLauncherDataUsesEntryTypeForIncompleteLegacyChildVersionRecords()
    {
        var state = new LauncherContentState
        {
            Addons = new List<LauncherContentEntryState>
            {
                new LauncherContentEntryState
                {
                    Name = "Compatibility Addon",
                    DependenceName = "ShockWave",
                    ModificationType = ModificationType.Addon,
                    ModificationVersions = new List<LauncherContentVersionState>
                    {
                        new LauncherContentVersionState
                        {
                            Version = "1.0",
                            Installed = true,
                            ContentSourceKind = ContentSourceKind.Manual
                        }
                    }
                }
            }
        };

        var launcherData = LauncherContentStateMapper.ToLauncherData(state);

        LauncherContentVersion version = launcherData.Addons.Should().ContainSingle().Subject
            .Versions.Should().ContainSingle().Subject;
        version.Name.Should().Be("Compatibility Addon");
        version.ParentContentName.Should().Be("ShockWave");
        version.ModificationType.Should().Be(ModificationType.Addon);
        version.Installation.ContentSourceKind.Should().Be(ContentSourceKind.Manual);
    }

    [Fact]
    public void ToLauncherDataIgnoresLegacyAdvertisingVersionRecords()
    {
        var state = new LauncherContentState
        {
            Modifications = new List<LauncherContentEntryState>
            {
                new LauncherContentEntryState
                {
                    Name = "Featured",
                    ModificationType = ModificationType.Advertising,
                    ModificationVersions = new List<LauncherContentVersionState>
                    {
                        new LauncherContentVersionState
                        {
                            Name = "Featured",
                            Version = "2.0",
                            ModificationType = ModificationType.Advertising,
                            Installed = true
                        }
                    }
                }
            }
        };

        var launcherData = LauncherContentStateMapper.ToLauncherData(state);

        launcherData.Modifications.Should().BeEmpty();
        launcherData.Patches.Should().BeEmpty();
        launcherData.Addons.Should().BeEmpty();
    }

    [Fact]
    public void ToLauncherContentStatePersistsAddedRepositoryModsButFiltersUninstalledChildren()
    {
        var launcherData = new LauncherData();
        launcherData.AddOrUpdate(new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation { Installed = true },
            ModificationType = ModificationType.Mod,
            Name = "Installed",
            Version = "1.0",
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
    public void ToLauncherContentStateDoesNotPersistVersionSelectionForUnselectedEntry()
    {
        var launcherData = new LauncherData();
        launcherData.AddOrUpdate(new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation { Installed = true, IsSelected = true },
            ModificationType = ModificationType.Mod,
            Name = "Installed",
            Version = "1.0",
        });
        launcherData.Modifications[0].IsSelected = false;

        var state = LauncherContentStateMapper.ToLauncherContentState(launcherData);

        LauncherContentEntryState entry = state.Modifications.Should().ContainSingle().Subject;
        entry.IsSelected.Should().BeFalse();
        entry.ModificationVersions.Should().ContainSingle().Which.IsSelected.Should().BeFalse();
    }

    [Fact]
    public void ToLauncherContentStatePersistsEntryOrder()
    {
        var launcherData = new LauncherData();
        launcherData.AddOrUpdate(new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation { Installed = true },
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = "1.0",
        });
        launcherData.Modifications[0].NumberInList = 7;

        var state = LauncherContentStateMapper.ToLauncherContentState(launcherData);

        state.Modifications.Should().ContainSingle().Which.NumberInList.Should().Be(7);
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
            ModificationVersions = new List<LauncherContentVersionState>
            {
                new LauncherContentVersionState
                {
                    Version = version,
                    Installed = true,
                    IsSelected = versionSelected ?? selected,
                    ContentSourceKind = ContentSourceKind.Manual
                }
            }
        };
    }
}
