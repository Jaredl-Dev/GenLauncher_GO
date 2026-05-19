using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.UI.Features.Integrity.ViewModels;

internal sealed class IntegrityReviewViewModel
{
    public IntegrityReviewViewModel(
        ContentIntegrityReport report,
        ILauncherStringLocalizer stringLocalizer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(stringLocalizer);

        List<string> managed = GetNames(report, issue =>
            issue.SourceKind is ContentSourceKind.ManagedS3 or ContentSourceKind.ManagedSingleFile);
        List<string> manual = GetNames(report, issue => issue.Action == IntegrityIssueAction.Absorb);
        List<string> legacy = GetNames(report, issue => issue.Action == IntegrityIssueAction.TrustAsManual);

        Title = stringLocalizer["IntegrityDialogTitle"];
        CancelActionText = stringLocalizer["CancelLaunch"];
        if (report.HasBlockingIssues)
        {
            Description = stringLocalizer["IntegrityBlockedDescription"];
            PrimaryActionText = CancelActionText;
        }
        else if (managed.Count > 0 && manual.Count > 0)
        {
            Description = String.Format(
                stringLocalizer["IntegrityMixedDescription"],
                String.Join(", ", managed),
                String.Join(", ", manual));
            PrimaryActionText = stringLocalizer["FixAbsorbAndLaunch"];
        }
        else if (managed.Count > 0)
        {
            Description = String.Format(
                stringLocalizer["IntegrityManagedDescription"],
                String.Join(", ", managed));
            PrimaryActionText = stringLocalizer["FixAndLaunch"];
        }
        else if (manual.Count > 0)
        {
            Description = String.Format(
                stringLocalizer["IntegrityManualDescription"],
                String.Join(", ", manual));
            PrimaryActionText = stringLocalizer["AbsorbAndLaunch"];
        }
        else
        {
            Description = String.Format(
                stringLocalizer["IntegrityLegacyDescription"],
                String.Join(", ", legacy));
            PrimaryActionText = stringLocalizer["TrustAsManual"];
        }

        IsPrimaryActionVisible = !string.Equals(
            PrimaryActionText,
            CancelActionText,
            StringComparison.Ordinal);
        ConfirmResolutionCommand = new RelayCommand(ConfirmResolution);
        CancelCommand = new RelayCommand(Cancel);

        IssueGroups = [.. report.Issues
            .GroupBy(issue => issue.TargetId, StringComparer.Ordinal)
            .Select(group => CreateIssueGroup(group, stringLocalizer))];
    }

    public event EventHandler? CloseRequested;

    public string Title { get; }

    public string Description { get; }

    public string PrimaryActionText { get; }

    public bool IsPrimaryActionVisible { get; }

    public string CancelActionText { get; }

    public IReadOnlyList<IntegrityReviewGroup> IssueGroups { get; }

    public IRelayCommand ConfirmResolutionCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public bool ResolutionConfirmed { get; private set; }

    private void ConfirmResolution()
    {
        ResolutionConfirmed = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel()
    {
        ResolutionConfirmed = false;
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
            IntegrityIssueAction.Absorb => stringLocalizer["IntegrityAbsorbGroup"],
            IntegrityIssueAction.TrustAsManual => stringLocalizer["IntegrityTrustGroup"],
            _ => action.ToString(),
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
            _ => kind.ToString(),
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

    private static string CreateIssueDetails(ContentIntegrityIssue issue)
    {
        string relativePath = string.IsNullOrWhiteSpace(issue.RelativePath) ? "." : issue.RelativePath;
        string downloadSizeText = CreateDownloadSizeText(issue);
        if (!string.IsNullOrWhiteSpace(downloadSizeText))
        {
            relativePath = $"{relativePath} ({downloadSizeText})";
        }

        return string.IsNullOrWhiteSpace(issue.Message)
            ? relativePath
            : $"{relativePath} - {issue.Message}";
    }

    private static string CreateDownloadSizeText(ContentIntegrityIssue issue)
    {
        if (issue.Kind is not (IntegrityIssueKind.MissingFile or IntegrityIssueKind.ModifiedFile) ||
            issue.Action is not (IntegrityIssueAction.Repair or IntegrityIssueAction.Redownload) ||
            issue.ExpectedSizeBytes is not >= 0)
        {
            return string.Empty;
        }

        return FormatByteSize(issue.ExpectedSizeBytes.Value);
    }

    private static string FormatByteSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        int unitIndex = 0;
        while (value >= 1024D && unitIndex < units.Length - 1)
        {
            value /= 1024D;
            unitIndex++;
        }

        string format = unitIndex == 0 ? "0" : "0.0";
        return string.Format(
            CultureInfo.CurrentCulture,
            "{0} {1}",
            value.ToString(format, CultureInfo.CurrentCulture),
            units[unitIndex]);
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
