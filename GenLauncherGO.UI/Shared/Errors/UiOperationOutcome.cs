namespace GenLauncherGO.UI.Shared.Errors;

/// <summary>
///     Identifies how an operation completed at the shared UI exception boundary.
/// </summary>
internal enum UiOperationOutcome
{
    Succeeded,
    Canceled,
    Failed
}
