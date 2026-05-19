using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Records every dialog the launcher raised and answers each one from a configured result.
/// </summary>
internal sealed class RecordingLauncherDialogService : ILauncherDialogService
{
    public List<LauncherInfoDialogRequest> InfoRequests { get; } = [];

    public List<LauncherInfoDialogRequest> ErrorRequests { get; } = [];

    public List<(LauncherInfoDialogRequest Request, string? ContinueText)> WarningConfirmationRequests { get; } = [];

    public List<ContentIntegrityReport> IntegrityReviewRequests { get; } = [];

    public bool WarningConfirmationResult { get; init; }

    public bool IntegrityReviewResult { get; init; }

    public Task ShowInfoAsync(LauncherInfoDialogRequest request, Window? owner = null)
    {
        InfoRequests.Add(request);
        return Task.CompletedTask;
    }

    public Task<bool> ShowInfoActionAsync(
        LauncherInfoDialogRequest request,
        string actionText,
        Window? owner = null)
    {
        return Task.FromException<bool>(Unexpected(nameof(ShowInfoActionAsync)));
    }

    public Task ShowErrorAsync(LauncherInfoDialogRequest request, Window? owner = null)
    {
        ErrorRequests.Add(request);
        return Task.CompletedTask;
    }

    public Task<bool> ShowWarningConfirmationAsync(
        LauncherInfoDialogRequest request,
        string? continueText = null,
        Window? owner = null)
    {
        WarningConfirmationRequests.Add((request, continueText));
        return Task.FromResult(WarningConfirmationResult);
    }

    public Task<string?> ShowModificationSelectionAsync(
        IReadOnlyList<string> modificationNames,
        Window? owner = null)
    {
        return Task.FromException<string?>(Unexpected(nameof(ShowModificationSelectionAsync)));
    }

    public Task<ManualModificationDialogResult?> ShowManualModificationImportAsync(
        IReadOnlyList<string> files,
        Window? owner = null)
    {
        return Task.FromException<ManualModificationDialogResult?>(
            Unexpected(nameof(ShowManualModificationImportAsync)));
    }

    public Task<bool> ShowIntegrityReviewAsync(ContentIntegrityReport report, Window? owner = null)
    {
        IntegrityReviewRequests.Add(report);
        return Task.FromResult(IntegrityReviewResult);
    }

    /// <summary>
    ///     Fails a test that raised a dialog this fake answers for nobody, rather than handing back a default that
    ///     reads like a real user choice. Give the dialog a recorded result here once a test actually needs one.
    /// </summary>
    private static InvalidOperationException Unexpected(string dialogName)
    {
        return new InvalidOperationException($"No test configures {dialogName} on this fake.");
    }
}
