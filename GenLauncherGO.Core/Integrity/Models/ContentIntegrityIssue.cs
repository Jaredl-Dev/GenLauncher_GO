namespace GenLauncherGO.Core.Integrity.Models;

public sealed record ContentIntegrityIssue(
    string TargetId,
    string TargetDisplayName,
    ContentSourceKind SourceKind,
    IntegrityIssueKind Kind,
    IntegrityIssueAction Action,
    string RelativePath,
    string? Message = null,
    long? ExpectedSizeBytes = null);
