using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Shared.Controls;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.UI.Features.Startup.Views;

/// <summary>
///     Blocks startup when the standalone executable is inside a configured game installation.
/// </summary>
internal partial class LauncherLocationWarningWindow : Window
{
    public LauncherLocationWarningWindow()
    {
        InitializeComponent();
        LauncherWindowScaling.Attach(this);
    }

    public LauncherLocationWarningWindow(
        string executableDirectory,
        string containingGameDirectory,
        SupportedGame containingGame)
        : this()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(containingGameDirectory);

        LauncherLocationText.Text = executableDirectory;
        GameLocationText.Text = containingGameDirectory;
        // Shown before a game is active, so it publishes the offending game's palette for itself and its dialogs.
        if (Application.Current is { } application)
        {
            LauncherThemeResourceApplier.Apply(
                application.Resources,
                LauncherThemePresets.Create(containingGame),
                false);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
