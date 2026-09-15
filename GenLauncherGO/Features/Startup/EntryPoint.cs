using System;
using Avalonia;
using Velopack;

namespace GenLauncherGO.Features.Startup;

/// <summary>
///     Provides the Windows executable entry point for GenLauncherGO.
/// </summary>
internal static class EntryPoint
{
    /// <summary>
    ///     Starts the launcher application.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    ///     Creates the native Avalonia desktop application.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<LauncherAvaloniaApplication>()
            .UsePlatformDetect();
    }
}
