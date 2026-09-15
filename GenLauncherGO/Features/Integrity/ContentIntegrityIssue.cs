namespace GenLauncherGO.Features.Integrity;

internal sealed record ContentIntegrityIssue(
    string TargetId,
    string TargetDisplayName,
    ContentSourceKind SourceKind,
    IntegrityIssueKind Kind,
    IntegrityIssueAction Action,
    string RelativePath,
    string? Message = null,
    long? ExpectedSizeBytes = null);
