using System;
using System.Collections.Generic;
using System.IO;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Models;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Infrastructure.Startup;
using GenLauncherGO.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Startup;

public sealed class WindowsGameInstallationServiceTests
{
    [Theory]
    [InlineData(SupportedGame.Generals, "Window.big", "generalsv.exe")]
    [InlineData(SupportedGame.ZeroHour, "WindowZH.big", "generalszh.exe")]
    [InlineData(SupportedGame.ZeroHour, "WindowZH.big", "generalsonlinezh.exe")]
    public void ValidateAcceptsCanonicalFilesAndReturnsPhysicalPath(
        SupportedGame game,
        string archiveName,
        string executableName)
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string gameDirectory = CreateGame(directory, "Game", archiveName, executableName);
        WindowsGameInstallationService service = CreateService(new FakeRegistry());

        GameInstallationValidationResult result =
            service.Validate(game, gameDirectory, executableDirectory);

        result.IsValid.Should().BeTrue();
        result.Failure.Should().Be(GameInstallationValidationFailure.None);
        result.CanonicalPath.Should().Be(PhysicalDirectoryPath.ResolveExisting(gameDirectory));
    }

    [Theory]
    [InlineData(SupportedGame.Generals, "Window.big.GLR", "generalsv.exe")]
    [InlineData(SupportedGame.ZeroHour, "WindowZH.big.GLR", "generalszh.exe")]
    public void ValidateRejectsLegacyRenamedArchive(
        SupportedGame game,
        string archiveName,
        string executableName)
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string gameDirectory = CreateGame(directory, "Game", archiveName, executableName);
        WindowsGameInstallationService service = CreateService(new FakeRegistry());

        GameInstallationValidationResult result =
            service.Validate(game, gameDirectory, executableDirectory);

        result.Failure.Should().Be(GameInstallationValidationFailure.RequiredFilesMissing);
    }

    [Fact]
    public void ValidateRejectsFolderForDifferentSupportedGame()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string generalsDirectory = CreateGame(directory, "Generals", "Window.big", "generalsv.exe");
        WindowsGameInstallationService service = CreateService(new FakeRegistry());

        GameInstallationValidationResult result =
            service.Validate(SupportedGame.ZeroHour, generalsDirectory, executableDirectory);

        result.Failure.Should().Be(GameInstallationValidationFailure.RequiredFilesMissing);
        result.CanonicalPath.Should().BeNull();
    }

    [Fact]
    public void ValidateRejectsExecutableInsideGameInstallation()
    {
        using var directory = new TestDirectory();
        string gameDirectory = CreateGame(directory, "Game", "WindowZH.big", "generalszh.exe");
        string executableDirectory = Directory.CreateDirectory(
            Path.Combine(gameDirectory, "Launcher")).FullName;
        WindowsGameInstallationService service = CreateService(new FakeRegistry());

        GameInstallationValidationResult result =
            service.Validate(SupportedGame.ZeroHour, gameDirectory, executableDirectory);

        result.Failure.Should().Be(GameInstallationValidationFailure.LauncherLocationOverlapsGame);
    }

    [Fact]
    public void ValidateAcceptsGameInstallationBelowExecutableDirectoryWhenOutsideLauncherData()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string gameDirectory = CreateGame(
            directory,
            Path.Combine("Launcher", "Game"),
            "Window.big",
            "generalsv.exe");
        WindowsGameInstallationService service = CreateService(new FakeRegistry());

        GameInstallationValidationResult result =
            service.Validate(SupportedGame.Generals, gameDirectory, executableDirectory);

        result.IsValid.Should().BeTrue();
        result.CanonicalPath.Should().Be(PhysicalDirectoryPath.ResolveExisting(gameDirectory));
    }

    [Fact]
    public void ValidateRejectsGameInstallationInsideLauncherOwnedData()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string gameDirectory = CreateGame(
            directory,
            Path.Combine("Launcher", LauncherFileSystemLayout.LauncherDataFolderName, "Game"),
            "Window.big",
            "generalsv.exe");
        WindowsGameInstallationService service = CreateService(new FakeRegistry());

        GameInstallationValidationResult result =
            service.Validate(SupportedGame.Generals, gameDirectory, executableDirectory);

        result.Failure.Should().Be(GameInstallationValidationFailure.UnsafeFileSystemPath);
    }

    [SymbolicLinkFact]
    public void ValidateRejectsInstallationReachedThroughSymbolicLink()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string gameDirectory = CreateGame(directory, "RealGame", "Window.big", "generalsv.exe");
        string linkedDirectory = directory.GetPath("LinkedGame");
        Directory.CreateSymbolicLink(linkedDirectory, gameDirectory);
        WindowsGameInstallationService service = CreateService(new FakeRegistry());

        GameInstallationValidationResult result =
            service.Validate(SupportedGame.Generals, linkedDirectory, executableDirectory);

        result.Failure.Should().Be(GameInstallationValidationFailure.UnsafeFileSystemPath);
    }

    [Fact]
    public void DiscoverValidInstallationsNeverOverwritesValidConfiguredPath()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string configuredDirectory = CreateGame(
            directory,
            "ConfiguredGenerals",
            "Window.big",
            "generalsv.exe");
        string registryDirectory = CreateGame(
            directory,
            "RegistryGenerals",
            "Window.big",
            "generalsv.exe");
        var registry = new FakeRegistry();
        registry.Add(SupportedGame.Generals, registryDirectory);
        WindowsGameInstallationService service = CreateService(registry);
        var current = new LauncherInstallations { Generals = configuredDirectory };

        LauncherInstallations discovered =
            service.DiscoverValidInstallations(current, executableDirectory);

        discovered.Generals.Should().Be(configuredDirectory);
    }

    [Fact]
    public void DiscoverValidInstallationsSkipsInvalidRegistryCandidateAndFillsMissingPath()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string invalidDirectory = directory.CreateDirectory("NotAGame");
        string validDirectory = CreateGame(
            directory,
            "ZeroHour",
            "WindowZH.big",
            "generalszh.exe");
        var registry = new FakeRegistry();
        registry.Add(SupportedGame.ZeroHour, invalidDirectory, validDirectory);
        WindowsGameInstallationService service = CreateService(registry);

        LauncherInstallations discovered =
            service.DiscoverValidInstallations(new LauncherInstallations(), executableDirectory);

        discovered.ZeroHour.Should().Be(PhysicalDirectoryPath.ResolveExisting(validDirectory));
        discovered.Generals.Should().BeNull();
    }

    [Fact]
    public void DiscoverValidInstallationsUsesFirstValidRegistryCandidate()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string steamDirectory = CreateGame(
            directory,
            "SteamZeroHour",
            "WindowZH.big",
            "generalszh.exe");
        string eaDirectory = CreateGame(
            directory,
            "EaZeroHour",
            "WindowZH.big",
            "generalszh.exe");
        var registry = new FakeRegistry();
        registry.Add(SupportedGame.ZeroHour, steamDirectory, eaDirectory);
        WindowsGameInstallationService service = CreateService(registry);

        LauncherInstallations discovered =
            service.DiscoverValidInstallations(new LauncherInstallations(), executableDirectory);

        discovered.ZeroHour.Should().Be(PhysicalDirectoryPath.ResolveExisting(steamDirectory));
    }

    [Fact]
    public void ValidateRejectsInstallationContainingBothGameMarkerSets()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string combinedDirectory = CreateGame(
            directory,
            "Combined",
            "Window.big",
            "generalsv.exe");
        File.WriteAllText(Path.Combine(combinedDirectory, "WindowZH.big"), string.Empty);
        File.WriteAllText(Path.Combine(combinedDirectory, "generalszh.exe"), string.Empty);
        WindowsGameInstallationService service = CreateService(new FakeRegistry());

        GameInstallationValidationResult generals =
            service.Validate(SupportedGame.Generals, combinedDirectory, executableDirectory);
        GameInstallationValidationResult zeroHour =
            service.Validate(SupportedGame.ZeroHour, combinedDirectory, executableDirectory);

        generals.Failure.Should().Be(GameInstallationValidationFailure.RequiredFilesMissing);
        zeroHour.Failure.Should().Be(GameInstallationValidationFailure.RequiredFilesMissing);
    }

    [Fact]
    public void FindContainingInstallationDetectsGameBeforeStandaloneStorageIsCreated()
    {
        using var directory = new TestDirectory();
        string gameDirectory = CreateGame(
            directory,
            "ZeroHour",
            "WindowZH.big",
            "generalszh.exe");
        string executableDirectory = directory.CreateDirectory(
            Path.Combine("ZeroHour", "Tools", "GenLauncherGO"));
        WindowsGameInstallationService service = CreateService(new FakeRegistry());

        GameInstallationLocation? result =
            service.FindContainingInstallation(executableDirectory);

        result.Should().NotBeNull();
        result!.Game.Should().Be(SupportedGame.ZeroHour);
        result.Directory.Should().Be(PhysicalDirectoryPath.ResolveExisting(gameDirectory));
    }

    private static WindowsGameInstallationService CreateService(IGameInstallationRegistry registry)
    {
        return new WindowsGameInstallationService(
            registry,
            NullLogger<WindowsGameInstallationService>.Instance);
    }

    private static string CreateGame(
        TestDirectory directory,
        string relativeDirectory,
        string archiveName,
        string executableName)
    {
        string gameDirectory = directory.CreateDirectory(relativeDirectory);
        File.WriteAllText(Path.Combine(gameDirectory, "BINKW32.DLL"), string.Empty);
        File.WriteAllText(Path.Combine(gameDirectory, archiveName), string.Empty);
        File.WriteAllText(Path.Combine(gameDirectory, executableName), string.Empty);
        return gameDirectory;
    }

    private sealed class FakeRegistry : IGameInstallationRegistry
    {
        private readonly Dictionary<SupportedGame, IReadOnlyList<string>> _candidates = new();

        public void Add(SupportedGame game, params string[] candidates)
        {
            _candidates[game] = candidates;
        }

        public IReadOnlyList<string> ReadCandidates(SupportedGame game)
        {
            return _candidates.TryGetValue(game, out IReadOnlyList<string>? candidates)
                ? candidates
                : Array.Empty<string>();
        }
    }
}
