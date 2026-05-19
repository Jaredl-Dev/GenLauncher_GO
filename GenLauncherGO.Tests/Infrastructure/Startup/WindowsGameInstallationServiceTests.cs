using System;
using System.Collections.Generic;
using System.IO;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Core.Startup.Models;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Infrastructure.Startup;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Startup;

public sealed class WindowsGameInstallationServiceTests
{
    [Theory]
    [InlineData(SupportedGame.Generals, LauncherFileSystemLayout.GeneralsCommunityExecutableFileName)]
    [InlineData(SupportedGame.Generals, LauncherFileSystemLayout.RetailGameExecutableFileName)]
    [InlineData(SupportedGame.ZeroHour, LauncherFileSystemLayout.GeneralsOnlineExecutableFileName)]
    [InlineData(SupportedGame.ZeroHour, LauncherFileSystemLayout.ZeroHourCommunityExecutableFileName)]
    [InlineData(SupportedGame.ZeroHour, LauncherFileSystemLayout.RetailGameExecutableFileName)]
    public void Validate_AcceptsRootWithMatchingBuiltInExecutable(
        SupportedGame game,
        string executableName)
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string gameDirectory = CreateDirectoryWithExecutable(directory, "Game", executableName);
        WindowsGameInstallationService service = CreateService(new FakeRegistry());

        GameInstallationValidationResult result =
            service.Validate(game, gameDirectory, executableDirectory);

        result.IsValid.Should().BeTrue();
        result.Failure.Should().Be(GameInstallationValidationFailure.None);
        result.CanonicalPath.Should().Be(PhysicalDirectoryPath.ResolveExisting(gameDirectory));
    }

    [Theory]
    [InlineData(SupportedGame.Generals)]
    [InlineData(SupportedGame.ZeroHour)]
    public void Validate_RejectsRootWithoutBuiltInExecutable(SupportedGame game)
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string gameDirectory = directory.CreateDirectory("Game");
        WindowsGameInstallationService service = CreateService(new FakeRegistry());

        GameInstallationValidationResult result =
            service.Validate(game, gameDirectory, executableDirectory);

        result.Failure.Should().Be(GameInstallationValidationFailure.BuiltInExecutableNotFound);
    }

    [Theory]
    [InlineData(SupportedGame.Generals, LauncherFileSystemLayout.ZeroHourCommunityExecutableFileName)]
    [InlineData(SupportedGame.ZeroHour, LauncherFileSystemLayout.GeneralsCommunityExecutableFileName)]
    public void Validate_RejectsBuiltInExecutableForDifferentGame(
        SupportedGame game,
        string executableName)
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string gameDirectory = CreateDirectoryWithExecutable(directory, "Game", executableName);
        WindowsGameInstallationService service = CreateService(new FakeRegistry());

        GameInstallationValidationResult result =
            service.Validate(game, gameDirectory, executableDirectory);

        result.Failure.Should().Be(GameInstallationValidationFailure.BuiltInExecutableNotFound);
    }

    [SymbolicLinkFact]
    public void Validate_RejectsBuiltInExecutableReachedThroughSymbolicLink()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string gameDirectory = directory.CreateDirectory("Game");
        string targetPath = directory.CreateFile("target.exe", string.Empty);
        SymbolicLinkTestSupport.CreateFileLink(
            Path.Combine(gameDirectory, LauncherFileSystemLayout.GeneralsCommunityExecutableFileName),
            targetPath);
        WindowsGameInstallationService service = CreateService(new FakeRegistry());

        GameInstallationValidationResult result =
            service.Validate(SupportedGame.Generals, gameDirectory, executableDirectory);

        result.Failure.Should().Be(GameInstallationValidationFailure.BuiltInExecutableNotFound);
    }

    [Fact]
    public void ValidateInstallations_TreatsQuotedAndUnquotedDirectoryAsDuplicate()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string gameDirectory = CreateDirectoryWithExecutable(
            directory,
            "Game",
            LauncherFileSystemLayout.RetailGameExecutableFileName);
        WindowsGameInstallationService service = CreateService(new FakeRegistry());

        LauncherInstallationsValidationResult result = service.ValidateInstallations(
            new LauncherInstallations
            {
                Generals = gameDirectory,
                ZeroHour = $"\"{gameDirectory}\""
            },
            executableDirectory);

        result.GeneralsValidation.IsValid.Should().BeTrue();
        result.ZeroHourValidation.IsValid.Should().BeTrue();
        result.HasDuplicatePath.Should().BeTrue();
    }

    [Fact]
    public void Validate_RejectsExecutableInsideGameInstallation()
    {
        using var directory = new TestDirectory();
        string gameDirectory = directory.CreateDirectory("Game");
        string executableDirectory = Directory.CreateDirectory(
            Path.Combine(gameDirectory, "Launcher")).FullName;
        WindowsGameInstallationService service = CreateService(new FakeRegistry());

        GameInstallationValidationResult result =
            service.Validate(SupportedGame.ZeroHour, gameDirectory, executableDirectory);

        result.Failure.Should().Be(GameInstallationValidationFailure.LauncherLocationOverlapsGame);
    }

    [Fact]
    public void Validate_AcceptsGameInstallationBelowExecutableDirectoryWhenOutsideLauncherData()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string gameDirectory = CreateDirectoryWithExecutable(
            directory,
            Path.Combine("Launcher", "Game"),
            LauncherFileSystemLayout.GeneralsCommunityExecutableFileName);
        WindowsGameInstallationService service = CreateService(new FakeRegistry());

        GameInstallationValidationResult result =
            service.Validate(SupportedGame.Generals, gameDirectory, executableDirectory);

        result.IsValid.Should().BeTrue();
        result.CanonicalPath.Should().Be(PhysicalDirectoryPath.ResolveExisting(gameDirectory));
    }

    [Fact]
    public void Validate_RejectsGameInstallationInsideLauncherOwnedData()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string gameDirectory = directory.CreateDirectory(
            Path.Combine("Launcher", LauncherFileSystemLayout.LauncherDataFolderName, "Game"));
        WindowsGameInstallationService service = CreateService(new FakeRegistry());

        GameInstallationValidationResult result =
            service.Validate(SupportedGame.Generals, gameDirectory, executableDirectory);

        result.Failure.Should().Be(GameInstallationValidationFailure.UnsafeFileSystemPath);
    }

    [Fact]
    public void Validate_RejectsInstallationReachedThroughReparsePoint()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string linkedDirectory = directory.GetPath("LinkedGame");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(
            directory,
            linkedDirectory,
            "RealGame");
        WindowsGameInstallationService service = CreateService(new FakeRegistry());

        GameInstallationValidationResult result =
            service.Validate(SupportedGame.Generals, linkedDirectory, executableDirectory);

        result.Failure.Should().Be(GameInstallationValidationFailure.UnsafeFileSystemPath);
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    /// <summary>
    ///     The launcher writes its owned data below its own directory, so a launcher location reached through a link is
    ///     exactly as unsafe as a linked game directory.
    /// </summary>
    [Fact]
    public void Validate_RejectsLauncherLocationReachedThroughReparsePoint()
    {
        using var directory = new TestDirectory();
        string gameDirectory = CreateDirectoryWithExecutable(
            directory,
            "Game",
            LauncherFileSystemLayout.GeneralsCommunityExecutableFileName);
        string linkedExecutableDirectory = directory.GetPath("LinkedLauncher");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(
            directory,
            linkedExecutableDirectory,
            "RealLauncher");
        WindowsGameInstallationService service = CreateService(new FakeRegistry());

        GameInstallationValidationResult result =
            service.Validate(SupportedGame.Generals, gameDirectory, linkedExecutableDirectory);

        result.Failure.Should().Be(GameInstallationValidationFailure.UnsafeFileSystemPath);
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    /// <summary>
    ///     Setup shows a message for every validation failure, so a directory value Windows cannot even turn into a
    ///     path has to arrive as an actionable result instead of an exception.
    /// </summary>
    [Fact]
    public void Validate_UnusableDirectoryValue_ReportsPathUnavailable()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        WindowsGameInstallationService service = CreateService(new FakeRegistry());

        GameInstallationValidationResult result =
            service.Validate(SupportedGame.ZeroHour, "C:\\Games\0Zero Hour", executableDirectory);

        result.Failure.Should().Be(GameInstallationValidationFailure.PathUnavailable);
    }

    /// <summary>
    ///     Both supplied paths are inspected for safety, and neither inspection may escape as an exception.
    /// </summary>
    [Fact]
    public void Validate_UnusableExecutableDirectoryValue_ReportsPathUnavailable()
    {
        using var directory = new TestDirectory();
        string gameDirectory = CreateDirectoryWithExecutable(
            directory,
            "Game",
            LauncherFileSystemLayout.ZeroHourCommunityExecutableFileName);
        WindowsGameInstallationService service = CreateService(new FakeRegistry());

        GameInstallationValidationResult result =
            service.Validate(SupportedGame.ZeroHour, gameDirectory, "C:\\Launcher\0Data");

        result.Failure.Should().Be(GameInstallationValidationFailure.PathUnavailable);
    }

    /// <summary>
    ///     The launcher folder can be moved or deleted while the launcher runs. Canonicalizing it then fails, and
    ///     setup has to report that rather than propagate the failure.
    /// </summary>
    [Fact]
    public void Validate_MissingExecutableDirectory_ReportsPathUnavailable()
    {
        using var directory = new TestDirectory();
        string gameDirectory = CreateDirectoryWithExecutable(
            directory,
            "Game",
            LauncherFileSystemLayout.ZeroHourCommunityExecutableFileName);
        string missingExecutableDirectory = directory.GetPath("RemovedLauncher");
        WindowsGameInstallationService service = CreateService(new FakeRegistry());

        GameInstallationValidationResult result =
            service.Validate(SupportedGame.ZeroHour, gameDirectory, missingExecutableDirectory);

        result.Failure.Should().Be(GameInstallationValidationFailure.PathUnavailable);
    }

    /// <summary>
    ///     A session without a detected game carries <see cref="SupportedGame.Unknown" />. Validating against it has no
    ///     meaning, so it is rejected instead of silently producing a failure result callers would show to the user.
    /// </summary>
    [Fact]
    public void Validate_UnsupportedGame_IsRejected()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string gameDirectory = CreateDirectoryWithExecutable(
            directory,
            "Game",
            LauncherFileSystemLayout.RetailGameExecutableFileName);
        WindowsGameInstallationService service = CreateService(new FakeRegistry());

        Action act = () => service.Validate(SupportedGame.Unknown, gameDirectory, executableDirectory);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DiscoverValidInstallations_NeverOverwritesValidConfiguredPath()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string configuredDirectory = CreateDirectoryWithExecutable(
            directory,
            "ConfiguredGenerals",
            LauncherFileSystemLayout.GeneralsCommunityExecutableFileName);
        string registryDirectory = CreateDirectoryWithExecutable(
            directory,
            "RegistryGenerals",
            LauncherFileSystemLayout.GeneralsCommunityExecutableFileName);
        var registry = new FakeRegistry();
        registry.Add(SupportedGame.Generals, registryDirectory);
        WindowsGameInstallationService service = CreateService(registry);
        var current = new LauncherInstallations { Generals = configuredDirectory };

        LauncherInstallations discovered =
            service.DiscoverValidInstallations(current, executableDirectory);

        discovered.Generals.Should().Be(configuredDirectory);
    }

    [Fact]
    public void DiscoverValidInstallations_SkipsMissingRegistryCandidateAndFillsMissingPath()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string missingDirectory = directory.GetPath("Missing");
        string validDirectory = CreateDirectoryWithExecutable(
            directory,
            "ZeroHour",
            LauncherFileSystemLayout.ZeroHourCommunityExecutableFileName);
        var registry = new FakeRegistry();
        registry.Add(SupportedGame.ZeroHour, missingDirectory, validDirectory);
        WindowsGameInstallationService service = CreateService(registry);

        LauncherInstallations discovered =
            service.DiscoverValidInstallations(new LauncherInstallations(), executableDirectory);

        discovered.ZeroHour.Should().Be(PhysicalDirectoryPath.ResolveExisting(validDirectory));
        discovered.Generals.Should().BeNull();
    }

    [Fact]
    public void DiscoverValidInstallations_UsesFirstValidRegistryCandidate()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string steamDirectory = CreateDirectoryWithExecutable(
            directory,
            "SteamZeroHour",
            LauncherFileSystemLayout.RetailGameExecutableFileName);
        string eaDirectory = CreateDirectoryWithExecutable(
            directory,
            "EaZeroHour",
            LauncherFileSystemLayout.RetailGameExecutableFileName);
        var registry = new FakeRegistry();
        registry.Add(SupportedGame.ZeroHour, steamDirectory, eaDirectory);
        WindowsGameInstallationService service = CreateService(registry);

        LauncherInstallations discovered =
            service.DiscoverValidInstallations(new LauncherInstallations(), executableDirectory);

        discovered.ZeroHour.Should().Be(PhysicalDirectoryPath.ResolveExisting(steamDirectory));
    }

    /// <summary>
    ///     A retail directory satisfies both games, and discovery walks Zero Hour first. Filling both entries with one
    ///     directory would leave the launcher deploying Generals content into a Zero Hour installation.
    /// </summary>
    [Fact]
    public void DiscoverValidInstallations_CandidateValidForBothGames_FillsOnlyZeroHour()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string sharedDirectory = CreateDirectoryWithExecutable(
            directory,
            "Retail",
            LauncherFileSystemLayout.RetailGameExecutableFileName);
        var registry = new FakeRegistry();
        registry.Add(SupportedGame.ZeroHour, sharedDirectory);
        registry.Add(SupportedGame.Generals, sharedDirectory);
        WindowsGameInstallationService service = CreateService(registry);

        LauncherInstallations discovered =
            service.DiscoverValidInstallations(new LauncherInstallations(), executableDirectory);

        discovered.ZeroHour.Should().Be(PhysicalDirectoryPath.ResolveExisting(sharedDirectory));
        discovered.Generals.Should().BeNull();
    }

    [Fact]
    public void DiscoverValidInstallations_ConfiguredPathAlsoOfferedToTheOtherGame_KeepsTheOtherGameEmpty()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string sharedDirectory = CreateDirectoryWithExecutable(
            directory,
            "Retail",
            LauncherFileSystemLayout.RetailGameExecutableFileName);
        var registry = new FakeRegistry();
        registry.Add(SupportedGame.Generals, sharedDirectory);
        WindowsGameInstallationService service = CreateService(registry);
        var current = new LauncherInstallations { ZeroHour = sharedDirectory };

        LauncherInstallations discovered =
            service.DiscoverValidInstallations(current, executableDirectory);

        discovered.ZeroHour.Should().Be(sharedDirectory);
        discovered.Generals.Should().BeNull();
    }

    [Fact]
    public void FindContainingInstallation_DetectsGameBeforeStandaloneStorageIsCreated()
    {
        using var directory = new TestDirectory();
        string gameDirectory = CreateDirectoryWithExecutable(
            directory,
            "ZeroHour",
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

    private static string CreateDirectoryWithExecutable(
        TestDirectory directory,
        string relativeDirectory,
        string executableName)
    {
        string gameDirectory = directory.CreateDirectory(relativeDirectory);
        File.WriteAllText(Path.Combine(gameDirectory, executableName), string.Empty);
        return gameDirectory;
    }

    private sealed class FakeRegistry : IGameInstallationRegistry
    {
        private readonly Dictionary<SupportedGame, IReadOnlyList<string>> _candidates = [];

        public IReadOnlyList<string> ReadCandidates(SupportedGame game)
        {
            return _candidates.TryGetValue(game, out IReadOnlyList<string>? candidates)
                ? candidates
                : Array.Empty<string>();
        }

        public void Add(SupportedGame game, params string[] candidates)
        {
            _candidates[game] = candidates;
        }
    }
}
