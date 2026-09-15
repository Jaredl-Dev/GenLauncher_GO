using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using GenLauncherGO.Features.Launcher;
using GenLauncherGO.Features.Startup;
using GenLauncherGO.Shared.Dialogs;
using GenLauncherGO.Shared.Localization;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Features.Updating;

/// <summary>
///     Sequences background application update work, safe prompting, deferral, and restart requests.
/// </summary>
internal sealed class LauncherApplicationUpdateCoordinator
{
    private readonly ILauncherApplicationUpdateService _applicationUpdateService;
    private readonly LauncherCloseGuard _closeGuard;
    private readonly ILauncherDialogService _dialogService;
    private readonly Func<Window, bool> _isLauncherActive;
    private readonly ILogger<LauncherApplicationUpdateCoordinator> _logger;
    private readonly LauncherRestartCoordinator _restartCoordinator;
    private readonly ILauncherStringLocalizer _stringLocalizer;
    private string? _downloadedVersion;
    private Window? _owner;
    private bool _promptInProgress;
    private bool _started;

    public LauncherApplicationUpdateCoordinator(
        ILauncherApplicationUpdateService applicationUpdateService,
        ILauncherDialogService dialogService,
        LauncherCloseGuard closeGuard,
        LauncherRestartCoordinator restartCoordinator,
        ILauncherStringLocalizer stringLocalizer,
        ILogger<LauncherApplicationUpdateCoordinator> logger)
        : this(
            applicationUpdateService,
            dialogService,
            closeGuard,
            restartCoordinator,
            stringLocalizer,
            logger,
            static owner => owner.IsActive)
    {
    }

    internal LauncherApplicationUpdateCoordinator(
        ILauncherApplicationUpdateService applicationUpdateService,
        ILauncherDialogService dialogService,
        LauncherCloseGuard closeGuard,
        LauncherRestartCoordinator restartCoordinator,
        ILauncherStringLocalizer stringLocalizer,
        ILogger<LauncherApplicationUpdateCoordinator> logger,
        Func<Window, bool> isLauncherActive)
    {
        _applicationUpdateService = applicationUpdateService ??
                                    throw new ArgumentNullException(nameof(applicationUpdateService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _closeGuard = closeGuard ?? throw new ArgumentNullException(nameof(closeGuard));
        _restartCoordinator = restartCoordinator ?? throw new ArgumentNullException(nameof(restartCoordinator));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _isLauncherActive = isLauncherActive ?? throw new ArgumentNullException(nameof(isLauncherActive));
        _closeGuard.ActiveOperationsChanged += CloseGuard_ActiveOperationsChanged;
    }

    /// <summary>
    ///     Runs the session's single automatic check after the main-window startup prompts have completed.
    /// </summary>
    public async Task StartAsync(Window owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_started)
        {
            return;
        }

        _owner = owner;
        _started = true;
        _downloadedVersion = await _applicationUpdateService
            .CheckAndDownloadUpdateAsync(cancellationToken);
        _logger.LogDebug(
            "The session application-update check completed. Downloaded version: {DownloadedVersion}.",
            _downloadedVersion);
        await TryShowPromptIfReadyAsync(owner);
    }

    /// <summary>
    ///     Reconsiders a downloaded update when the launcher becomes active after a deferred prompt.
    /// </summary>
    public Task HandleLauncherActivatedAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return TryShowPromptIfReadyAsync(owner);
    }

    private void CloseGuard_ActiveOperationsChanged(object? sender, EventArgs eventArgs)
    {
        if (_closeGuard.HasActiveOperations || _owner is not { } owner)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            _ = RetryPromptAfterActivityAsync(owner);
            return;
        }

        Dispatcher.UIThread.Post(() => _ = RetryPromptAfterActivityAsync(owner));
    }

    private async Task RetryPromptAfterActivityAsync(Window owner)
    {
        try
        {
            await TryShowPromptIfReadyAsync(owner);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Could not show the downloaded launcher application update after launcher activity finished.");
        }
    }

    private async Task TryShowPromptIfReadyAsync(Window owner)
    {
        string? downloadedVersion = _downloadedVersion;
        if (downloadedVersion == null ||
            _promptInProgress ||
            _closeGuard.HasActiveOperations ||
            !_isLauncherActive(owner))
        {
            return;
        }

        _promptInProgress = true;
        try
        {
            bool restartNow = await _dialogService.ShowInfoActionAsync(
                new LauncherInfoDialogRequest(
                    _stringLocalizer["ApplicationUpdateReadyTitle"],
                    string.Format(
                        CultureInfo.CurrentCulture,
                        _stringLocalizer["ApplicationUpdateReadyDetails"],
                        downloadedVersion),
                    cancelText: _stringLocalizer["RestartLater"]),
                _stringLocalizer["RestartNow"],
                owner);
            if (!restartNow)
            {
                _downloadedVersion = null;
                _logger.LogInformation(
                    "Application update {DownloadedVersion} will be applied on a later launcher start.",
                    downloadedVersion);
                return;
            }

            if (await _restartCoordinator.TryRequestRestartAsync(
                    owner,
                    LauncherRestartKind.ApplicationUpdate))
            {
                _downloadedVersion = null;
                _logger.LogInformation(
                    "Closing the launcher to apply downloaded application update {DownloadedVersion}.",
                    downloadedVersion);
                owner.Close();
            }
        }
        finally
        {
            _promptInProgress = false;
        }
    }
}
