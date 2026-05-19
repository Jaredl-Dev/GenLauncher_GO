using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.UI.Features.Dialogs.Models;

namespace GenLauncherGO.UI.Features.Dialogs.Contracts;

/// <summary>
/// Shows launcher-owned dialogs and returns user choices to callers.
/// </summary>
/// <remarks>
/// Implementations prefer an explicit owner, fall back to an active application window, and keep ownerless dialogs
/// alive until their window closes when no owner exists.
/// </remarks>
internal interface ILauncherDialogService
{
    /// <summary>
    /// Shows a themed information dialog.
    /// </summary>
    Task ShowInfoAsync(LauncherInfoDialogRequest request, Window? owner = null);

    /// <summary>
    /// Shows a themed error dialog.
    /// </summary>
    Task ShowErrorAsync(LauncherInfoDialogRequest request, Window? owner = null);

    /// <summary>
    /// Shows a themed warning confirmation dialog.
    /// </summary>
    /// <returns><see langword="true"/> when the user chose to continue.</returns>
    Task<bool> ShowWarningConfirmationAsync(
        LauncherInfoDialogRequest request,
        string? continueText = null,
        Window? owner = null);

    /// <summary>
    /// Shows the repository modification selection dialog.
    /// </summary>
    /// <returns>The selected modification name, or <see langword="null"/> when the dialog was canceled.</returns>
    Task<string?> ShowModificationSelectionAsync(
        IReadOnlyList<string> modificationNames,
        Window? owner = null);

    /// <summary>
    /// Shows the manual import details dialog.
    /// </summary>
    /// <returns>The entered import details, or <see langword="null"/> when the dialog was canceled.</returns>
    Task<ManualModificationDialogResult?> ShowManualModificationImportAsync(
        ManualModificationDialogRequest request,
        Window? owner = null);

    /// <summary>
    /// Shows the integrity review dialog.
    /// </summary>
    /// <returns><see langword="true"/> when the user confirmed the offered resolution.</returns>
    Task<bool> ShowIntegrityReviewAsync(
        ContentIntegrityReport report,
        Window? owner = null);
}
