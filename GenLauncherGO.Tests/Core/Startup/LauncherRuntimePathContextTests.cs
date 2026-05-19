using System;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Tests.Core.Startup;

public sealed class LauncherRuntimePathContextTests
{
    [Fact]
    public void SwitchActiveAtomicallyReplacesImmutablePathSnapshot()
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
    public void ConstructorRejectsPathsOwnedByAnotherLauncherStorageRoot()
    {
        var storagePaths = new LauncherStoragePaths(@"C:\Launcher");
        LauncherPaths foreignPaths = new LauncherStoragePaths(@"D:\OtherLauncher")
            .CreateGamePaths(SupportedGame.ZeroHour, @"C:\Games\Zero Hour");

        Action act = () => new LauncherRuntimePathContext(storagePaths, foreignPaths);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*canonical per-game data directory*");
    }
}
