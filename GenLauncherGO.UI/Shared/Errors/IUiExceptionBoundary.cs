using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace GenLauncherGO.UI.Shared.Errors;

/// <summary>
///     Runs UI operations through the launcher's consistent exception and user-notification boundary.
/// </summary>
internal interface IUiExceptionBoundary
{
    /// <summary>
    ///     Runs an asynchronous UI operation and converts completion, cancellation, or failure to a typed outcome.
    /// </summary>
    /// <param name="operationContext">A diagnostic description of the operation.</param>
    /// <param name="operation">The operation to run.</param>
    /// <param name="owner">The optional owner for a failure dialog.</param>
    /// <returns>The typed operation outcome.</returns>
    Task<UiOperationOutcome> ExecuteAsync(
        string operationContext,
        Func<Task> operation,
        Window? owner = null);

    /// <summary>
    ///     Handles an unexpected exception raised by an Avalonia event boundary.
    /// </summary>
    /// <param name="exception">The unexpected exception.</param>
    /// <param name="operationContext">A diagnostic description of the operation.</param>
    /// <param name="owner">The optional owner for a failure dialog.</param>
    /// <returns>The typed failure outcome.</returns>
    Task<UiOperationOutcome> HandleUnexpectedAsync(
        Exception exception,
        string operationContext,
        Window? owner = null);
}
