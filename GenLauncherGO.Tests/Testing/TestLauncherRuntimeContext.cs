using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Builds the runtime context on the single launcher layout <see cref="TestLauncherPaths" /> owns.
/// </summary>
internal static class TestLauncherRuntimeContext
{
    internal const string LauncherVersion = "1.0.0-test";

    public static LauncherRuntimeContext Create(
        SupportedGame currentlyManagedGame = SupportedGame.ZeroHour,
        ColorsInfo? colors = null,
        bool connected = false)
    {
        return Create(
            TestLauncherPaths.Create(game: currentlyManagedGame),
            LauncherVersion,
            colors,
            connected);
    }

    public static LauncherRuntimeContext Create(
        LauncherPaths paths,
        string version = LauncherVersion,
        ColorsInfo? colors = null,
        bool connected = false)
    {
        return new LauncherRuntimeContext(TestLauncherPaths.CreateRuntimePathContext(paths), version)
        {
            Colors = colors ?? TestLauncherTheme.Create(),
            Connected = connected
        };
    }
}
