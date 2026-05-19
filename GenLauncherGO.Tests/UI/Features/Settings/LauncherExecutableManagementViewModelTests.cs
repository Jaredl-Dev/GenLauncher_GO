using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Exceptions;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Settings.Models;
using GenLauncherGO.UI.Features.Settings.ViewModels;
using GenLauncherGO.UI.Features.Startup;

namespace GenLauncherGO.Tests.UI.Features.Settings;

public sealed class LauncherExecutableManagementViewModelTests
{
    [Fact]
    public void InitializeListsLockedBuiltInsBeforeRegisteredCustomEntries()
    {
        using var directory = new TestDirectory();
        RecordingPreferencesService preferences = new(CreatePreferences(
            customGameClients: new[] { new LauncherCustomExecutable("Custom", "custom.exe") }));
        IGameExecutableDiscoveryService discovery = CreateDiscovery(customAvailable: false);
        LauncherExecutableManagementViewModel viewModel = CreateViewModel(
            directory.Path,
            preferences,
            discovery);

        viewModel.Initialize(LauncherExecutableManagementKind.GameClient);

        viewModel.Entries.Should().HaveCount(3);
        viewModel.Entries[0].ExecutableName.Should().Be("generalszh.exe");
        viewModel.Entries[1].ExecutableName.Should().Be("generalsonlinezh.exe");
        viewModel.Entries[0].CanRemove.Should().BeFalse();
        viewModel.Entries[2].DisplayName.Should().Be("Custom");
        viewModel.Entries[2].IsAvailable.Should().BeFalse();
        viewModel.Entries[2].CanRemove.Should().BeTrue();
    }

    [Fact]
    public void AddPersistsCustomEntryImmediatelyAndPreservesInsertionOrder()
    {
        using var directory = new TestDirectory();
        string gameDirectory = directory.CreateDirectory("Game");
        string executablePath = Path.Combine(gameDirectory, "second.exe");
        File.WriteAllText(executablePath, string.Empty);
        RecordingPreferencesService preferences = new(CreatePreferences(
            customGameClients: new[] { new LauncherCustomExecutable("First", "first.exe") }));
        IGameExecutableDiscoveryService discovery = CreateDiscovery(customAvailable: true);
        LauncherExecutableManagementViewModel viewModel = CreateViewModel(
            gameDirectory,
            preferences,
            discovery);
        viewModel.Initialize(LauncherExecutableManagementKind.GameClient);
        viewModel.BeginAdd();
        viewModel.DisplayName = "Second";
        viewModel.SetSelectedExecutablePath(executablePath);

        viewModel.TrySaveEditor().Should().BeTrue();

        preferences.UpdateCount.Should().Be(1);
        preferences.Current.Games.ZeroHour.CustomGameClients.Should().Equal(
            new LauncherCustomExecutable("First", "first.exe"),
            new LauncherCustomExecutable("Second", "second.exe"));
        viewModel.Entries.Should().Contain(entry =>
            entry.DisplayName == "Second" && entry.ExecutableName == "second.exe");
        viewModel.DisplayName.Should().BeEmpty();
        viewModel.ExecutableName.Should().BeEmpty();
    }

    [Fact]
    public void SetSelectedExecutablePathRejectsFilesOutsideActiveGameRoot()
    {
        using var directory = new TestDirectory();
        string gameDirectory = directory.CreateDirectory("Game");
        string externalPath = directory.CreateFile("external.exe", string.Empty);
        LauncherExecutableManagementViewModel viewModel = CreateViewModel(
            gameDirectory,
            new RecordingPreferencesService(CreatePreferences()),
            CreateDiscovery(customAvailable: true));
        viewModel.Initialize(LauncherExecutableManagementKind.GameClient);

        viewModel.SetSelectedExecutablePath(externalPath);

        viewModel.ExecutableName.Should().BeEmpty();
        viewModel.ValidationMessage.Should().Be("Must be in game root");
    }

    [Fact]
    public void AddRejectsCaseInsensitiveBuiltInNameAndFilenameCollisions()
    {
        using var directory = new TestDirectory();
        string gameDirectory = directory.CreateDirectory("Game");
        RecordingPreferencesService preferences = new(CreatePreferences());
        IGameExecutableDiscoveryService discovery = CreateDiscovery(customAvailable: true);
        LauncherExecutableManagementViewModel viewModel = CreateViewModel(
            gameDirectory,
            preferences,
            discovery);
        viewModel.Initialize(LauncherExecutableManagementKind.GameClient);
        viewModel.BeginAdd();

        viewModel.DisplayName = "superhackers client";
        viewModel.SetSelectedExecutablePath(Path.Combine(gameDirectory, "custom.exe"));
        viewModel.TrySaveEditor().Should().BeFalse();

        viewModel.ValidationMessage.Should().Be("Duplicate name");
        preferences.UpdateCount.Should().Be(0);

        viewModel.DisplayName = "Different";
        viewModel.SetSelectedExecutablePath(Path.Combine(gameDirectory, "GENERALSZH.EXE"));
        viewModel.TrySaveEditor().Should().BeFalse();

        viewModel.ValidationMessage.Should().Be("Duplicate file");
        preferences.UpdateCount.Should().Be(0);
    }

    [Fact]
    public void RemoveUnregistersSelectedCustomEntryWithoutDeletingFile()
    {
        using var directory = new TestDirectory();
        string gameDirectory = directory.CreateDirectory("Game");
        string executablePath = Path.Combine(gameDirectory, "custom.exe");
        File.WriteAllText(executablePath, string.Empty);
        RecordingPreferencesService preferences = new(CreatePreferences(
            selectedGameClient: "custom.exe",
            customGameClients: new[] { new LauncherCustomExecutable("Custom", "custom.exe") }));
        LauncherExecutableManagementViewModel viewModel = CreateViewModel(
            gameDirectory,
            preferences,
            CreateDiscovery(customAvailable: true));
        viewModel.Initialize(LauncherExecutableManagementKind.GameClient);

        viewModel.Remove(viewModel.Entries[2]);

        preferences.Current.Games.ZeroHour.CustomGameClients.Should().BeEmpty();
        preferences.Current.Games.ZeroHour.SelectedGameClient.Should().Be("generalszh.exe");
        File.Exists(executablePath).Should().BeTrue();
        viewModel.Entries.Should().HaveCount(2);
    }

    [Fact]
    public void AddPersistenceFailureKeepsPresentationAndReportsSaveFailure()
    {
        using var directory = new TestDirectory();
        string gameDirectory = directory.CreateDirectory("Game");
        RecordingPreferencesService preferences = new(CreatePreferences())
        {
            UpdateFailure = new LauncherPreferencesPersistenceException(new IOException("locked")),
        };
        LauncherExecutableManagementViewModel viewModel = CreateViewModel(
            gameDirectory,
            preferences,
            CreateDiscovery(customAvailable: true));
        viewModel.Initialize(LauncherExecutableManagementKind.GameClient);
        viewModel.BeginAdd();
        viewModel.DisplayName = "Custom";
        viewModel.SetSelectedExecutablePath(Path.Combine(gameDirectory, "custom.exe"));

        viewModel.TrySaveEditor().Should().BeFalse();

        viewModel.LastPersistenceSaveFailed.Should().BeTrue();
        preferences.Current.Games.ZeroHour.CustomGameClients.Should().BeEmpty();
        viewModel.Entries.Should().HaveCount(2);
        viewModel.DisplayName.Should().Be("Custom");
        viewModel.ExecutableName.Should().Be("custom.exe");
    }

    [Fact]
    public void EditReplacesCustomEntryInPlaceAndUpdatesSavedSelection()
    {
        using var directory = new TestDirectory();
        string gameDirectory = directory.CreateDirectory("Game");
        RecordingPreferencesService preferences = new(CreatePreferences(
            selectedGameClient: "custom.exe",
            customGameClients: new[]
            {
                new LauncherCustomExecutable("First", "first.exe"),
                new LauncherCustomExecutable("Custom", "custom.exe"),
            }));
        LauncherExecutableManagementViewModel viewModel = CreateViewModel(
            gameDirectory,
            preferences,
            CreateDiscovery(customAvailable: true));
        viewModel.Initialize(LauncherExecutableManagementKind.GameClient);
        ExecutableOption custom = viewModel.Entries.Single(entry => entry.ExecutableName == "custom.exe");
        viewModel.BeginEdit(custom);
        viewModel.DisplayName = "Edited";
        viewModel.SetSelectedExecutablePath(Path.Combine(gameDirectory, "replacement.exe"));

        viewModel.TrySaveEditor().Should().BeTrue();

        preferences.Current.Games.ZeroHour.CustomGameClients.Should().Equal(
            new LauncherCustomExecutable("First", "first.exe"),
            new LauncherCustomExecutable("Edited", "replacement.exe"));
        preferences.Current.Games.ZeroHour.SelectedGameClient.Should().Be("replacement.exe");
    }

    [Fact]
    public void SearchFiltersByDisplayNameOrFilenameWithoutChangingOrder()
    {
        using var directory = new TestDirectory();
        RecordingPreferencesService preferences = new(CreatePreferences(
            customGameClients: new[]
            {
                new LauncherCustomExecutable("Alpha Client", "alpha.exe"),
                new LauncherCustomExecutable("Beta Client", "special-beta.exe"),
            }));
        LauncherExecutableManagementViewModel viewModel = CreateViewModel(
            directory.Path,
            preferences,
            CreateDiscovery(customAvailable: true));
        viewModel.Initialize(LauncherExecutableManagementKind.GameClient);

        viewModel.SearchText = "special";
        viewModel.Entries.Should().ContainSingle()
            .Which.DisplayName.Should().Be("Beta Client");

        viewModel.SearchText = "client";
        viewModel.Entries.Select(entry => entry.DisplayName)
            .Should().Equal("SuperHackers client", "GeneralsOnline client", "Alpha Client", "Beta Client");
    }

    private static LauncherExecutableManagementViewModel CreateViewModel(
        string gameDirectory,
        ILauncherPreferencesService preferences,
        IGameExecutableDiscoveryService discovery)
    {
        LauncherPaths paths = TestLauncherPaths.Create(gameDirectory, SupportedGame.ZeroHour);
        var runtimeContext = new LauncherRuntimeContext(
            TestLauncherPaths.CreateRuntimePathContext(paths),
            "1.0");
        var localizer = new TestStringLocalizer(new Dictionary<string, string>
        {
            ["ExecutableDetailsRequired"] = "Details required",
            ["ExecutableFileAlreadyExists"] = "Duplicate file",
            ["ExecutableMustBeInGameRoot"] = "Must be in game root",
            ["ExecutableNameAlreadyExists"] = "Duplicate name",
            ["ExecutableUnavailable"] = "Unavailable",
            ["AddExecutable"] = "Add executable",
            ["BuiltInExecutables"] = "Built-in executables",
            ["CustomExecutables"] = "Custom executables",
            ["EditExecutable"] = "Edit executable",
            ["GeneralsOnlineClient"] = "GeneralsOnline client",
            ["ManageGameClients"] = "Manage game clients",
            ["ManageWorldBuilders"] = "Manage World Builders",
            ["SuperHackersClient"] = "SuperHackers client",
            ["SuperHackersWorldBuilder"] = "SuperHackers World Builder",
            ["VanillaWorldBuilder"] = "Vanilla World Builder",
        });
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

    private static IGameExecutableDiscoveryService CreateDiscovery(bool customAvailable)
    {
        IGameExecutableDiscoveryService discovery = Substitute.For<IGameExecutableDiscoveryService>();
        discovery.GetGameClients().Returns(new[]
        {
            new GameClientExecutable("generalszh.exe", GameClientExecutableKind.Community, true),
            new GameClientExecutable(
                "generalsonlinezh.exe",
                GameClientExecutableKind.GeneralsOnline,
                true),
        });
        discovery.GetWorldBuilders().Returns(new[]
        {
            new WorldBuilderExecutable("WorldBuilder.exe", WorldBuilderExecutableKind.Vanilla, true),
            new WorldBuilderExecutable(
                "worldbuilderzh.exe",
                WorldBuilderExecutableKind.Community,
                true),
        });
        discovery.IsExecutableAvailable(Arg.Any<string?>()).Returns(customAvailable);
        return discovery;
    }

    private static LauncherPreferences CreatePreferences(
        string selectedGameClient = "",
        IReadOnlyList<LauncherCustomExecutable>? customGameClients = null)
    {
        return new LauncherPreferences
        {
            Games = new LauncherGamePreferencesSet
            {
                ZeroHour = new LauncherGamePreferences
                {
                    SelectedGameClient = selectedGameClient,
                    CustomGameClients = customGameClients ?? Array.Empty<LauncherCustomExecutable>(),
                },
            },
        };
    }

    private sealed class RecordingPreferencesService : ILauncherPreferencesService
    {
        public RecordingPreferencesService(LauncherPreferences current)
        {
            Current = current;
        }

        public event EventHandler<LauncherPreferences>? PreferencesChanged;

        public LauncherPreferences Current { get; private set; }

        public int UpdateCount { get; private set; }

        public LauncherPreferencesPersistenceException? UpdateFailure { get; init; }

        public void Update(LauncherPreferences preferences)
        {
            if (UpdateFailure != null)
            {
                throw UpdateFailure;
            }

            Current = preferences;
            UpdateCount++;
            PreferencesChanged?.Invoke(this, preferences);
        }
    }
}
