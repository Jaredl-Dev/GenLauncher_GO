using System.IO;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Mods.Models;
using GenLauncherGO.Infrastructure.Mods.Services;
using GenLauncherGO.Infrastructure.Persistence.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Mods.Services;

public sealed class YamlLauncherContentStateStoreTests
{
    [Theory]
    [InlineData("Mod", 0)]
    [InlineData("Addon", 1)]
    [InlineData("Patch", 2)]
    [InlineData("Advertising", 3)]
    public void Load_AcceptsLegacyPersistedContentTypeNames(
        string persistedValue,
        int expectedTypeValue)
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        string documentPath = paths.LauncherDataFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(documentPath)!);
        File.WriteAllText(
            documentPath,
            $"""
             Addons: []
             Modifications:
             - ModificationType: {persistedValue}
               Name: Compatibility Entry
               DependenceName: Original Game
               Installed: true
               IsSelected: false
               NumberInList: 0
               ModificationVersions:
               - ModificationType: {persistedValue}
                 Name: Compatibility Entry
                 Version: 1.0
                 DependenceName: Original Game
                 Installed: true
                 IsSelected: false
                 ContentSourceKind: Manual
             Patches: []
             """);
        var store = new YamlLauncherContentStateStore(
            new AtomicFileWriter(),
            NullLogger<YamlDocumentStore<LauncherContentState>>.Instance);

        LauncherContentState state = store.Load(paths);

        LauncherContentEntryState entry = state.Modifications.Should().ContainSingle().Subject;
        entry.ModificationType.Should().Be((ModificationType)expectedTypeValue);
        entry.DependenceName.Should().Be("Original Game");
        LauncherContentVersionState version = entry.ModificationVersions.Should().ContainSingle().Subject;
        version.ModificationType.Should().Be((ModificationType)expectedTypeValue);
        version.Name.Should().Be("Compatibility Entry");
        version.Version.Should().Be("1.0");
        version.DependenceName.Should().Be("Original Game");
    }

    [Theory]
    [InlineData(0, "Mod")]
    [InlineData(1, "Addon")]
    [InlineData(2, "Patch")]
    [InlineData(3, "Advertising")]
    public void Save_PreservesLegacyPersistedContentTypeNames(
        int contentTypeValue,
        string persistedValue)
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        string documentPath = paths.LauncherDataFilePath;
        var store = new YamlLauncherContentStateStore(
            new AtomicFileWriter(),
            NullLogger<YamlDocumentStore<LauncherContentState>>.Instance);
        var state = new LauncherContentState
        {
            Modifications =
            {
                new LauncherContentEntryState
                {
                    ModificationType = (ModificationType)contentTypeValue,
                    Name = "Compatibility Entry",
                    DependenceName = "Original Game",
                    ModificationVersions =
                    {
                        new LauncherContentVersionState
                        {
                            ModificationType = (ModificationType)contentTypeValue,
                            Name = "Compatibility Entry",
                            Version = "1.0",
                            DependenceName = "Original Game"
                        }
                    }
                }
            }
        };

        store.Save(paths, state);

        string yaml = File.ReadAllText(documentPath);
        yaml.Should().Contain($"ModificationType: {persistedValue}", Exactly.Twice());
        yaml.Should().Contain("DependenceName: Original Game", Exactly.Twice());
    }

    [Fact]
    public void Load_UsesEmptyContentStateAsDefaultDocument()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        var store = new YamlLauncherContentStateStore(
            new AtomicFileWriter(),
            NullLogger<YamlDocumentStore<LauncherContentState>>.Instance);

        LauncherContentState state = store.Load(paths);

        state.Modifications.Should().BeEmpty();
        state.Addons.Should().BeEmpty();
        state.Patches.Should().BeEmpty();
    }

    [Fact]
    public void Save_KeepsIdenticalContentKeysIsolatedByGame()
    {
        using var directory = new TestDirectory();
        LauncherPaths generalsPaths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        LauncherPaths zeroHourPaths = TestLauncherPaths.Create(directory);
        var store = new YamlLauncherContentStateStore(
            new AtomicFileWriter(),
            NullLogger<YamlDocumentStore<LauncherContentState>>.Instance);
        LauncherContentState generalsState = CreateState("Shared Key", "Generals Version");
        LauncherContentState zeroHourState = CreateState("Shared Key", "Zero Hour Version");

        store.Save(generalsPaths, generalsState);
        store.Save(zeroHourPaths, zeroHourState);

        store.Load(generalsPaths).Modifications.Should().ContainSingle()
            .Which.ModificationVersions.Should().ContainSingle()
            .Which.Version.Should().Be("Generals Version");
        store.Load(zeroHourPaths).Modifications.Should().ContainSingle()
            .Which.ModificationVersions.Should().ContainSingle()
            .Which.Version.Should().Be("Zero Hour Version");
        generalsPaths.LauncherDataFilePath.Should().NotBe(zeroHourPaths.LauncherDataFilePath);
    }

    [Fact]
    public void Save_PreservesLauncherContentYamlKeysAndEnumValues()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        string documentPath = paths.LauncherDataFilePath;
        var store = new YamlLauncherContentStateStore(
            new AtomicFileWriter(),
            NullLogger<YamlDocumentStore<LauncherContentState>>.Instance);
        var state = new LauncherContentState
        {
            Modifications =
            {
                new LauncherContentEntryState
                {
                    ModificationType = ModificationType.Mod,
                    Name = "ShockWave",
                    DependenceName = "Original game",
                    Installed = true,
                    IsSelected = true,
                    NumberInList = 3,
                    ModificationVersions =
                    {
                        new LauncherContentVersionState
                        {
                            ModificationType = ModificationType.Mod,
                            Name = "ShockWave",
                            Version = "1.2",
                            DependenceName = "Original game",
                            Installed = true,
                            IsSelected = true,
                            DownloadSuspended = true,
                            SuspendedProgressPercentage = 42.5,
                            ContentSourceKind = ContentSourceKind.Manual
                        }
                    }
                }
            }
        };

        store.Save(paths, state);

        string yaml = File.ReadAllText(documentPath);
        yaml.Should().Contain("Addons:");
        yaml.Should().Contain("Modifications:");
        yaml.Should().Contain("Patches:");
        yaml.Should().Contain("ModificationType: Mod");
        yaml.Should().Contain("Name: ShockWave");
        yaml.Should().Contain("Version: 1.2");
        yaml.Should().Contain("DependenceName: Original game");
        yaml.Should().Contain("Installed: true");
        yaml.Should().Contain("IsSelected: true");
        yaml.Should().Contain("NumberInList: 3");
        yaml.Should().Contain("ModificationVersions:");
        yaml.Should().Contain("DownloadSuspended: true");
        yaml.Should().Contain("SuspendedProgressPercentage: 42.5");
        yaml.Should().Contain("ContentSourceKind: Manual");

        LauncherContentState loadedState = store.Load(paths);
        LauncherContentEntryState loadedEntry = loadedState.Modifications.Should().ContainSingle().Subject;
        loadedEntry.ModificationType.Should().Be(ModificationType.Mod);
        loadedEntry.NumberInList.Should().Be(3);
        LauncherContentVersionState loadedVersion =
            loadedEntry.ModificationVersions.Should().ContainSingle().Subject;
        loadedVersion.Version.Should().Be("1.2");
        loadedVersion.DownloadSuspended.Should().BeTrue();
        loadedVersion.SuspendedProgressPercentage.Should().Be(42.5);
        loadedVersion.ContentSourceKind.Should().Be(ContentSourceKind.Manual);
    }

    private static LauncherContentState CreateState(string name, string version)
    {
        return new LauncherContentState
        {
            Modifications =
            {
                new LauncherContentEntryState
                {
                    ModificationType = ModificationType.Mod,
                    Name = name,
                    ModificationVersions =
                    {
                        new LauncherContentVersionState
                        {
                            ModificationType = ModificationType.Mod,
                            Name = name,
                            Version = version
                        }
                    }
                }
            }
        };
    }
}
