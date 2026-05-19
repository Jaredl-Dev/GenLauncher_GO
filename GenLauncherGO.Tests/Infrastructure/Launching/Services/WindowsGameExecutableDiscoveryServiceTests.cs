using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Launching.Services;
using GenLauncherGO.Infrastructure.Launching.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Launching.Services;

public sealed class WindowsGameExecutableDiscoveryServiceTests
{
    [Fact]
    public void GetGameClients_ReturnsGeneralsOnlineThenCommunityThenRetailWithAvailability()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(Path.Combine(directory.Path, "Game"));
        CreateGameFile(paths, "generalszh.exe");
        CreateGameFile(paths, "generalsonlinezh.exe");
        CreateGameFile(paths, "generals.exe");
        WindowsGameExecutableDiscoveryService service = CreateService(paths);

        IReadOnlyList<BuiltInExecutable> clients = service.GetGameClients();

        clients.Should().HaveCount(3);
        clients[0].ExecutableName.Should().Be("generalsonlinezh.exe");
        clients[0].Kind.Should().Be(BuiltInExecutableKind.GeneralsOnline);
        clients[0].IsAvailable.Should().BeTrue();
        clients[1].ExecutableName.Should().Be("generalszh.exe");
        clients[1].Kind.Should().Be(BuiltInExecutableKind.Community);
        clients[1].IsAvailable.Should().BeTrue();
        clients[2].ExecutableName.Should().Be("generals.exe");
        clients[2].Kind.Should().Be(BuiltInExecutableKind.Retail);
        clients[2].IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void GetGameClients_KeepsMissingBuiltInsVisible()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(Path.Combine(directory.Path, "Game"));
        WindowsGameExecutableDiscoveryService service = CreateService(paths);

        IReadOnlyList<BuiltInExecutable> clients = service.GetGameClients();

        clients.Select(client => client.ExecutableName).Should()
            .Equal("generalsonlinezh.exe", "generalszh.exe", "generals.exe");
        clients.Should().OnlyContain(client => !client.IsAvailable);
    }

    [Fact]
    public void GetGameClients_UsesManagedGeneralsCommunityThenRetailExecutables()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(
            Path.Combine(directory.Path, "Game"),
            SupportedGame.Generals);
        CreateGameFile(paths, "generalsv.exe");
        CreateGameFile(paths, "generals.exe");
        CreateGameFile(paths, "generalsonlinezh.exe");
        WindowsGameExecutableDiscoveryService service = CreateService(paths);

        IReadOnlyList<BuiltInExecutable> clients = service.GetGameClients();

        clients.Should().HaveCount(2);
        clients[0].ExecutableName.Should().Be("generalsv.exe");
        clients[0].Kind.Should().Be(BuiltInExecutableKind.Community);
        clients[1].ExecutableName.Should().Be("generals.exe");
        clients[1].Kind.Should().Be(BuiltInExecutableKind.Retail);
    }

    [Fact]
    public void GetWorldBuilders_ReturnsRetailThenCommunityWhenPresent()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(Path.Combine(directory.Path, "Game"));
        CreateGameFile(paths, "WorldBuilder.exe");
        CreateGameFile(paths, "worldbuilderzh.exe");
        WindowsGameExecutableDiscoveryService service = CreateService(paths);

        IReadOnlyList<BuiltInExecutable> worldBuilders =
            service.GetWorldBuilders();

        worldBuilders.Should().HaveCount(2);
        worldBuilders[0].ExecutableName.Should().Be("WorldBuilder.exe");
        worldBuilders[0].Kind.Should().Be(BuiltInExecutableKind.Retail);
        worldBuilders[0].IsAvailable.Should().BeTrue();
        worldBuilders[1].ExecutableName.Should().Be("worldbuilderzh.exe");
        worldBuilders[1].Kind.Should().Be(BuiltInExecutableKind.Community);
        worldBuilders[1].IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void IsExecutableAvailable_ChecksRelativeNamesInGameDirectory()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(Path.Combine(directory.Path, "Game"));
        CreateGameFile(paths, "generalszh.exe");
        WindowsGameExecutableDiscoveryService service = CreateService(paths);

        bool available = service.IsExecutableAvailable("generalszh.exe");
        bool missing = service.IsExecutableAvailable("missing.exe");

        available.Should().BeTrue();
        missing.Should().BeFalse();
    }

    [Fact]
    public void IsExecutableAvailable_RejectsRootedExecutablePaths()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(Path.Combine(directory.Path, "Game"));
        string executablePath = Path.Combine(directory.Path, "external.exe");
        File.WriteAllText(executablePath, string.Empty);
        WindowsGameExecutableDiscoveryService service = CreateService(paths);

        bool available = service.IsExecutableAvailable(executablePath);

        available.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsExecutableAvailable_WithoutAnExecutableName_ReturnsFalse(string? executableName)
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(Path.Combine(directory.Path, "Game"));
        WindowsGameExecutableDiscoveryService service = CreateService(paths);

        bool available = service.IsExecutableAvailable(executableName);

        available.Should().BeFalse();
    }

    [Fact]
    public void IsExecutableAvailable_WhenADirectoryCarriesTheExecutableName_ReturnsFalse()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(Path.Combine(directory.Path, "Game"));
        Directory.CreateDirectory(Path.Combine(paths.GameDirectory, "generalszh.exe"));
        WindowsGameExecutableDiscoveryService service = CreateService(paths);

        bool available = service.IsExecutableAvailable("generalszh.exe");

        available.Should().BeFalse();
    }

    [Fact]
    public void IsExecutableAvailable_AcceptsRootLevelHardLink()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(Path.Combine(directory.Path, "Game"));
        Directory.CreateDirectory(paths.GameDirectory);
        string sourcePath = directory.CreateFile("source.exe", string.Empty);
        string hardLinkPath = Path.Combine(paths.GameDirectory, "custom.exe");
        new WindowsHardLinkCreator().TryCreateHardLink(hardLinkPath, sourcePath).Should().BeTrue();
        WindowsGameExecutableDiscoveryService service = CreateService(paths);

        service.IsExecutableAvailable("custom.exe").Should().BeTrue();
    }

    [SymbolicLinkFact]
    public void IsExecutableAvailable_RejectsRootLevelSymbolicLink()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(Path.Combine(directory.Path, "Game"));
        Directory.CreateDirectory(paths.GameDirectory);
        string targetPath = directory.CreateFile("target.exe", string.Empty);
        SymbolicLinkTestSupport.CreateFileLink(
            Path.Combine(paths.GameDirectory, "custom.exe"),
            targetPath);
        WindowsGameExecutableDiscoveryService service = CreateService(paths);

        service.IsExecutableAvailable("custom.exe").Should().BeFalse();
    }

    [Fact]
    public void Discovery_UsesNewActiveInstallationWithoutRebuildingService()
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

        service.GetGameClients()[0]
            .Should().Match<BuiltInExecutable>(client =>
                client.ExecutableName == "generalsv.exe" && client.IsAvailable);

        runtimePaths.SwitchActive(zeroHourPaths);

        service.GetGameClients()[1]
            .Should().Match<BuiltInExecutable>(client =>
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

}
