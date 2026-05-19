using System;
using System.IO;
using GenLauncherGO.Core.Settings.Exceptions;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Persistence.Services;
using GenLauncherGO.Infrastructure.Settings.Models;
using GenLauncherGO.Infrastructure.Settings.Services;
using GenLauncherGO.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Settings.Services;

public sealed class PreferencesServiceTests
{
    [Fact]
    public void Current_WhenPreferencesFileIsMissing_ReturnsCurrentSchemaDefaults()
    {
        using var directory = new TestDirectory();

        PreferencesService service = CreateService(
            Path.Combine(directory.Path, "LauncherPreferences.yaml"));

        service.Current.Should().Be(new LauncherPreferences());
    }

    [Fact]
    public void Current_WhenPreferencesFileIsMalformed_ReturnsDefaults()
    {
        using var directory = new TestDirectory();
        string preferencesFilePath = directory.CreateFile(
            "LauncherPreferences.yaml",
            "Installations: [");

        PreferencesService service = CreateService(preferencesFilePath);

        service.Current.Should().Be(new LauncherPreferences());
        string resetYaml = File.ReadAllText(preferencesFilePath);
        resetYaml.Should().Contain("SchemaVersion: 1");
        resetYaml.Should().NotContain("Installations: [");
    }

    [Fact]
    public void Current_MigratesUnversionedFlatPreferencesToCurrentSchema()
    {
        using var directory = new TestDirectory();
        string preferencesFilePath = directory.CreateFile(
            "LauncherPreferences.yaml",
            """
            LaunchesCount: 7
            AutoDeleteOldVersions: true
            SelectedGameClient: generalszh.exe
            """);

        PreferencesService service = CreateService(preferencesFilePath);

        service.Current.Shared.AutoDeleteOldVersions.Should().BeTrue();
        service.Current.Games.ZeroHour.LaunchesCount.Should().Be(7);
        service.Current.Games.ZeroHour.SelectedGameClient.Should().Be("generalszh.exe");

        string migratedYaml = File.ReadAllText(preferencesFilePath);
        migratedYaml.Should().Contain("SchemaVersion: 1");
        migratedYaml.Should().Contain("Shared:");
        migratedYaml.Should().Contain("Games:");
        File.ReadAllLines(preferencesFilePath).Should().NotContain("LaunchesCount: 7");
        File.ReadAllLines(preferencesFilePath).Should().NotContain("AutoDeleteOldVersions: true");
    }

    [Fact]
    public void Current_MigratesSchemaZeroFlatPreferencesToCurrentSchema()
    {
        using var directory = new TestDirectory();
        string preferencesFilePath = directory.CreateFile(
            "LauncherPreferences.yaml",
            """
            SchemaVersion: 0
            LaunchesCount: 4
            AutoDeleteOldVersions: true
            SelectedGameClient: generalszh.exe
            """);

        PreferencesService service = CreateService(preferencesFilePath);

        service.Current.Shared.AutoDeleteOldVersions.Should().BeTrue();
        service.Current.Games.ZeroHour.LaunchesCount.Should().Be(4);
        service.Current.Games.ZeroHour.SelectedGameClient.Should().Be("generalszh.exe");
        string migratedYaml = File.ReadAllText(preferencesFilePath);
        migratedYaml.Should().Contain("SchemaVersion: 1");
        migratedYaml.Should().NotContain("SchemaVersion: 0");
        File.ReadAllLines(preferencesFilePath).Should().NotContain("LaunchesCount: 4");
    }

    [Fact]
    public void Current_WhenSchemaIsNewerThanSupported_ResetsToCurrentSchemaDefaults()
    {
        using var directory = new TestDirectory();
        const string futurePreferences =
            """
            SchemaVersion: 2
            Shared:
              AutoDeleteOldVersions: true
            FutureSetting: keep-me
            """;
        string preferencesFilePath = directory.CreateFile(
            "LauncherPreferences.yaml",
            futurePreferences);

        PreferencesService service = CreateService(preferencesFilePath);

        service.Current.Should().Be(new LauncherPreferences());
        string resetYaml = File.ReadAllText(preferencesFilePath);
        resetYaml.Should().Contain("SchemaVersion: 1");
        resetYaml.Should().NotContain("FutureSetting");
    }

    [Fact]
    public void Current_WhenFutureSchemaHasIncompatibleCurrentFieldShape_ResetsAndRewritesDefaults()
    {
        using var directory = new TestDirectory();
        string preferencesFilePath = directory.CreateFile(
            "LauncherPreferences.yaml",
            """
            SchemaVersion: 2
            Shared:
            - incompatible-future-shape
            """);

        PreferencesService service = CreateService(preferencesFilePath);

        service.Current.Should().Be(new LauncherPreferences());
        string resetYaml = File.ReadAllText(preferencesFilePath);
        resetYaml.Should().Contain("SchemaVersion: 1");
        resetYaml.Should().NotContain("SchemaVersion: 2");
        resetYaml.Should().NotContain("incompatible-future-shape");
    }

    [Fact]
    public void Current_WhenCurrentSchemaHasIncompatibleFieldShape_ResetsAndRewritesDefaults()
    {
        using var directory = new TestDirectory();
        string preferencesFilePath = directory.CreateFile(
            "LauncherPreferences.yaml",
            """
            SchemaVersion: 1
            Shared:
            - incompatible-current-shape
            """);

        PreferencesService service = CreateService(preferencesFilePath);

        service.Current.Should().Be(new LauncherPreferences());
        string resetYaml = File.ReadAllText(preferencesFilePath);
        resetYaml.Should().Contain("SchemaVersion: 1");
        resetYaml.Should().NotContain("incompatible-current-shape");
    }

    [Fact]
    public void Current_WhenUnversionedSchemaIsUnknown_ResetsToCurrentSchemaDefaults()
    {
        using var directory = new TestDirectory();
        const string unknownPreferences = "FutureSetting: keep-me";
        string preferencesFilePath = directory.CreateFile(
            "LauncherPreferences.yaml",
            unknownPreferences);

        PreferencesService service = CreateService(preferencesFilePath);

        service.Current.Should().Be(new LauncherPreferences());
        string resetYaml = File.ReadAllText(preferencesFilePath);
        resetYaml.Should().Contain("SchemaVersion: 1");
        resetYaml.Should().NotContain("FutureSetting");
    }

    [Fact]
    public void Current_NormalizesNullableCurrentSchemaMembers()
    {
        using var directory = new TestDirectory();
        string preferencesFilePath = directory.CreateFile(
            "LauncherPreferences.yaml",
            """
            SchemaVersion: 1
            Installations:
              Generals:
              ZeroHour:
            LastSelectedGame: Unknown
            Shared:
              AutoDeleteOldVersions: true
            Games:
              Generals:
                LaunchesCount: -2
                SelectedGameClient:
              ZeroHour:
                GameArguments:
            """);

        PreferencesService service = CreateService(preferencesFilePath);

        service.Current.Installations.Should().Be(new LauncherInstallations());
        service.Current.LastSelectedGame.Should().BeNull();
        service.Current.Shared.AutoDeleteOldVersions.Should().BeTrue();
        service.Current.Games.Generals.LaunchesCount.Should().Be(0);
        service.Current.Games.Generals.SelectedGameClient.Should().BeEmpty();
        service.Current.Games.ZeroHour.GameArguments.Should().BeEmpty();
    }

    [Fact]
    public void Update_PersistsAndReloadsStandaloneSchema()
    {
        using var directory = new TestDirectory();
        string preferencesFilePath = Path.Combine(directory.Path, "LauncherPreferences.yaml");
        string generalsDirectory = directory.CreateDirectory("Generals");
        string zeroHourDirectory = directory.CreateDirectory("ZeroHour");
        PreferencesService service = CreateService(preferencesFilePath);
        var preferences = new LauncherPreferences
        {
            Installations = new LauncherInstallations
            {
                Generals = generalsDirectory + Path.DirectorySeparatorChar,
                ZeroHour = zeroHourDirectory,
            },
            LastSelectedGame = SupportedGame.ZeroHour,
            Shared = new LauncherSharedPreferences
            {
                AutoDeleteOldVersions = true,
                HideLauncherAfterGameStart = true,
                UseEnglishLanguage = true,
            },
            Games = new LauncherGamePreferencesSet
            {
                Generals = new LauncherGamePreferences
                {
                    LaunchesCount = 3,
                    SelectedGameClient = " generalsv.exe ",
                    CustomGameClients = new[]
                    {
                        new LauncherCustomExecutable("Generals Client A", "generals-custom-a.exe"),
                        new LauncherCustomExecutable("Generals Client B", "generals-custom-b.exe"),
                    },
                },
                ZeroHour = new LauncherGamePreferences
                {
                    LaunchesCount = 7,
                    SelectedGameClient = "generalszh.exe",
                    SelectedWorldBuilder = "worldbuilderzh.exe",
                    GameArguments = "-quickstart",
                    WorldBuilderArguments = "-wb",
                    CustomGameClients = new[]
                    {
                        new LauncherCustomExecutable("Zero Hour Client", "zh-custom.exe"),
                    },
                    CustomWorldBuilders = new[]
                    {
                        new LauncherCustomExecutable("Map Editor", "map-editor.exe"),
                    },
                },
            },
        };

        service.Update(preferences);
        PreferencesService reloadedService = CreateService(preferencesFilePath);

        LauncherPreferences persisted = reloadedService.Current;
        persisted.Installations.Generals.Should().Be(Path.GetFullPath(generalsDirectory));
        persisted.Installations.ZeroHour.Should().Be(Path.GetFullPath(zeroHourDirectory));
        persisted.LastSelectedGame.Should().Be(SupportedGame.ZeroHour);
        persisted.Shared.Should().Be(preferences.Shared);
        persisted.Games.Generals.SelectedGameClient.Should().Be("generalsv.exe");
        persisted.Games.Generals.CustomGameClients.Should().Equal(
            preferences.Games.Generals.CustomGameClients);
        persisted.Games.ZeroHour.Should().BeEquivalentTo(preferences.Games.ZeroHour);
        persisted.Games.ZeroHour.CustomGameClients.Should().ContainSingle()
            .Which.ExecutableName.Should().Be("zh-custom.exe");
        persisted.Games.ZeroHour.CustomWorldBuilders.Should().ContainSingle()
            .Which.ExecutableName.Should().Be("map-editor.exe");

        string yaml = File.ReadAllText(preferencesFilePath);
        yaml.Should().Contain("SchemaVersion: 1");
        yaml.Should().Contain("Installations:");
        yaml.Should().Contain("LastSelectedGame: ZeroHour");
        yaml.Should().Contain("Shared:");
        yaml.Should().Contain("Games:");
        yaml.Should().Contain("CustomGameClients:");
        yaml.Should().Contain("CustomWorldBuilders:");
    }

    [Fact]
    public void Current_NormalizesCustomExecutablesPerGameAndRejectsInvalidOrDuplicateEntries()
    {
        using var directory = new TestDirectory();
        string preferencesFilePath = directory.CreateFile(
            "LauncherPreferences.yaml",
            """
            SchemaVersion: 1
            Games:
              ZeroHour:
                CustomGameClients:
                - DisplayName: First
                  ExecutableName: custom-one.exe
                - DisplayName: first
                  ExecutableName: custom-two.exe
                - DisplayName: Second
                  ExecutableName: CUSTOM-ONE.EXE
                - DisplayName: Built in
                  ExecutableName: generalszh.exe
                - DisplayName: Nested
                  ExecutableName: tools/custom.exe
                CustomWorldBuilders:
                - DisplayName: Editor
                  ExecutableName: editor.exe
              Generals:
                CustomGameClients:
                - DisplayName: Generals Custom
                  ExecutableName: custom-one.exe
            """);

        PreferencesService service = CreateService(preferencesFilePath);

        service.Current.Games.ZeroHour.CustomGameClients.Should().ContainSingle()
            .Which.Should().Be(new LauncherCustomExecutable("First", "custom-one.exe"));
        service.Current.Games.ZeroHour.CustomWorldBuilders.Should().ContainSingle()
            .Which.Should().Be(new LauncherCustomExecutable("Editor", "editor.exe"));
        service.Current.Games.Generals.CustomGameClients.Should().ContainSingle()
            .Which.Should().Be(new LauncherCustomExecutable("Generals Custom", "custom-one.exe"));
    }

    [Fact]
    public void Update_WhenPreferencesAreUnchanged_DoesNotPersistOrRaisePreferencesChanged()
    {
        using var directory = new TestDirectory();
        string preferencesFilePath = Path.Combine(directory.Path, "LauncherPreferences.yaml");
        PreferencesService service = CreateService(preferencesFilePath);
        int changedCount = 0;
        service.PreferencesChanged += (_, _) => changedCount++;

        service.Update(new LauncherPreferences());

        changedCount.Should().Be(0);
        File.Exists(preferencesFilePath).Should().BeFalse();
    }

    [Fact]
    public void Update_WhenPreferencesChange_RaisesPreferencesChangedWithNormalizedState()
    {
        using var directory = new TestDirectory();
        string preferencesFilePath = Path.Combine(directory.Path, "LauncherPreferences.yaml");
        PreferencesService service = CreateService(preferencesFilePath);
        LauncherPreferences? changedPreferences = null;
        service.PreferencesChanged += (_, current) => changedPreferences = current;
        var preferences = new LauncherPreferences
        {
            Games = new LauncherGamePreferencesSet
            {
                ZeroHour = new LauncherGamePreferences { GameArguments = "-quickstart" },
            },
        };

        service.Update(preferences);

        changedPreferences.Should().Be(service.Current);
        changedPreferences!.Games.ZeroHour.GameArguments.Should().Be("-quickstart");
    }

    [Fact]
    public void Update_WhenPreferencesCannotBePersisted_KeepsCurrentAndDoesNotPublish()
    {
        using var directory = new TestDirectory();
        string preferencesFilePath = directory.CreateDirectory("LauncherPreferences.yaml");
        PreferencesService service = CreateService(preferencesFilePath);
        LauncherPreferences? changedPreferences = null;
        service.PreferencesChanged += (_, current) => changedPreferences = current;
        var preferences = new LauncherPreferences
        {
            Shared = new LauncherSharedPreferences { AutoDeleteOldVersions = true },
        };

        Action act = () => service.Update(preferences);

        act.Should().Throw<LauncherPreferencesPersistenceException>()
            .WithInnerException<IOException>();
        service.Current.Should().Be(new LauncherPreferences());
        changedPreferences.Should().BeNull();
        Directory.Exists(preferencesFilePath).Should().BeTrue();
    }

    private static PreferencesService CreateService(string preferencesFilePath)
    {
        return new PreferencesService(
            new YamlDocumentStore<LauncherPreferencesSchemaDocument>(
                preferencesFilePath,
                new AtomicFileWriter(),
                NullLogger<YamlDocumentStore<LauncherPreferencesSchemaDocument>>.Instance),
            new YamlDocumentStore<LauncherPreferencesDocument>(
                preferencesFilePath,
                new AtomicFileWriter(),
                NullLogger<YamlDocumentStore<LauncherPreferencesDocument>>.Instance),
            new YamlDocumentStore<LegacyLauncherPreferencesDocument>(
                preferencesFilePath,
                new AtomicFileWriter(),
                NullLogger<YamlDocumentStore<LegacyLauncherPreferencesDocument>>.Instance));
    }
}
