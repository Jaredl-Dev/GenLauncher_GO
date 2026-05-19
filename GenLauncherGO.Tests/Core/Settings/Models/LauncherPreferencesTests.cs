using System;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Tests.Core.Settings.Models;

public sealed class LauncherPreferencesTests
{
    [Theory]
    [InlineData(SupportedGame.Generals, SupportedGame.ZeroHour, @"C:\Games\Generals")]
    [InlineData(SupportedGame.ZeroHour, SupportedGame.Generals, @"D:\Games\ZeroHour")]
    public void WithPath_RoundTripsThroughGetPath(SupportedGame game, SupportedGame otherGame, string path)
    {
        LauncherInstallations installations = new LauncherInstallations().WithPath(game, path);

        installations.GetPath(game).Should().Be(path);
        installations.GetPath(otherGame).Should().BeNull();
    }

    [Fact]
    public void InstallationsResolveConfiguredPreferredGame_WithSingleInstallationFallback()
    {
        var both = new LauncherInstallations
        {
            Generals = @"C:\Games\Generals",
            ZeroHour = @"C:\Games\ZeroHour"
        };
        var generalsOnly = new LauncherInstallations { Generals = @"C:\Games\Generals" };
        var zeroHourOnly = new LauncherInstallations { ZeroHour = @"C:\Games\ZeroHour" };

        both.ResolvePreferredGame(SupportedGame.Generals).Should().Be(SupportedGame.Generals);
        both.ResolvePreferredGame(SupportedGame.ZeroHour).Should().Be(SupportedGame.ZeroHour);
        both.ResolvePreferredGame(null).Should().BeNull();
        generalsOnly.ResolvePreferredGame(SupportedGame.ZeroHour).Should().Be(SupportedGame.Generals);
        zeroHourOnly.ResolvePreferredGame(SupportedGame.Generals).Should().Be(SupportedGame.ZeroHour);
        new LauncherInstallations().ResolvePreferredGame(SupportedGame.Generals).Should().BeNull();
    }

    [Theory]
    [InlineData(SupportedGame.Generals, SupportedGame.ZeroHour)]
    [InlineData(SupportedGame.ZeroHour, SupportedGame.Generals)]
    public void With_RoundTripsThroughGet(SupportedGame game, SupportedGame otherGame)
    {
        var preferences = new LauncherGamePreferences { GameArguments = "-quickstart" };
        LauncherGamePreferencesSet original = new();

        LauncherGamePreferencesSet games = original.With(game, preferences);

        games.Get(game).Should().BeSameAs(preferences);
        games.Get(otherGame).Should().BeSameAs(original.Get(otherGame));
    }

    [Fact]
    public void CustomExecutable_TrimsDisplayNameAndNormalizesExecutableName()
    {
        LauncherCustomExecutable executable = new("  My Client  ", "  custom.exe  ");

        executable.DisplayName.Should().Be("My Client");
        executable.ExecutableName.Should().Be("custom.exe");
    }

    [Theory]
    [InlineData(@"tools\custom.exe")]
    [InlineData(@"..\custom.exe")]
    [InlineData("custom.txt")]
    [InlineData("CON.exe")]
    public void CustomExecutable_WithUnsafeExecutableName_Throws(string executableName)
    {
        Action act = () => new LauncherCustomExecutable("My Client", executableName);

        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(executableName));
    }
}
