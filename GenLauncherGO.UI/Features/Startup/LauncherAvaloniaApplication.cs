using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.UI.Features.Startup;

/// <summary>
///     Owns the Avalonia desktop lifetime and the launcher application host.
/// </summary>
internal sealed class LauncherAvaloniaApplication : Application
{
    private LauncherApplicationHost? _host;

    /// <summary>
    ///     Loads the Avalonia application resources and seeds the theme every control theme depends on.
    /// </summary>
    /// <remarks>
    ///     The control themes declared in App.axaml resolve launcher colours through <c>DynamicResource</c>, so those
    ///     keys have to exist at application scope before the first window is shown. Seeding here covers the startup
    ///     windows that run before a game is known; <see cref="LauncherRuntimeContext.Colors" /> replaces this with the
    ///     active game's palette as soon as one is resolved.
    /// </remarks>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        LauncherThemeResourceApplier.Apply(Resources, LauncherThemePresets.Create(SupportedGame.ZeroHour));
    }

    /// <summary>
    ///     Starts the launcher after Avalonia has created its desktop lifetime.
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
