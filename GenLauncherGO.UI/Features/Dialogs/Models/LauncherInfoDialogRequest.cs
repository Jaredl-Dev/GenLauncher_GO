using System;
using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.UI.Features.Dialogs.Models;

internal sealed class LauncherInfoDialogRequest(
    string mainMessage,
    string detailMessage,
    double? detailFontSize = null,
    string? cancelText = null)
{
    public string MainMessage { get; } = mainMessage ?? throw new ArgumentNullException(nameof(mainMessage));

    public string DetailMessage { get; } = detailMessage ?? throw new ArgumentNullException(nameof(detailMessage));

    public double? DetailFontSize { get; } = detailFontSize;

    public string? CancelText { get; } = cancelText;

    /// <summary>
    ///     Creates the canonical message shown when a user-requested settings change could not be persisted.
    /// </summary>
    public static LauncherInfoDialogRequest CreateSettingsSaveFailure(
        ILauncherStringLocalizer stringLocalizer)
    {
        ArgumentNullException.ThrowIfNull(stringLocalizer);
        return new LauncherInfoDialogRequest(
            stringLocalizer["SettingsSaveFailedTitle"],
            stringLocalizer["SettingsSaveFailedDetails"]);
    }
}
