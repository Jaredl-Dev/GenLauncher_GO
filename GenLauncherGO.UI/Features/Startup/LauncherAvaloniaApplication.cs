using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace GenLauncherGO.UI.Features.Startup;

/// <summary>
/// Owns the Avalonia desktop lifetime and the launcher application host.
/// </summary>
internal sealed class LauncherAvaloniaApplication : Application
{
    private LauncherApplicationHost? _host;

    /// <summary>
    /// Loads the Avalonia application resources.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Starts the launcher after Avalonia has created its desktop lifetime.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _host = new LauncherApplicationHost();
            desktop.Exit += (_, _) =>
            {
                _host?.Shutdown();
                _host?.Dispose();
                _host = null;
            };

            Dispatcher.UIThread.Post(async () =>
            {
                bool started = await _host.StartAsync(desktop);
                if (!started)
                {
                    desktop.Shutdown();
                }
            });
        }

        base.OnFrameworkInitializationCompleted();
    }
}
