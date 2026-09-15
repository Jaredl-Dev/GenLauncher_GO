using System;
using GenLauncherGO.Features.Settings;
using GenLauncherGO.Features.Startup;

namespace GenLauncherGO.Tests.Features.Startup;

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

        result.HasOverlappingPaths.Should().BeTrue();
        result.IsValid.Should().BeFalse();
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
        result.HasOverlappingPaths.Should().BeFalse();
        result.CanonicalInstallations.Should().Be(
            new LauncherInstallations { Generals = CanonicalPath });
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

    [Theory]
    [InlineData(@"C:\Games\Shared", @"c:\games\shared")]
    [InlineData(@"C:\Games\Shared", @"C:\Games\Shared\Child")]
    [InlineData(@"C:\Games\Shared\Child", @"C:\Games\Shared")]
    public void ValidateInstallations_WithOverlappingCanonicalPaths_RejectsSet(string generals, string zeroHour)
    {
        IGameInstallationService service = CreateService((game, _) =>
            GameInstallationValidationResult.Valid(
                game == SupportedGame.Generals ? generals : zeroHour));

        LauncherInstallationsValidationResult result = service.ValidateInstallations(
            new LauncherInstallations
            {
                Generals = @"C:\Games\GeneralsAlias",
                ZeroHour = @"C:\Games\ZeroHourAlias"
            },
            @"C:\Launcher");

        result.IsValid.Should().BeFalse();
        result.HasOverlappingPaths.Should().BeTrue();
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
