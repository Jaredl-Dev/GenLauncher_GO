using System;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Core.Startup.Models;

namespace GenLauncherGO.Tests.Core.Startup;

public sealed class GameInstallationServiceExtensionsTests
{
    [Fact]
    public void ValidateInstallations_ReportsIdenticalEnteredPathsBeforeFilesystemFailures()
    {
        IGameInstallationService service = Substitute.For<IGameInstallationService>();
        service.Validate(
                Arg.Any<SupportedGame>(),
                Arg.Any<string?>(),
                Arg.Any<string>())
            .Returns(GameInstallationValidationResult.Invalid(
                GameInstallationValidationFailure.DirectoryNotFound));

        LauncherInstallationsValidationResult result = service.ValidateInstallations(
            new LauncherInstallations
            {
                Generals = @"C:\Missing\Game",
                ZeroHour = @" c:\missing\game "
            },
            @"C:\Launcher");

        result.HasDuplicatePath.Should().BeTrue();
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateInstallations_WithNoConfiguredPaths_RejectsSet()
    {
        IGameInstallationService service = CreateService((_, _) => MissingPath());

        LauncherInstallationsValidationResult result = service.ValidateInstallations(
            new LauncherInstallations(),
            @"C:\Launcher");

        result.IsValid.Should().BeFalse();
        result.HasDuplicatePath.Should().BeFalse();
        result.CanonicalInstallations.Should().Be(new LauncherInstallations());
    }

    [Fact]
    public void ValidateInstallations_WithOneValidPath_ReturnsCanonicalSet()
    {
        const string CanonicalPath = @"C:\Games\Generals";
        IGameInstallationService service = CreateService((game, path) =>
            game == SupportedGame.Generals && !string.IsNullOrWhiteSpace(path)
                ? GameInstallationValidationResult.Valid(CanonicalPath)
                : MissingPath());

        LauncherInstallationsValidationResult result = service.ValidateInstallations(
            new LauncherInstallations { Generals = @"C:\Games\GENERALS" },
            @"C:\Launcher");

        result.IsValid.Should().BeTrue();
        result.HasDuplicatePath.Should().BeFalse();
        result.CanonicalInstallations.Should().Be(
            new LauncherInstallations { Generals = CanonicalPath });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateInstallations_WithABlankPath_TreatsItAsNotConfigured(string blankPath)
    {
        const string ZeroHourPath = @"C:\Games\ZeroHour";
        IGameInstallationService service = CreateService((game, path) =>
            game == SupportedGame.ZeroHour && !string.IsNullOrWhiteSpace(path)
                ? GameInstallationValidationResult.Valid(ZeroHourPath)
                : MissingPath());

        LauncherInstallationsValidationResult result = service.ValidateInstallations(
            new LauncherInstallations { Generals = blankPath, ZeroHour = ZeroHourPath },
            @"C:\Launcher");

        result.IsValid.Should().BeTrue();
        result.CanonicalInstallations.Should().Be(
            new LauncherInstallations { ZeroHour = ZeroHourPath });
    }

    [Fact]
    public void ValidateInstallations_WithInvalidNonemptyPath_RejectsSet()
    {
        IGameInstallationService service = CreateService((game, path) =>
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return MissingPath();
            }

            return game == SupportedGame.Generals
                ? GameInstallationValidationResult.Valid(@"C:\Games\Generals")
                : GameInstallationValidationResult.Invalid(
                    GameInstallationValidationFailure.DirectoryNotFound);
        });

        LauncherInstallationsValidationResult result = service.ValidateInstallations(
            new LauncherInstallations
            {
                Generals = @"C:\Games\Generals",
                ZeroHour = @"C:\Not-Zero-Hour"
            },
            @"C:\Launcher");

        result.IsValid.Should().BeFalse();
        result.CanonicalInstallations.Should().Be(
            new LauncherInstallations { Generals = @"C:\Games\Generals" });
    }

    [Fact]
    public void ValidateInstallations_WithSameCanonicalPath_RejectsDuplicate()
    {
        IGameInstallationService service = CreateService((game, _) =>
            GameInstallationValidationResult.Valid(
                game == SupportedGame.Generals ? @"C:\Games\Shared" : @"c:\games\shared"));

        LauncherInstallationsValidationResult result = service.ValidateInstallations(
            new LauncherInstallations
            {
                Generals = @"C:\Games\GeneralsAlias",
                ZeroHour = @"C:\Games\ZeroHourAlias"
            },
            @"C:\Launcher");

        result.IsValid.Should().BeFalse();
        result.HasDuplicatePath.Should().BeTrue();
    }

    private static IGameInstallationService CreateService(
        Func<SupportedGame, string?, GameInstallationValidationResult> validation)
    {
        IGameInstallationService service = Substitute.For<IGameInstallationService>();
        service.Validate(
                Arg.Any<SupportedGame>(),
                Arg.Any<string?>(),
                Arg.Any<string>())
            .Returns(call => validation(
                call.ArgAt<SupportedGame>(0),
                call.ArgAt<string?>(1)));
        return service;
    }

    private static GameInstallationValidationResult MissingPath()
    {
        return GameInstallationValidationResult.Invalid(
            GameInstallationValidationFailure.PathMissing);
    }
}
