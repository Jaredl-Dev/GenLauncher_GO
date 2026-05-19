using Avalonia;

namespace GenLauncherGO.UI.Features.Startup;

/// <summary>
/// Provides the Windows executable entry point for GenLauncherGO.
/// </summary>
internal static class EntryPoint
{
    /// <summary>
    /// Starts the launcher application.
    /// </summary>
    [System.STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Creates the native Avalonia desktop application.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<LauncherAvaloniaApplication>()
            .UsePlatformDetect();
    }
}
