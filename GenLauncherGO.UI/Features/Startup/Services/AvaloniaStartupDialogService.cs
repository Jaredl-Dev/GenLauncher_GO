using System;
using System.Threading.Tasks;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Mods.ViewModels;
using GenLauncherGO.UI.Features.Mods.Views;
using GenLauncherGO.UI.Features.Startup.Contracts;
using GenLauncherGO.UI.Shared.Dialogs;
using GenLauncherGO.UI.Shared.Localization;
using GenLauncherGO.UI.Shared.Themes;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.UI.Features.Startup.Services;

/// <summary>
/// Shows startup messages with the themed Avalonia launcher dialog.
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
        _logger.LogWarning("Showing startup message dialog.");
        return ShowDialogAsync(
            new LauncherInfoDialogRequest(_stringLocalizer["Info"], message),
            InfoDialogKind.Info,
            LauncherThemePresets.Create(SupportedGame.ZeroHour));
    }

    public Task ShowThemedMessageAsync(string title, string message, ColorsInfo colors)
    {
        ArgumentNullException.ThrowIfNull(colors);

        _logger.LogInformation("Showing themed startup message dialog {DialogTitle}.", title);
        return ShowDialogAsync(
            new LauncherInfoDialogRequest(title, message),
            InfoDialogKind.Info,
            colors);
    }

    public async Task<bool> ShowRetryCancelWarningAsync(string title, string message)
    {
        bool retry = await ShowDialogAsync(
            new LauncherInfoDialogRequest(
                title,
                message,
                cancelText: _stringLocalizer["Cancel"]),
            InfoDialogKind.WarningConfirmation,
            LauncherThemePresets.Create(SupportedGame.ZeroHour),
            _stringLocalizer["Retry"]);
        _logger.LogWarning(
            "Startup retry/cancel warning {DialogTitle} completed with retry: {Retry}.",
            title,
            retry);
        return retry;
    }

    private Task<bool> ShowDialogAsync(
        LauncherInfoDialogRequest request,
        InfoDialogKind kind,
        ColorsInfo colors,
        string? continueText = null)
    {
        InfoWindow dialog = new(
            request,
            kind,
            colors,
            continueText,
            request.CancelText ?? _stringLocalizer["Cancel"]);
        return AvaloniaDialog.ShowAsync(dialog, owner: null, () => dialog.Accepted);
    }
}
