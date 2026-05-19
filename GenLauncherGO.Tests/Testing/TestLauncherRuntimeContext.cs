using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.Tests.Testing;

internal static class TestLauncherRuntimeContext
{
    public static LauncherRuntimeContext Create(
        SupportedGame currentlyManagedGame = SupportedGame.ZeroHour,
        ColorsInfo? colors = null)
    {
        var storagePaths = new LauncherStoragePaths(@"C:\Launcher");
        LauncherPaths gamePaths = storagePaths.CreateGamePaths(
            currentlyManagedGame,
            @"C:\Games\Game");
        return new LauncherRuntimeContext(
            new LauncherRuntimePathContext(storagePaths, gamePaths),
            "1.0.0-test")
        {
            Colors = colors ?? TestLauncherTheme.Create(),
        };
    }
}
