using System.Collections.Generic;
using System.Linq;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Services;

public sealed class LauncherExecutableSelectionServiceTests
{
    [Fact]
    public void GetGameClientOptionsKeepsBuiltInsFirstAndAppendsRegisteredCustomEntries()
    {
        IGameExecutableDiscoveryService discovery = Substitute.For<IGameExecutableDiscoveryService>();
        discovery.GetGameClients().Returns(new[]
        {
            new GameClientExecutable("generalszh.exe", GameClientExecutableKind.Community, true),
            new GameClientExecutable("generalsonlinezh.exe", GameClientExecutableKind.GeneralsOnline, false),
        });
        discovery.IsExecutableAvailable("custom.exe").Returns(true);
        LauncherExecutableSelectionService service = CreateService(
            discovery,
            new LauncherGamePreferences
            {
                CustomGameClients = new[] { new LauncherCustomExecutable("Custom", "custom.exe") },
            });

        IReadOnlyList<ExecutableOption> options = service.GetGameClientOptions();

        options.Select(option => option.DisplayName).Should()
            .Equal("SuperHackers client", "GeneralsOnline client", "Custom");
        options.Select(option => option.ExecutableName).Should()
            .Equal("generalszh.exe", "generalsonlinezh.exe", "custom.exe");
        options[0].IsAvailable.Should().BeTrue();
        options[1].IsAvailable.Should().BeFalse();
        options[1].IsGeneralsOnline.Should().BeTrue();
        options[2].IsBuiltIn.Should().BeFalse();
        options[2].CanRemove.Should().BeTrue();
        options.Select(option => option.ShowGroupHeader).Should().Equal(true, false, true);
        options[0].GroupDisplayName.Should().Be("Built-in executables");
        options[2].GroupDisplayName.Should().Be("Custom executables");
    }

    [Fact]
    public void SelectGameClientOptionPreservesSavedMissingExecutable()
    {
        ExecutableOption first = new("First", "first.exe", true, true);
        ExecutableOption saved = new("Saved", "saved.exe", false, false);
        LauncherExecutableSelectionService service = CreateService();

        ExecutableOption? selected = service.SelectGameClientOption(
            new[] { first, saved },
            "SAVED.EXE");

        selected.Should().BeSameAs(saved);
    }

    [Fact]
    public void SelectGameClientOptionFallsBackToFirstAvailableThenFirstMissing()
    {
        ExecutableOption missing = new("Missing", "missing.exe", false, true);
        ExecutableOption available = new("Available", "available.exe", true, false);
        LauncherExecutableSelectionService service = CreateService();

        service.SelectGameClientOption(new[] { missing, available }, "unknown.exe")
            .Should().BeSameAs(available);
        service.SelectGameClientOption(new[] { missing }, "unknown.exe")
            .Should().BeSameAs(missing);
    }

    [Fact]
    public void GetWorldBuilderOptionsKeepsMissingBuiltInsAndCustomEntriesVisible()
    {
        IGameExecutableDiscoveryService discovery = Substitute.For<IGameExecutableDiscoveryService>();
        discovery.GetWorldBuilders().Returns(new[]
        {
            new WorldBuilderExecutable("WorldBuilder.exe", WorldBuilderExecutableKind.Vanilla, false),
            new WorldBuilderExecutable("worldbuilderzh.exe", WorldBuilderExecutableKind.Community, true),
        });
        discovery.IsExecutableAvailable("custom-wb.exe").Returns(false);
        LauncherExecutableSelectionService service = CreateService(
            discovery,
            new LauncherGamePreferences
            {
                CustomWorldBuilders = new[] { new LauncherCustomExecutable("Custom WB", "custom-wb.exe") },
            });

        IReadOnlyList<ExecutableOption> options = service.GetWorldBuilderOptions();

        options.Select(option => option.DisplayName).Should()
            .Equal("Vanilla World Builder", "SuperHackers World Builder", "Custom WB");
        options.Select(option => option.IsAvailable).Should().Equal(false, true, false);
    }

    [Fact]
    public void SelectWorldBuilderOptionPrefersSavedExecutable()
    {
        ExecutableOption first = new("First", "first.exe", true, true);
        ExecutableOption second = new("Second", "second.exe", false, false);
        LauncherExecutableSelectionService service = CreateService();

        ExecutableOption? selected = service.SelectWorldBuilderOption(
            new[] { first, second },
            "second.exe");

        selected.Should().BeSameAs(second);
    }

    private static LauncherExecutableSelectionService CreateService(
        IGameExecutableDiscoveryService? discovery = null,
        LauncherGamePreferences? gamePreferences = null)
    {
        ILauncherPreferencesService preferences = Substitute.For<ILauncherPreferencesService>();
        preferences.Current.Returns(new LauncherPreferences
        {
            Games = new LauncherGamePreferencesSet
            {
                ZeroHour = gamePreferences ?? new LauncherGamePreferences(),
            },
        });

        return new LauncherExecutableSelectionService(
            discovery ?? Substitute.For<IGameExecutableDiscoveryService>(),
            TestLauncherRuntimeContext.Create(),
            preferences,
            new TestStringLocalizer(new Dictionary<string, string>
            {
                ["BuiltInExecutables"] = "Built-in executables",
                ["CustomExecutables"] = "Custom executables",
                ["GeneralsOnlineClient"] = "GeneralsOnline client",
                ["SuperHackersClient"] = "SuperHackers client",
                ["VanillaWorldBuilder"] = "Vanilla World Builder",
                ["SuperHackersWorldBuilder"] = "SuperHackers World Builder",
            }));
    }
}
