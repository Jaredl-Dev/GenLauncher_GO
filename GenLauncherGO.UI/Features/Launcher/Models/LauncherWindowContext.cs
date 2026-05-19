using System;
using Avalonia.Controls;
using GenLauncherGO.UI.Features.Launcher.Support;
using GenLauncherGO.UI.Features.Launcher.ViewModels;

namespace GenLauncherGO.UI.Features.Launcher.Models;

/// <summary>
///     Carries the main window's identity into the workflows that act on it.
/// </summary>
/// <remarks>
///     These three travel together through every window-owning workflow, so they are validated once here rather than
///     re-checked at each entry point. The window builds one of these and passes it per call: the workflow coordinator
///     is a singleton and must not hold window state of its own.
/// </remarks>
internal sealed class LauncherWindowContext(
    MainWindowViewModel viewModel,
    LauncherWindowListController content,
    Window owner)
{
    /// <summary>
    ///     Gets the bindable state for the window.
    /// </summary>
    public MainWindowViewModel ViewModel { get; } = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

    /// <summary>
    ///     Gets the controller for the window's content lists and tabs.
    /// </summary>
    public LauncherWindowListController Content { get; } = content ?? throw new ArgumentNullException(nameof(content));

    /// <summary>
    ///     Gets the window that owns dialogs raised by a workflow.
    /// </summary>
    public Window Owner { get; } = owner ?? throw new ArgumentNullException(nameof(owner));
}
