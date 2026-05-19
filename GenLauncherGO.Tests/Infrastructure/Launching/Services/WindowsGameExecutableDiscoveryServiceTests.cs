using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Launching.Services;
using GenLauncherGO.Infrastructure.Launching.Support;
using GenLauncherGO.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Launching.Services;

public sealed class WindowsGameExecutableDiscoveryServiceTests
{
    [Fact]
    public void GetGameClientsReturnsCommunityThenGeneralsOnlineWithAvailability()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        CreateGameFile(paths, "generalszh.exe");
        CreateGameFile(paths, "generalsonlinezh.exe");
        WindowsGameExecutableDiscoveryService service = CreateService(paths);

        IReadOnlyList<GameClientExecutable> clients = service.GetGameClients();

        clients.Should().HaveCount(2);
        clients[0].ExecutableName.Should().Be("generalszh.exe");
        clients[0].Kind.Should().Be(GameClientExecutableKind.Community);
        clients[0].IsAvailable.Should().BeTrue();
        clients[1].ExecutableName.Should().Be("generalsonlinezh.exe");
        clients[1].Kind.Should().Be(GameClientExecutableKind.GeneralsOnline);
        clients[1].IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void GetGameClientsKeepsMissingBuiltInsVisible()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        WindowsGameExecutableDiscoveryService service = CreateService(paths);

        IReadOnlyList<GameClientExecutable> clients = service.GetGameClients();

        clients.Select(client => client.ExecutableName).Should()
            .Equal("generalszh.exe", "generalsonlinezh.exe");
        clients.Should().OnlyContain(client => !client.IsAvailable);
    }

    [Fact]
    public void GetGameClientsUsesManagedGeneralsCommunityExecutable()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(
            Path.Combine(directory.Path, "Game"),
            SupportedGame.Generals);
        CreateGameFile(paths, "generalsv.exe");
        CreateGameFile(paths, "generalsonlinezh.exe");
        WindowsGameExecutableDiscoveryService service = CreateService(paths);

        IReadOnlyList<GameClientExecutable> clients = service.GetGameClients();

        clients.Should().ContainSingle();
        clients[0].ExecutableName.Should().Be("generalsv.exe");
        clients[0].Kind.Should().Be(GameClientExecutableKind.Community);
    }

    [Fact]
    public void GetWorldBuildersReturnsVanillaThenCommunityWhenPresent()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        CreateGameFile(paths, "WorldBuilder.exe");
        CreateGameFile(paths, "worldbuilderzh.exe");
        WindowsGameExecutableDiscoveryService service = CreateService(paths);

        IReadOnlyList<WorldBuilderExecutable> worldBuilders =
            service.GetWorldBuilders();

        worldBuilders.Should().HaveCount(2);
        worldBuilders[0].ExecutableName.Should().Be("WorldBuilder.exe");
        worldBuilders[0].Kind.Should().Be(WorldBuilderExecutableKind.Vanilla);
        worldBuilders[0].IsAvailable.Should().BeTrue();
        worldBuilders[1].ExecutableName.Should().Be("worldbuilderzh.exe");
        worldBuilders[1].Kind.Should().Be(WorldBuilderExecutableKind.Community);
        worldBuilders[1].IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void IsExecutableAvailableChecksRelativeNamesInGameDirectory()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        CreateGameFile(paths, "generalszh.exe");
        WindowsGameExecutableDiscoveryService service = CreateService(paths);

        bool available = service.IsExecutableAvailable("generalszh.exe");
        bool missing = service.IsExecutableAvailable("missing.exe");

        available.Should().BeTrue();
        missing.Should().BeFalse();
    }

    [Fact]
    public void IsExecutableAvailableRejectsRootedExecutablePaths()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string executablePath = Path.Combine(directory.Path, "external.exe");
        File.WriteAllText(executablePath, string.Empty);
        WindowsGameExecutableDiscoveryService service = CreateService(paths);

        bool available = service.IsExecutableAvailable(executablePath);

        available.Should().BeFalse();
    }

    [Fact]
    public void IsExecutableAvailableReturnsFalseForBlankExecutable()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        WindowsGameExecutableDiscoveryService service = CreateService(paths);

        bool available = service.IsExecutableAvailable(" ");

        available.Should().BeFalse();
    }

    [Fact]
    public void IsExecutableAvailableAcceptsRootLevelHardLink()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        Directory.CreateDirectory(paths.GameDirectory);
        string sourcePath = directory.CreateFile("source.exe", string.Empty);
        string hardLinkPath = Path.Combine(paths.GameDirectory, "custom.exe");
        new WindowsHardLinkCreator().TryCreateHardLink(hardLinkPath, sourcePath).Should().BeTrue();
        WindowsGameExecutableDiscoveryService service = CreateService(paths);

        service.IsExecutableAvailable("custom.exe").Should().BeTrue();
    }

    [SymbolicLinkFact]
    public void IsExecutableAvailableRejectsRootLevelSymbolicLink()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        Directory.CreateDirectory(paths.GameDirectory);
        string targetPath = directory.CreateFile("target.exe", string.Empty);
        SymbolicLinkTestSupport.CreateFileLink(
            Path.Combine(paths.GameDirectory, "custom.exe"),
            targetPath);
        WindowsGameExecutableDiscoveryService service = CreateService(paths);

        service.IsExecutableAvailable("custom.exe").Should().BeFalse();
    }

    [Fact]
    public void DiscoveryUsesNewActiveInstallationWithoutRebuildingService()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        var storagePaths = new LauncherStoragePaths(executableDirectory);
        LauncherPaths generalsPaths = storagePaths.CreateGamePaths(
            SupportedGame.Generals,
            directory.CreateDirectory("GeneralsGame"));
        LauncherPaths zeroHourPaths = storagePaths.CreateGamePaths(
            SupportedGame.ZeroHour,
            directory.CreateDirectory("ZeroHourGame"));
        CreateGameFile(generalsPaths, "generalsv.exe");
        CreateGameFile(zeroHourPaths, "generalszh.exe");
        var runtimePaths = new LauncherRuntimePathContext(storagePaths, generalsPaths);
        var service = new WindowsGameExecutableDiscoveryService(
            runtimePaths,
            NullLogger<WindowsGameExecutableDiscoveryService>.Instance);

        service.GetGameClients()
            .Should().ContainSingle()
            .Which.Should().Match<GameClientExecutable>(client =>
                client.ExecutableName == "generalsv.exe" && client.IsAvailable);

        runtimePaths.SwitchActive(zeroHourPaths);

        service.GetGameClients()[0]
            .Should().Match<GameClientExecutable>(client =>
                client.ExecutableName == "generalszh.exe" && client.IsAvailable);
    }

    private static WindowsGameExecutableDiscoveryService CreateService(LauncherPaths paths)
    {
        return new WindowsGameExecutableDiscoveryService(
            TestLauncherPaths.CreateRuntimePathContext(paths),
            NullLogger<WindowsGameExecutableDiscoveryService>.Instance);
    }

    private static void CreateGameFile(LauncherPaths paths, string fileName)
    {
        Directory.CreateDirectory(paths.GameDirectory);
        File.WriteAllText(Path.Combine(paths.GameDirectory, fileName), string.Empty);
    }

    private static LauncherPaths CreatePaths(string root)
    {
        return TestLauncherPaths.Create(Path.Combine(root, "Game"));
    }

}
