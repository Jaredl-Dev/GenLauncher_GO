using System;
using System.Collections.Generic;
using System.Linq;

namespace GenLauncherGO.Core.Integrity.Models;

/// <summary>
///     Contains all issues found while verifying active launch content.
/// </summary>
public sealed record ContentIntegrityReport
{
    public ContentIntegrityReport(IReadOnlyList<ContentIntegrityIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = Array.AsReadOnly(issues.ToArray());
    }

    public IReadOnlyList<ContentIntegrityIssue> Issues { get; }

    public bool HasIssues => Issues.Count > 0;

    public bool HasUnknownLegacyIssues => Issues.Any(issue => issue.Action == IntegrityIssueAction.TrustAsManual);

    public bool HasBlockingIssues => Issues.Any(issue => issue.Action == IntegrityIssueAction.Block);
}
