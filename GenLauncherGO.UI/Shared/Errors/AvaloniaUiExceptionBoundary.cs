using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Shared.Localization;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.UI.Shared.Errors;

/// <summary>
/// Logs unexpected Avalonia operation failures and routes them to one localized user-facing error path.
/// </summary>
internal sealed class AvaloniaUiExceptionBoundary : IUiExceptionBoundary
{
    private readonly ILauncherDialogService _dialogService;

    private readonly ILauncherStringLocalizer _stringLocalizer;

    private readonly ILogger<AvaloniaUiExceptionBoundary> _logger;

    public AvaloniaUiExceptionBoundary(
        ILauncherDialogService dialogService,
        ILauncherStringLocalizer stringLocalizer,
        ILogger<AvaloniaUiExceptionBoundary> logger)
    {
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<UiOperationOutcome> ExecuteAsync(
        string operationContext,
        Func<Task> operation,
        Window? owner = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationContext);
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            await Dispatcher.UIThread.InvokeAsync(operation);
            return UiOperationOutcome.Succeeded;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "UI operation {OperationContext} was canceled.",
                operationContext);
            return UiOperationOutcome.Canceled;
        }
        catch (Exception exception)
        {
            return await HandleUnexpectedAsync(exception, operationContext, owner);
        }
    }

    /// <inheritdoc />
    public async Task<UiOperationOutcome> HandleUnexpectedAsync(
        Exception exception,
        string operationContext,
        Window? owner = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationContext);

        _logger.LogError(
            exception,
            "Unexpected UI failure while {OperationContext}.",
            operationContext);

        try
        {
            LauncherInfoDialogRequest request = new(
                _stringLocalizer["UnexpectedErrorTitle"],
                String.Concat(
                    _stringLocalizer["UnexpectedErrorDetails"],
                    Environment.NewLine,
                    Environment.NewLine,
                    exception.GetType().Name,
                    ": ",
                    exception.Message));
            Func<Task> showError = () => _dialogService.ShowErrorAsync(request, owner);
            if (Dispatcher.UIThread.CheckAccess())
            {
                await showError();
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(showError);
            }
        }
        catch (Exception dialogException)
        {
            _logger.LogError(
                dialogException,
                "The unexpected-failure dialog could not be shown for {OperationContext}.",
                operationContext);
        }

        return UiOperationOutcome.Failed;
    }
}
