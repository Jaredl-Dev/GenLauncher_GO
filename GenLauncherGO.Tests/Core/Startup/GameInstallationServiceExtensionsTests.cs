using System;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Core.Startup.Models;

namespace GenLauncherGO.Tests.Core.Startup;

public sealed class GameInstallationServiceExtensionsTests
{
    [Fact]
    public void ValidateInstallations_WithOneValidPath_ReturnsCanonicalSet()
    {
        const string canonicalPath = @"C:\Games\Generals";
        IGameInstallationService service = CreateService((game, path) =>
            game == SupportedGame.Generals && !String.IsNullOrWhiteSpace(path)
                ? GameInstallationValidationResult.Valid(canonicalPath)
                : MissingPath());

        LauncherInstallationsValidationResult result = service.ValidateInstallations(
            new LauncherInstallations { Generals = @"C:\Games\GENERALS" },
            @"C:\Launcher");

        result.IsValid.Should().BeTrue();
        result.HasDuplicatePath.Should().BeFalse();
        result.CanonicalInstallations.Should().Be(
            new LauncherInstallations { Generals = canonicalPath });
    }

    [Fact]
    public void ValidateInstallations_WithInvalidNonemptyPath_RejectsSet()
    {
        IGameInstallationService service = CreateService((game, path) =>
        {
            if (String.IsNullOrWhiteSpace(path))
            {
                return MissingPath();
            }

            return game == SupportedGame.Generals
                ? GameInstallationValidationResult.Valid(@"C:\Games\Generals")
                : GameInstallationValidationResult.Invalid(
                    GameInstallationValidationFailure.RequiredFilesMissing);
        });

        LauncherInstallationsValidationResult result = service.ValidateInstallations(
            new LauncherInstallations
            {
                Generals = @"C:\Games\Generals",
                ZeroHour = @"C:\Not-Zero-Hour",
            },
            @"C:\Launcher");

        result.IsValid.Should().BeFalse();
        result.CanonicalInstallations.Should().Be(
            new LauncherInstallations { Generals = @"C:\Games\Generals" });
    }

    [Fact]
    public void ValidateInstallations_WithSameCanonicalPath_RejectsDuplicate()
    {
        const string canonicalPath = @"C:\Games\Shared";
        IGameInstallationService service = CreateService((_, _) =>
            GameInstallationValidationResult.Valid(canonicalPath));

        LauncherInstallationsValidationResult result = service.ValidateInstallations(
            new LauncherInstallations
            {
                Generals = @"C:\Games\GeneralsAlias",
                ZeroHour = @"C:\Games\ZeroHourAlias",
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
