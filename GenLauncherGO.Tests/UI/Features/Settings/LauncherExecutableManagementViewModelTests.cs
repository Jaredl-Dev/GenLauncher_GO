using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Exceptions;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Settings.ViewModels;
using GenLauncherGO.UI.Features.Startup;

namespace GenLauncherGO.Tests.UI.Features.Settings;

public sealed class LauncherExecutableManagementViewModelTests
{
    [Fact]
    public void Initialize_LockedBuiltIns_ListsBeforeCustomEntries()
    {
        using var directory = new TestDirectory();
        RecordingLauncherPreferencesService preferences = new(CreatePreferences(
            customExecutables: [new LauncherCustomExecutable("Custom", "custom.exe")]));
        IGameExecutableDiscoveryService discovery = CreateDiscovery(false);
        LauncherExecutableManagementViewModel viewModel = CreateViewModel(
            directory.Path,
            preferences,
            discovery);

        viewModel.Initialize(GameLaunchTargetKind.GameClient);

        viewModel.Entries.Should().HaveCount(4);
        viewModel.Entries[0].ExecutableName.Should().Be("generalsonlinezh.exe");
        viewModel.Entries[1].ExecutableName.Should().Be("generalszh.exe");
        viewModel.Entries[2].ExecutableName.Should().Be("generals.exe");
        viewModel.Entries[0].CanRemove.Should().BeFalse();
        viewModel.Entries[3].DisplayName.Should().Be("Custom");
        viewModel.Entries[3].IsAvailable.Should().BeFalse();
        viewModel.Entries[3].CanRemove.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Add_PersistsCustomEntryImmediatelyAndPreservesInsertionOrder(bool manageWorldBuilders)
    {
        GameLaunchTargetKind kind = Kind(manageWorldBuilders);
        using var directory = new TestDirectory();
        string gameDirectory = directory.CreateDirectory("Game");
        string executablePath = Path.Combine(gameDirectory, "second.exe");
        File.WriteAllText(executablePath, string.Empty);
        RecordingLauncherPreferencesService preferences = new(CreatePreferences(
            kind,
            customExecutables: [new LauncherCustomExecutable("First", "first.exe")]));
        LauncherExecutableManagementViewModel viewModel = CreateViewModel(
            gameDirectory,
            preferences,
            CreateDiscovery(true));
        viewModel.Initialize(kind);
        viewModel.BeginAdd();
        viewModel.DisplayName = "Second";
        viewModel.SetSelectedExecutablePath(executablePath);

        viewModel.TrySaveEditor().Should().BeTrue();

        preferences.UpdateCount.Should().Be(1);
        CustomExecutables(preferences.Current, kind).Should().Equal(
            new LauncherCustomExecutable("First", "first.exe"),
            new LauncherCustomExecutable("Second", "second.exe"));
        CustomExecutables(preferences.Current, Other(kind)).Should().BeEmpty();
        SelectedExecutable(preferences.Current, Other(kind)).Should().BeEmpty();
        viewModel.Entries.Should().Contain(entry =>
            entry.DisplayName == "Second" && entry.ExecutableName == "second.exe");
        viewModel.DisplayName.Should().BeEmpty();
        viewModel.ExecutableName.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Edit_ReplacesCustomEntryInPlaceAndUpdatesSavedSelection(bool manageWorldBuilders)
    {
        GameLaunchTargetKind kind = Kind(manageWorldBuilders);
        using var directory = new TestDirectory();
        string gameDirectory = directory.CreateDirectory("Game");
        RecordingLauncherPreferencesService preferences = new(CreatePreferences(
            kind,
            "custom.exe",
            new LauncherCustomExecutable("First", "first.exe"),
            new LauncherCustomExecutable("Custom", "custom.exe")));
        LauncherExecutableManagementViewModel viewModel = CreateViewModel(
            gameDirectory,
            preferences,
            CreateDiscovery(true));
        viewModel.Initialize(kind);
        ExecutableOption custom = viewModel.Entries.Single(entry => entry.ExecutableName == "custom.exe");
        viewModel.BeginEdit(custom);
        viewModel.DisplayName = "Edited";
        viewModel.SetSelectedExecutablePath(Path.Combine(gameDirectory, "replacement.exe"));

        viewModel.TrySaveEditor().Should().BeTrue();

        CustomExecutables(preferences.Current, kind).Should().Equal(
            new LauncherCustomExecutable("First", "first.exe"),
            new LauncherCustomExecutable("Edited", "replacement.exe"));
        SelectedExecutable(preferences.Current, kind).Should().Be("replacement.exe");
        CustomExecutables(preferences.Current, Other(kind)).Should().BeEmpty();
        SelectedExecutable(preferences.Current, Other(kind)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, "generalsonlinezh.exe")]
    [InlineData(true, "WorldBuilder.exe")]
    public void Remove_SelectedCustomEntry_UnregistersWithoutDeletingFile(
        bool manageWorldBuilders,
        string expectedFallbackExecutable)
    {
        GameLaunchTargetKind kind = Kind(manageWorldBuilders);
        using var directory = new TestDirectory();
        string gameDirectory = directory.CreateDirectory("Game");
        string executablePath = Path.Combine(gameDirectory, "custom.exe");
        File.WriteAllText(executablePath, string.Empty);
        RecordingLauncherPreferencesService preferences = new(CreatePreferences(
            kind,
            "custom.exe",
            new LauncherCustomExecutable("Custom", "custom.exe")));
        LauncherExecutableManagementViewModel viewModel = CreateViewModel(
            gameDirectory,
            preferences,
            CreateDiscovery(true));
        viewModel.Initialize(kind);

        viewModel.Remove(viewModel.Entries.Single(entry => entry.ExecutableName == "custom.exe"))
            .Should().BeTrue();

        CustomExecutables(preferences.Current, kind).Should().BeEmpty();
        SelectedExecutable(preferences.Current, kind).Should().Be(expectedFallbackExecutable);
        SelectedExecutable(preferences.Current, Other(kind)).Should().BeEmpty();
        File.Exists(executablePath).Should().BeTrue();
        viewModel.Entries.Should().OnlyContain(entry => !entry.CanRemove);
    }

    [Fact]
    public void Remove_WhenTheFirstRemainingEntryIsUnavailable_SelectsTheFirstAvailableOne()
    {
        using var directory = new TestDirectory();
        RecordingLauncherPreferencesService preferences = new(CreatePreferences(
            selectedExecutable: "custom.exe",
            customExecutables: [new LauncherCustomExecutable("Custom", "custom.exe")]));
        LauncherExecutableManagementViewModel viewModel = CreateViewModel(
            directory.Path,
            preferences,
            CreateDiscovery(true, false));
        viewModel.Initialize(GameLaunchTargetKind.GameClient);

        viewModel.Remove(viewModel.Entries.Single(entry => entry.ExecutableName == "custom.exe"))
            .Should().BeTrue();

        preferences.Current.Games.ZeroHour.SelectedGameClient.Should().Be("generalszh.exe");
    }

    [Fact]
    public void Remove_WhenNothingRemains_ClearsTheSavedSelection()
    {
        using var directory = new TestDirectory();
        RecordingLauncherPreferencesService preferences = new(CreatePreferences(
            selectedExecutable: "custom.exe",
            customExecutables: [new LauncherCustomExecutable("Custom", "custom.exe")]));
        IGameExecutableDiscoveryService discovery = Substitute.For<IGameExecutableDiscoveryService>();
        discovery.GetGameClients().Returns(Array.Empty<BuiltInExecutable>());
        discovery.GetWorldBuilders().Returns(Array.Empty<BuiltInExecutable>());
        discovery.IsExecutableAvailable(Arg.Any<string?>()).Returns(true);
        LauncherExecutableManagementViewModel viewModel = CreateViewModel(
            directory.Path,
            preferences,
            discovery);
        viewModel.Initialize(GameLaunchTargetKind.GameClient);

        viewModel.Remove(viewModel.Entries.Single(entry => entry.ExecutableName == "custom.exe"))
            .Should().BeTrue();

        preferences.Current.Games.ZeroHour.SelectedGameClient.Should().BeEmpty();
        viewModel.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Remove_BuiltInRegistration_IsRefusedWithoutTouchingPreferences()
    {
        using var directory = new TestDirectory();
        RecordingLauncherPreferencesService preferences = new(CreatePreferences(
            selectedExecutable: "generalsonlinezh.exe"));
        LauncherExecutableManagementViewModel viewModel = CreateViewModel(
            directory.Path,
            preferences,
            CreateDiscovery(true));
        viewModel.Initialize(GameLaunchTargetKind.GameClient);

        viewModel.Remove(viewModel.Entries.Single(entry => entry.ExecutableName == "generalsonlinezh.exe"))
            .Should().BeFalse();

        preferences.UpdateCount.Should().Be(0);
        preferences.Current.Games.ZeroHour.SelectedGameClient.Should().Be("generalsonlinezh.exe");
        viewModel.Entries.Should().HaveCount(3);
    }

    [Fact]
    public void Remove_WhenPersistenceFails_KeepsTheEntryAndReportsSaveFailure()
    {
        using var directory = new TestDirectory();
        RecordingLauncherPreferencesService preferences = new(CreatePreferences(
            customExecutables: [new LauncherCustomExecutable("Custom", "custom.exe")]))
        {
            UpdateFailure = new LauncherPreferencesPersistenceException(new IOException("locked"))
        };
        LauncherExecutableManagementViewModel viewModel = CreateViewModel(
            directory.Path,
            preferences,
            CreateDiscovery(true));
        viewModel.Initialize(GameLaunchTargetKind.GameClient);

        viewModel.Remove(viewModel.Entries.Single(entry => entry.ExecutableName == "custom.exe"))
            .Should().BeFalse();

        viewModel.LastPersistenceSaveFailed.Should().BeTrue();
        viewModel.Entries.Should().Contain(entry => entry.ExecutableName == "custom.exe");
        preferences.Current.Games.ZeroHour.CustomGameClients.Should().Equal(
            new LauncherCustomExecutable("Custom", "custom.exe"));
    }

    [Fact]
    public void SetSelectedExecutablePath_RejectsFilesOutsideActiveGameRoot()
    {
        using var directory = new TestDirectory();
        string gameDirectory = directory.CreateDirectory("Game");
        string externalPath = directory.CreateFile("external.exe", string.Empty);
        LauncherExecutableManagementViewModel viewModel = CreateViewModel(
            gameDirectory,
            new RecordingLauncherPreferencesService(CreatePreferences()),
            CreateDiscovery(true));
        viewModel.Initialize(GameLaunchTargetKind.GameClient);

        viewModel.SetSelectedExecutablePath(externalPath);

        viewModel.ExecutableName.Should().BeEmpty();
        viewModel.ValidationMessage.Should().Be("Must be in game root");
    }

    [Fact]
    public void SetSelectedExecutablePath_WithoutADraftName_FillsTheDisplayNameFromTheFile()
    {
        using var directory = new TestDirectory();
        string gameDirectory = directory.CreateDirectory("Game");
        LauncherExecutableManagementViewModel viewModel = CreateViewModel(
            gameDirectory,
            new RecordingLauncherPreferencesService(CreatePreferences()),
            CreateDiscovery(true));
        viewModel.Initialize(GameLaunchTargetKind.GameClient);
        viewModel.BeginAdd();

        viewModel.SetSelectedExecutablePath(Path.Combine(gameDirectory, "my-client.exe"));

        viewModel.DisplayName.Should().Be("my-client");
        viewModel.ExecutableName.Should().Be("my-client.exe");
        viewModel.ValidationMessage.Should().BeEmpty();
        viewModel.CanSave.Should().BeTrue();
    }

    [Fact]
    public void SetSelectedExecutablePath_WhenTheFileIsNotAvailable_ReportsItAsUnavailable()
    {
        using var directory = new TestDirectory();
        string gameDirectory = directory.CreateDirectory("Game");
        LauncherExecutableManagementViewModel viewModel = CreateViewModel(
            gameDirectory,
            new RecordingLauncherPreferencesService(CreatePreferences()),
            CreateDiscovery(false));
        viewModel.Initialize(GameLaunchTargetKind.GameClient);
        viewModel.BeginAdd();

        viewModel.SetSelectedExecutablePath(Path.Combine(gameDirectory, "custom.exe"));

        viewModel.ValidationMessage.Should().Be("Unavailable");
        viewModel.ExecutableName.Should().BeEmpty();
        viewModel.CanSave.Should().BeFalse();
    }

    [Fact]
    public void SetSelectedExecutablePath_WithANonExecutableFile_ReportsItAsUnavailable()
    {
        using var directory = new TestDirectory();
        string gameDirectory = directory.CreateDirectory("Game");
        LauncherExecutableManagementViewModel viewModel = CreateViewModel(
            gameDirectory,
            new RecordingLauncherPreferencesService(CreatePreferences()),
            CreateDiscovery(true));
        viewModel.Initialize(GameLaunchTargetKind.GameClient);
        viewModel.BeginAdd();

        viewModel.SetSelectedExecutablePath(Path.Combine(gameDirectory, "readme.txt"));

        viewModel.ValidationMessage.Should().Be("Unavailable");
        viewModel.ExecutableName.Should().BeEmpty();
    }

    [Fact]
    public void Add_DuplicateDisplayName_IsRejected()
    {
        using var directory = new TestDirectory();
        string gameDirectory = directory.CreateDirectory("Game");
        RecordingLauncherPreferencesService preferences = new(CreatePreferences());
        LauncherExecutableManagementViewModel viewModel = CreateViewModel(
            gameDirectory,
            preferences,
            CreateDiscovery(true));
        viewModel.Initialize(GameLaunchTargetKind.GameClient);
        viewModel.BeginAdd();
        viewModel.DisplayName = "thesuperhackers";
        viewModel.SetSelectedExecutablePath(Path.Combine(gameDirectory, "custom.exe"));

        viewModel.TrySaveEditor().Should().BeFalse();

        viewModel.ValidationMessage.Should().Be("Duplicate name");
        preferences.UpdateCount.Should().Be(0);
    }

    [Fact]
    public void Add_DuplicateExecutableFileName_IsRejected()
    {
        using var directory = new TestDirectory();
        string gameDirectory = directory.CreateDirectory("Game");
        RecordingLauncherPreferencesService preferences = new(CreatePreferences());
        LauncherExecutableManagementViewModel viewModel = CreateViewModel(
            gameDirectory,
            preferences,
            CreateDiscovery(true));
        viewModel.Initialize(GameLaunchTargetKind.GameClient);
        viewModel.BeginAdd();
        viewModel.DisplayName = "Different";
        viewModel.SetSelectedExecutablePath(Path.Combine(gameDirectory, "GENERALSZH.EXE"));

        viewModel.TrySaveEditor().Should().BeFalse();

        viewModel.ValidationMessage.Should().Be("Duplicate file");
        preferences.UpdateCount.Should().Be(0);
    }

    [Fact]
    public void AddPersistenceFailure_KeepsPresentationAndReportsSaveFailure()
    {
        using var directory = new TestDirectory();
        string gameDirectory = directory.CreateDirectory("Game");
        RecordingLauncherPreferencesService preferences = new(CreatePreferences())
        {
            UpdateFailure = new LauncherPreferencesPersistenceException(new IOException("locked"))
        };
        LauncherExecutableManagementViewModel viewModel = CreateViewModel(
            gameDirectory,
            preferences,
            CreateDiscovery(true));
        viewModel.Initialize(GameLaunchTargetKind.GameClient);
        viewModel.BeginAdd();
        viewModel.DisplayName = "Custom";
        viewModel.SetSelectedExecutablePath(Path.Combine(gameDirectory, "custom.exe"));

        viewModel.TrySaveEditor().Should().BeFalse();

        viewModel.LastPersistenceSaveFailed.Should().BeTrue();
        preferences.Current.Games.ZeroHour.CustomGameClients.Should().BeEmpty();
        viewModel.Entries.Should().HaveCount(3);
        viewModel.DisplayName.Should().Be("Custom");
        viewModel.ExecutableName.Should().Be("custom.exe");
    }

    [Fact]
    public void Search_FiltersByDisplayNameOrFilenameWithoutChangingOrder()
    {
        using var directory = new TestDirectory();
        LauncherExecutableManagementViewModel viewModel = CreateSearchViewModel(directory);

        viewModel.SearchText = "client";

        viewModel.Entries.Select(entry => entry.DisplayName).Should().Equal("Alpha Client", "Beta Client");
    }

    [Fact]
    public void Search_WithSurroundingWhitespace_MatchesTheTrimmedTerm()
    {
        using var directory = new TestDirectory();
        LauncherExecutableManagementViewModel viewModel = CreateSearchViewModel(directory);

        viewModel.SearchText = "  special  ";

        viewModel.Entries.Select(entry => entry.DisplayName).Should().Equal("Beta Client");
    }

    [Fact]
    public void Search_WhenCleared_RestoresEveryRegisteredEntry()
    {
        using var directory = new TestDirectory();
        LauncherExecutableManagementViewModel viewModel = CreateSearchViewModel(directory);
        viewModel.SearchText = "special";

        viewModel.SearchText = string.Empty;

        viewModel.Entries.Select(entry => entry.DisplayName).Should().Equal(
            "GeneralsOnline",
            "TheSuperHackers",
            "Retail",
            "Alpha Client",
            "Beta Client");
    }

    private static LauncherExecutableManagementViewModel CreateSearchViewModel(TestDirectory directory)
    {
        RecordingLauncherPreferencesService preferences = new(CreatePreferences(
            customExecutables:
            [
                new LauncherCustomExecutable("Alpha Client", "alpha.exe"),
                new LauncherCustomExecutable("Beta Client", "special-beta.exe")
            ]));
        LauncherExecutableManagementViewModel viewModel = CreateViewModel(
            directory.Path,
            preferences,
            CreateDiscovery(true));
        viewModel.Initialize(GameLaunchTargetKind.GameClient);
        return viewModel;
    }

    private static LauncherExecutableManagementViewModel CreateViewModel(
        string gameDirectory,
        ILauncherPreferencesService preferences,
        IGameExecutableDiscoveryService discovery)
    {
        LauncherRuntimeContext runtimeContext = TestLauncherRuntimeContext.Create(
            TestLauncherPaths.Create(gameDirectory));
        FakeStringLocalizer localizer = new(TestLocalizedStrings.Settings);
        var selectionService = new LauncherExecutableSelectionService(
            discovery,
            runtimeContext,
            preferences,
            localizer);
        return new LauncherExecutableManagementViewModel(
            preferences,
            discovery,
            selectionService,
            runtimeContext,
            localizer);
    }

    private static IGameExecutableDiscoveryService CreateDiscovery(
        bool customAvailable,
        bool firstBuiltInAvailable = true)
    {
        IGameExecutableDiscoveryService discovery = Substitute.For<IGameExecutableDiscoveryService>();
        discovery.GetGameClients().Returns(new[]
        {
            new BuiltInExecutable("generalsonlinezh.exe", firstBuiltInAvailable),
            new BuiltInExecutable("generalszh.exe", true),
            new BuiltInExecutable("generals.exe", true)
        });
        discovery.GetWorldBuilders().Returns(new[]
        {
            new BuiltInExecutable("WorldBuilder.exe", firstBuiltInAvailable),
            new BuiltInExecutable("worldbuilderzh.exe", true)
        });
        discovery.IsExecutableAvailable(Arg.Any<string?>()).Returns(customAvailable);
        return discovery;
    }

    private static LauncherPreferences CreatePreferences(
        GameLaunchTargetKind kind = GameLaunchTargetKind.GameClient,
        string selectedExecutable = "",
        params LauncherCustomExecutable[] customExecutables)
    {
        LauncherGamePreferences gamePreferences =
            kind == GameLaunchTargetKind.GameClient
                ? new LauncherGamePreferences
                {
                    SelectedGameClient = selectedExecutable,
                    CustomGameClients = customExecutables
                }
                : new LauncherGamePreferences
                {
                    SelectedWorldBuilder = selectedExecutable,
                    CustomWorldBuilders = customExecutables
                };

        return new LauncherPreferences
        {
            Games = new LauncherGamePreferencesSet { ZeroHour = gamePreferences }
        };
    }

    private static IReadOnlyList<LauncherCustomExecutable> CustomExecutables(
        LauncherPreferences preferences,
        GameLaunchTargetKind kind)
    {
        return kind == GameLaunchTargetKind.GameClient
            ? preferences.Games.ZeroHour.CustomGameClients
            : preferences.Games.ZeroHour.CustomWorldBuilders;
    }

    private static string SelectedExecutable(
        LauncherPreferences preferences,
        GameLaunchTargetKind kind)
    {
        return kind == GameLaunchTargetKind.GameClient
            ? preferences.Games.ZeroHour.SelectedGameClient
            : preferences.Games.ZeroHour.SelectedWorldBuilder;
    }

    private static GameLaunchTargetKind Kind(bool manageWorldBuilders)
    {
        return manageWorldBuilders
            ? GameLaunchTargetKind.WorldBuilder
            : GameLaunchTargetKind.GameClient;
    }

    private static GameLaunchTargetKind Other(GameLaunchTargetKind kind)
    {
        return kind == GameLaunchTargetKind.GameClient
            ? GameLaunchTargetKind.WorldBuilder
            : GameLaunchTargetKind.GameClient;
    }
}
