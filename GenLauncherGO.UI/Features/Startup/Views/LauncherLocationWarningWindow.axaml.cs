using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.UI.Features.Startup.Views;

/// <summary>
/// Blocks startup when the standalone executable is inside a configured game installation.
/// </summary>
internal partial class LauncherLocationWarningWindow : Window
{
    public LauncherLocationWarningWindow()
    {
        InitializeComponent();
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
        LauncherThemeResourceApplier.Apply(this, LauncherThemePresets.Create(containingGame));
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
