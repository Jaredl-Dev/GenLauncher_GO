using System;
using System.Threading.Tasks;
using GenLauncherGO.Features.Mods;
using GenLauncherGO.Features.Mods.Views;
using GenLauncherGO.Shared.Dialogs;
using GenLauncherGO.Shared.Localization;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Features.Startup;

/// <summary>
///     Shows startup messages with the themed Avalonia launcher dialog.
/// </summary>
internal sealed class AvaloniaStartupDialogService : IStartupDialogService
{
    private readonly ILogger<AvaloniaStartupDialogService> _logger;

    private readonly ILauncherStringLocalizer _stringLocalizer;

    public AvaloniaStartupDialogService(
        ILauncherStringLocalizer stringLocalizer,
        ILogger<AvaloniaStartupDialogService> logger)
    {
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task ShowMessageAsync(string message)
    {
        return ShowMessageAsync(_stringLocalizer["Info"], message);
    }

    public Task ShowMessageAsync(string title, string message)
    {
        _logger.LogDebug("Showing startup message dialog {DialogTitle}.", title);
        return ShowDialogAsync(
            new LauncherInfoDialogRequest(title, message),
            InfoDialogKind.Info);
    }

    public async Task<bool> ShowRetryCancelWarningAsync(string title, string message)
    {
        bool retry = await ShowDialogAsync(
            new LauncherInfoDialogRequest(
                title,
                message,
                cancelText: _stringLocalizer["Cancel"]),
            InfoDialogKind.WarningConfirmation,
            _stringLocalizer["Retry"]);
        _logger.LogInformation(
            "Startup retry/cancel warning {DialogTitle} completed with retry: {Retry}.",
            title,
            retry);
        return retry;
    }

    private Task<bool> ShowDialogAsync(
        LauncherInfoDialogRequest request,
        InfoDialogKind kind,
        string? continueText = null)
    {
        InfoWindow dialog = new(
            request,
            kind,
            continueText,
            request.CancelText ?? _stringLocalizer["Cancel"]);
        return AvaloniaDialog.ShowAsync(dialog, null, () => dialog.Accepted);
    }
}
