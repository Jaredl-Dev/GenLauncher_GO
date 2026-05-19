using System.Collections.Generic;
using System.Linq;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Services;

public sealed class LauncherExecutableSelectionServiceTests
{
    [Fact]
    public void GetOptions_GameClient_KeepsBuiltInsFirstAndAppendsRegisteredCustomEntries()
    {
        IGameExecutableDiscoveryService discovery = Substitute.For<IGameExecutableDiscoveryService>();
        discovery.GetGameClients().Returns(new[]
        {
            new BuiltInExecutable("generalsonlinezh.exe", false),
            new BuiltInExecutable("generalszh.exe", true),
            new BuiltInExecutable("generals.exe", true)
        });
        discovery.IsExecutableAvailable("custom.exe").Returns(true);
        LauncherExecutableSelectionService service = CreateService(
            discovery,
            new LauncherGamePreferences
            {
                CustomGameClients = new[] { new LauncherCustomExecutable("Custom", "custom.exe") }
            });

        IReadOnlyList<ExecutableOption> options = service.GetOptions(GameLaunchTargetKind.GameClient);

        options.Select(option => option.DisplayName).Should()
            .Equal(
                "GeneralsOnline",
                "TheSuperHackers",
                "Retail",
                "Custom");
        options.Select(option => option.ExecutableName).Should()
            .Equal("generalsonlinezh.exe", "generalszh.exe", "generals.exe", "custom.exe");
        options[0].IsAvailable.Should().BeFalse();
        options[0].IsGeneralsOnline.Should().BeTrue();
        options[2].IsRetail.Should().BeTrue();
        options[3].IsBuiltIn.Should().BeFalse();
        options[3].CanRemove.Should().BeTrue();
        options.Select(option => option.ShowGroupHeader).Should().Equal(true, false, false, true);
        options[0].GroupDisplayName.Should().Be("Built-in executables");
        options[3].GroupDisplayName.Should().Be("Custom executables");
    }

    [Fact]
    public void SelectPreferredOption_PreservesSavedMissingExecutable()
    {
        ExecutableOption first = new("First", "first.exe", true, true);
        ExecutableOption saved = new("Saved", "saved.exe", false, false);

        ExecutableOption? selected = LauncherExecutableSelectionService.SelectPreferredOption(
            new[] { first, saved },
            "SAVED.EXE");

        selected.Should().BeSameAs(saved);
    }

    [Fact]
    public void SelectPreferredOption_WhenTheSavedNameIsUnknown_FallsBackToFirstAvailable()
    {
        ExecutableOption missing = new("Missing", "missing.exe", false, true);
        ExecutableOption available = new("Available", "available.exe", true, false);

        ExecutableOption? selected = LauncherExecutableSelectionService.SelectPreferredOption(
            new[] { missing, available },
            "unknown.exe");

        selected.Should().BeSameAs(available);
    }

    [Fact]
    public void SelectPreferredOption_WhenNothingIsAvailable_FallsBackToFirstOption()
    {
        ExecutableOption missing = new("Missing", "missing.exe", false, true);
        ExecutableOption alsoMissing = new("Also missing", "also-missing.exe", false, true);

        ExecutableOption? selected = LauncherExecutableSelectionService.SelectPreferredOption(
            new[] { missing, alsoMissing },
            "unknown.exe");

        selected.Should().BeSameAs(missing);
    }

    [Fact]
    public void GetOptions_WorldBuilder_KeepsMissingBuiltInsAndCustomEntriesVisible()
    {
        IGameExecutableDiscoveryService discovery = Substitute.For<IGameExecutableDiscoveryService>();
        discovery.GetWorldBuilders().Returns(new[]
        {
            new BuiltInExecutable("WorldBuilder.exe", false),
            new BuiltInExecutable("worldbuilderzh.exe", true)
        });
        discovery.IsExecutableAvailable("custom-wb.exe").Returns(false);
        LauncherExecutableSelectionService service = CreateService(
            discovery,
            new LauncherGamePreferences
            {
                CustomWorldBuilders = new[] { new LauncherCustomExecutable("Custom WB", "custom-wb.exe") }
            });

        IReadOnlyList<ExecutableOption> options = service.GetOptions(GameLaunchTargetKind.WorldBuilder);

        options.Select(option => option.DisplayName).Should()
            .Equal("Retail", "TheSuperHackers", "Custom WB");
        options.Select(option => option.IsAvailable).Should().Equal(false, true, false);
    }

    /// <summary>
    ///     Custom executables are registered per game, so switching the managed game must change which of them the
    ///     selector offers.
    /// </summary>
    [Theory]
    [InlineData(SupportedGame.Generals, new[] { "TheSuperHackers", "Retail", "Generals Only" })]
    [InlineData(SupportedGame.ZeroHour, new[] { "TheSuperHackers", "Retail" })]
    public void GetOptions_GameClient_ListsCustomClientsOfTheManagedGameOnly(
        SupportedGame managedGame,
        string[] expectedDisplayNames)
    {
        IGameExecutableDiscoveryService discovery = Substitute.For<IGameExecutableDiscoveryService>();
        discovery.GetGameClients().Returns(new[]
        {
            new BuiltInExecutable("generalsv.exe", true),
            new BuiltInExecutable("generals.exe", true)
        });
        LauncherExecutableSelectionService service = CreateService(
            discovery,
            managedGame: managedGame,
            games: new LauncherGamePreferencesSet
            {
                Generals = new LauncherGamePreferences
                {
                    CustomGameClients = new[] { new LauncherCustomExecutable("Generals Only", "custom.exe") }
                }
            });

        IReadOnlyList<ExecutableOption> options = service.GetOptions(GameLaunchTargetKind.GameClient);

        options.Select(option => option.DisplayName).Should().Equal(expectedDisplayNames);
    }

    [Fact]
    public void SelectPreferredOption_PrefersSavedWorldBuilderExecutable()
    {
        ExecutableOption first = new("First", "first.exe", true, true);
        ExecutableOption second = new("Second", "second.exe", false, false);

        ExecutableOption? selected = LauncherExecutableSelectionService.SelectPreferredOption(
            new[] { first, second },
            "second.exe");

        selected.Should().BeSameAs(second);
    }

    private static LauncherExecutableSelectionService CreateService(
        IGameExecutableDiscoveryService? discovery = null,
        LauncherGamePreferences? gamePreferences = null,
        SupportedGame managedGame = SupportedGame.ZeroHour,
        LauncherGamePreferencesSet? games = null)
    {
        ILauncherPreferencesService preferences = Substitute.For<ILauncherPreferencesService>();
        preferences.Current.Returns(new LauncherPreferences
        {
            Games = games ?? new LauncherGamePreferencesSet
            {
                Generals = managedGame == SupportedGame.Generals
                    ? gamePreferences ?? new LauncherGamePreferences()
                    : new LauncherGamePreferences(),
                ZeroHour = managedGame == SupportedGame.ZeroHour
                    ? gamePreferences ?? new LauncherGamePreferences()
                    : new LauncherGamePreferences()
            }
        });

        return new LauncherExecutableSelectionService(
            discovery ?? Substitute.For<IGameExecutableDiscoveryService>(),
            TestLauncherRuntimeContext.Create(managedGame),
            preferences,
            FakeStringLocalizer.Create(TestLocalizedStrings.Settings));
    }
}
