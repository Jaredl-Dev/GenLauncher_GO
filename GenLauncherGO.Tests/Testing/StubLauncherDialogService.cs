using Avalonia.Controls;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;

namespace GenLauncherGO.Tests.Testing;

internal static class StubLauncherDialogService
{
    /// <summary>
    ///     Creates a dialog service that answers every warning confirmation with <paramref name="confirmed" />.
    /// </summary>
    /// <remarks>
    ///     A substitute rather than a hand-written fake because the callers assert the request, continue text, and
    ///     owner window a workflow raised the dialog with, none of which
    ///     <see cref="RecordingLauncherDialogService" /> records.
    /// </remarks>
    public static ILauncherDialogService AnsweringWarningConfirmations(bool confirmed)
    {
        ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
        dialogService.ShowWarningConfirmationAsync(
                Arg.Any<LauncherInfoDialogRequest>(),
                Arg.Any<string?>(),
                Arg.Any<Window?>())
            .Returns(confirmed);
        return dialogService;
    }
}
