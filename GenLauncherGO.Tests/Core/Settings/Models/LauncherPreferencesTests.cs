using System;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Tests.Core.Settings.Models;

public sealed class LauncherPreferencesTests
{
    [Theory]
    [InlineData(SupportedGame.Generals, @"C:\Games\Generals")]
    [InlineData(SupportedGame.ZeroHour, @"D:\Games\ZeroHour")]
    public void InstallationsGetAndWithPathUseSupportedGameIdentity(SupportedGame game, string path)
    {
        LauncherInstallations installations = new LauncherInstallations().WithPath(game, path);

        installations.GetPath(game).Should().Be(path);
    }

    [Fact]
    public void InstallationsResolveConfiguredPreferredGameWithSingleInstallationFallback()
    {
        var both = new LauncherInstallations
        {
            Generals = @"C:\Games\Generals",
            ZeroHour = @"C:\Games\ZeroHour",
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
    [InlineData(SupportedGame.Generals)]
    [InlineData(SupportedGame.ZeroHour)]
    public void GamePreferencesGetAndWithUseSupportedGameIdentity(SupportedGame game)
    {
        var preferences = new LauncherGamePreferences { GameArguments = "-quickstart" };

        LauncherGamePreferencesSet games = new LauncherGamePreferencesSet().With(game, preferences);

        games.Get(game).Should().Be(preferences);
    }

    [Fact]
    public void CustomExecutableNormalizesNamesAndRequiresRootLevelExeFile()
    {
        LauncherCustomExecutable executable = new("  My Client  ", "  custom.exe  ");

        executable.DisplayName.Should().Be("My Client");
        executable.ExecutableName.Should().Be("custom.exe");

        Action nested = () => new LauncherCustomExecutable("Nested", @"tools\custom.exe");
        Action traversal = () => new LauncherCustomExecutable("Traversal", @"..\custom.exe");
        Action nonExecutable = () => new LauncherCustomExecutable("Text", "custom.txt");
        Action blankName = () => new LauncherCustomExecutable(" ", "custom.exe");

        nested.Should().Throw<ArgumentException>();
        traversal.Should().Throw<ArgumentException>();
        nonExecutable.Should().Throw<ArgumentException>();
        blankName.Should().Throw<ArgumentException>();
    }
}
