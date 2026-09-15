using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using GenLauncherGO.Shared.Formatting;
using GenLauncherGO.Shared.Localization;

namespace GenLauncherGO.Features.Integrity;

internal sealed class IntegrityReviewViewModel
{
    public IntegrityReviewViewModel(
        ContentIntegrityReport report,
        ILauncherStringLocalizer stringLocalizer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(stringLocalizer);

        Title = stringLocalizer["IntegrityDialogTitle"];
        CancelActionText = stringLocalizer["CancelLaunch"];

        // Only managed remote content is compared against a trusted source, so every issue that offers a resolution
        // belongs to a managed target. Anything else the verification reports is blocking.
        if (report.HasBlockingIssues)
        {
            Description = stringLocalizer["IntegrityBlockedDescription"];
            PrimaryActionText = CancelActionText;
        }
        else
        {
            Description = string.Format(CultureInfo.CurrentCulture,
                stringLocalizer["IntegrityManagedDescription"],
                string.Join(", ", GetNames(report, issue => issue.SourceKind.IsManagedRemote())));
            PrimaryActionText = stringLocalizer["FixAndLaunch"];
        }

        IsPrimaryActionVisible = !string.Equals(
            PrimaryActionText,
            CancelActionText,
            StringComparison.Ordinal);
        ConfirmResolutionCommand = new RelayCommand(ConfirmResolution);
        CancelCommand = new RelayCommand(Cancel);

        IssueGroups =
        [
            .. report.Issues
                .GroupBy(issue => issue.TargetId, StringComparer.Ordinal)
                .Select(group => CreateIssueGroup(group, stringLocalizer))
        ];
    }

    public string Title { get; }

    public string Description { get; }

    public string PrimaryActionText { get; }

    public bool IsPrimaryActionVisible { get; }

    public string CancelActionText { get; }

    public IReadOnlyList<IntegrityReviewGroup> IssueGroups { get; }

    public IRelayCommand ConfirmResolutionCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public bool ResolutionConfirmed { get; private set; }

    public event EventHandler? CloseRequested;

    private void ConfirmResolution()
    {
        CompleteReview(true);
    }

    private void Cancel()
    {
        CompleteReview(false);
    }

    private void CompleteReview(bool resolutionConfirmed)
    {
        ResolutionConfirmed = resolutionConfirmed;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private static IntegrityReviewGroup CreateIssueGroup(
        IGrouping<string, ContentIntegrityIssue> issues,
        ILauncherStringLocalizer stringLocalizer)
    {
        var issueList = issues.ToList();
        ContentIntegrityIssue firstIssue = issueList[0];
        return new IntegrityReviewGroup(
            firstIssue.TargetDisplayName,
            CreateIssueSummary(issueList.Count, stringLocalizer),
            issueList
                .Select(issue => new IntegrityReviewEntry(
                    GetIssueKindLabel(issue.Kind, stringLocalizer),
                    CreateIssueDetails(issue),
                    GetActionLabel(issue.Action, stringLocalizer)))
                .ToList());
    }

    private static string CreateIssueSummary(
        int issueCount,
        ILauncherStringLocalizer stringLocalizer)
    {
        string format = issueCount == 1
            ? stringLocalizer["IntegrityIssueGroupSummarySingle"]
            : stringLocalizer["IntegrityIssueGroupSummaryMultiple"];
        return string.Format(CultureInfo.CurrentCulture, format, issueCount);
    }

    private static string GetActionLabel(
        IntegrityIssueAction action,
        ILauncherStringLocalizer stringLocalizer)
    {
        return action switch
        {
            IntegrityIssueAction.Block => stringLocalizer["IntegrityBlockGroup"],
            IntegrityIssueAction.Delete => stringLocalizer["IntegrityDeleteGroup"],
            IntegrityIssueAction.Repair => stringLocalizer["IntegrityRepairGroup"],
            IntegrityIssueAction.Redownload => stringLocalizer["IntegrityRedownloadGroup"],
            _ => action.ToString()
        };
    }

    private static string GetIssueKindLabel(
        IntegrityIssueKind kind,
        ILauncherStringLocalizer stringLocalizer)
    {
        return kind switch
        {
            IntegrityIssueKind.Untracked => stringLocalizer["IntegrityIssueUntrackedLabel"],
            IntegrityIssueKind.MissingFile => stringLocalizer["IntegrityIssueMissingFileLabel"],
            IntegrityIssueKind.ModifiedFile => stringLocalizer["IntegrityIssueModifiedFileLabel"],
            IntegrityIssueKind.UnexpectedFile => stringLocalizer["IntegrityIssueUnexpectedFileLabel"],
            IntegrityIssueKind.EmptyDirectory => stringLocalizer["IntegrityIssueEmptyDirectoryLabel"],
            IntegrityIssueKind.UnsafeLink => stringLocalizer["IntegrityIssueUnsafeLinkLabel"],
            IntegrityIssueKind.VerificationError => stringLocalizer["IntegrityIssueVerificationErrorLabel"],
            _ => kind.ToString()
        };
    }

    private static List<string> GetNames(
        ContentIntegrityReport report,
        Func<ContentIntegrityIssue, bool> predicate)
    {
        return report.Issues
            .Where(predicate)
            .Select(issue => issue.TargetDisplayName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    ///     Builds the detail text for one issue row.
    /// </summary>
    /// <remarks>
    ///     An issue about the target as a whole rather than one entry inside it carries "." as its relative path.
    ///     Printing that verbatim puts a bare dot where the reader expects a file name, so the whole-target case
    ///     contributes no path at all and the row is described by its kind and action alone.
    /// </remarks>
    private static string CreateIssueDetails(ContentIntegrityIssue issue)
    {
        List<string> parts = [];
        if (!IsWholeTargetPath(issue.RelativePath))
        {
            parts.Add(issue.RelativePath);
        }

        string downloadSizeText = CreateDownloadSizeText(issue);
        if (!string.IsNullOrWhiteSpace(downloadSizeText))
        {
            parts.Add(parts.Count > 0 ? $"({downloadSizeText})" : downloadSizeText);
        }

        string details = string.Join(" ", parts);
        if (string.IsNullOrWhiteSpace(issue.Message))
        {
            return details;
        }

        return details.Length == 0 ? issue.Message : $"{details} - {issue.Message}";
    }

    private static bool IsWholeTargetPath(string relativePath)
    {
        return string.IsNullOrWhiteSpace(relativePath) ||
               string.Equals(relativePath.Trim(), ".", StringComparison.Ordinal);
    }

    private static string CreateDownloadSizeText(ContentIntegrityIssue issue)
    {
        if (issue.Kind is not (IntegrityIssueKind.MissingFile or IntegrityIssueKind.ModifiedFile) ||
            issue.Action is not (IntegrityIssueAction.Repair or IntegrityIssueAction.Redownload) ||
            issue.ExpectedSizeBytes is not >= 0)
        {
            return string.Empty;
        }

        return ByteSizeFormatter.Format(issue.ExpectedSizeBytes.Value);
    }
}

internal sealed record IntegrityReviewGroup(
    string TargetDisplayName,
    string Summary,
    IReadOnlyList<IntegrityReviewEntry> Entries);

internal sealed record IntegrityReviewEntry(
    string IssueKindLabel,
    string RelativePath,
    string ActionLabel);
