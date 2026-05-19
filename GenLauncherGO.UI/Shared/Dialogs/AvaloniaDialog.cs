using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace GenLauncherGO.UI.Shared.Dialogs;

/// <summary>
///     Shows an Avalonia dialog with the active application window as its owner when one is available.
/// </summary>
internal static class AvaloniaDialog
{
    /// <summary>
    ///     Closes a modal window with its result, or closes an ownerless window without supplying one.
    /// </summary>
    public static void CloseWithResult(Window dialog, bool? result)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        if (dialog.IsDialog && result.HasValue)
        {
            dialog.Close(result.Value);
            return;
        }

        dialog.Close();
    }

    /// <summary>
    ///     Shows a modal dialog when an owner resolves; otherwise shows it ownerless and completes only after it closes.
    /// </summary>
    public static async Task<TResult> ShowAsync<TResult>(
        Window dialog,
        Window? owner,
        Func<TResult> readOwnerlessResult)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(readOwnerlessResult);

        Window? resolvedOwner = ResolveOwner(dialog, owner);

        if (resolvedOwner != null)
        {
            return await dialog.ShowDialog<TResult>(resolvedOwner);
        }

        TaskCompletionSource<TResult> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.Closed += DialogClosed;
        try
        {
            dialog.Show();
            return await completion.Task;
        }
        finally
        {
            dialog.Closed -= DialogClosed;
        }

        void DialogClosed(object? sender, EventArgs eventArgs)
        {
            completion.TrySetResult(readOwnerlessResult());
        }
    }

    /// <summary>
    ///     Applies the shared dialog centering policy and resolves the preferred or active application owner.
    /// </summary>
    public static Window? ResolveOwner(Window dialog, Window? preferredOwner)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        Window? resolvedOwner = preferredOwner ?? ResolveApplicationOwner(dialog);
        dialog.WindowStartupLocation = resolvedOwner == null
            ? WindowStartupLocation.CenterScreen
            : WindowStartupLocation.CenterOwner;
        return resolvedOwner;
    }

    private static Window? ResolveApplicationOwner(Window dialog)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return null;
        }

        Window? activeWindow =
            desktop.Windows.FirstOrDefault(window => !ReferenceEquals(window, dialog) && window.IsActive);
        if (activeWindow != null)
        {
            return activeWindow;
        }

        if (desktop.MainWindow is { } mainWindow &&
            !ReferenceEquals(mainWindow, dialog) &&
            mainWindow.IsVisible)
        {
            return mainWindow;
        }

        return desktop.Windows.FirstOrDefault(window => !ReferenceEquals(window, dialog) && window.IsVisible);
    }
}
