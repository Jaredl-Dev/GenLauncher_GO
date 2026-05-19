using System;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Tests.Core.Startup;

public sealed class LauncherRuntimePathContextTests
{
    [Fact]
    public void SwitchActiveAtomically_ReplacesImmutablePathSnapshot()
    {
        var storagePaths = new LauncherStoragePaths(@"C:\Launcher");
        LauncherPaths generalsPaths = storagePaths.CreateGamePaths(
            SupportedGame.Generals,
            @"C:\Games\Generals");
        LauncherPaths zeroHourPaths = storagePaths.CreateGamePaths(
            SupportedGame.ZeroHour,
            @"D:\Games\Zero Hour");
        var context = new LauncherRuntimePathContext(storagePaths, generalsPaths);

        context.SwitchActive(zeroHourPaths);

        context.ActivePaths.Should().BeSameAs(zeroHourPaths);
        context.StoragePaths.Should().BeSameAs(storagePaths);
    }

    [Fact]
    public void SwitchActive_WithPathsOwnedByAnotherStorageRoot_RejectsAndKeepsActiveSnapshot()
    {
        var storagePaths = new LauncherStoragePaths(@"C:\Launcher");
        LauncherPaths generalsPaths = storagePaths.CreateGamePaths(
            SupportedGame.Generals,
            @"C:\Games\Generals");
        LauncherPaths foreignPaths = new LauncherStoragePaths(@"D:\OtherLauncher")
            .CreateGamePaths(SupportedGame.ZeroHour, @"D:\Games\Zero Hour");
        var context = new LauncherRuntimePathContext(storagePaths, generalsPaths);

        Action act = () => context.SwitchActive(foreignPaths);

        act.Should().Throw<ArgumentException>();
        context.ActivePaths.Should().BeSameAs(generalsPaths);
    }

    [Fact]
    public void Constructor_RejectsPathsOwnedByAnotherLauncherStorageRoot()
    {
        var storagePaths = new LauncherStoragePaths(@"C:\Launcher");
        LauncherPaths foreignPaths = new LauncherStoragePaths(@"D:\OtherLauncher")
            .CreateGamePaths(SupportedGame.ZeroHour, @"C:\Games\Zero Hour");

        Action act = () => new LauncherRuntimePathContext(storagePaths, foreignPaths);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*canonical per-game data directory*");
    }
}
