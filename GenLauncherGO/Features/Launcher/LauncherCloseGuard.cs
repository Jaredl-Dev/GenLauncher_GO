using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Features.Launching;
using GenLauncherGO.Features.Updating;
using GenLauncherGO.Shared.Dialogs;
using GenLauncherGO.Shared.Localization;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Features.Launcher;

/// <summary>
///     Applies one active-operation safety policy to launcher exit and restart requests.
/// </summary>
internal sealed class LauncherCloseGuard
{
    private readonly ILauncherDialogService _dialogService;
    private readonly LauncherLaunchCoordinator _launchCoordinator;

    private readonly ILogger<LauncherCloseGuard> _logger;

    private readonly LauncherPackageActivityService _packageActivityService;

    private readonly ILauncherStringLocalizer _stringLocalizer;

    public LauncherCloseGuard(
        LauncherLaunchCoordinator launchCoordinator,
        LauncherPackageActivityService packageActivityService,
        ILauncherDialogService dialogService,
        ILauncherStringLocalizer stringLocalizer,
        ILogger<LauncherCloseGuard> logger)
    {
        _launchCoordinator = launchCoordinator ?? throw new ArgumentNullException(nameof(launchCoordinator));
        _packageActivityService = packageActivityService ??
                                  throw new ArgumentNullException(nameof(packageActivityService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _launchCoordinator.PropertyChanged += LaunchCoordinator_PropertyChanged;
        _packageActivityService.ActivityChanged += PackageActivityService_ActivityChanged;
    }

    /// <summary>
    ///     Raised when launch or package activity changes and close safety should be reconsidered.
    /// </summary>
    public event EventHandler? ActiveOperationsChanged;

    /// <summary>
    ///     Determines whether all active operations permit the requested close behavior.
    /// </summary>
    public async Task<bool> CanCloseAsync(Window owner, LauncherCloseReason reason)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return await CanCloseDuringActiveLaunchAsync(owner) &&
               await CanCloseDuringActivePackageActivityAsync(owner, reason);
    }

    /// <summary>
    ///     Gets whether game launch or package activity currently makes an automatic restart prompt unsafe.
    /// </summary>
    public bool HasActiveOperations =>
        _launchCoordinator.IsLaunchInProgress ||
        _launchCoordinator.HasActiveProcess ||
        _packageActivityService.IsActive;

    private void LaunchCoordinator_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(LauncherLaunchCoordinator.IsLaunchInProgress) or
            nameof(LauncherLaunchCoordinator.HasActiveProcess))
        {
            ActiveOperationsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void PackageActivityService_ActivityChanged(object? sender, EventArgs eventArgs)
    {
        ActiveOperationsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     Determines whether a launched game or background process permits closure.
    /// </summary>
    private async Task<bool> CanCloseDuringActiveLaunchAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (!_launchCoordinator.IsLaunchInProgress &&
            !_launchCoordinator.HasActiveProcess)
        {
            return true;
        }

        bool processIsRunning = _launchCoordinator.HasActiveProcess;
        if (processIsRunning)
        {
            _logger.LogInformation("Launcher close was blocked because a launched process is still running.");
        }
        else
        {
            _logger.LogInformation("Launcher close was blocked because a launch workflow is still in progress.");
        }
        await _dialogService.ShowInfoAsync(
            new LauncherInfoDialogRequest(
                _stringLocalizer[processIsRunning
                    ? "RunningProcessCloseBlockedTitle"
                    : "LaunchCloseBlockedTitle"],
                _stringLocalizer[processIsRunning
                    ? "RunningProcessCloseBlockedDetails"
                    : "LaunchCloseBlockedDetails"]),
            owner);
        return false;
    }

    /// <summary>
    ///     Determines whether active package work or downloads permit closure.
    /// </summary>
    private async Task<bool> CanCloseDuringActivePackageActivityAsync(
        Window owner,
        LauncherCloseReason reason)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (!_packageActivityService.IsActive)
        {
            return true;
        }

        // A paused download is already stopped and its partial content is kept across the close, so there is
        // nothing to warn about. An actively transferring one still gets a heads-up that closing will stop it.
        if (reason != LauncherCloseReason.Restart && _packageActivityService.IsDownloadPaused)
        {
            _logger.LogInformation(
                "Launcher close proceeded during paused package activity {ActivityName}; its progress is kept.",
                _packageActivityService.ActiveDisplayName);
            return true;
        }

        if (reason == LauncherCloseReason.Restart)
        {
            _logger.LogInformation(
                "Launcher restart was blocked during package activity {ActivityName}.",
                _packageActivityService.ActiveDisplayName);
            await _dialogService.ShowInfoAsync(
                new LauncherInfoDialogRequest(
                    _stringLocalizer["RestartBlockedTitle"],
                    string.Format(CultureInfo.CurrentCulture,
                        _stringLocalizer["RestartBlockedActiveOperation"],
                        _packageActivityService.ActiveDisplayName)),
                owner);
            return false;
        }

        bool confirmed = await _dialogService.ShowWarningConfirmationAsync(
            new LauncherInfoDialogRequest(
                _stringLocalizer["PackageActivityInProgress"],
                string.Format(CultureInfo.CurrentCulture,
                    _stringLocalizer["ClosePackageActivityDetails"],
                    _packageActivityService.ActiveDisplayName)),
            _stringLocalizer["CloseAnyway"],
            owner);
        _logger.LogInformation(
            "Launcher close requested during package activity {ActivityName}. Confirmed: {Confirmed}.",
            _packageActivityService.ActiveDisplayName,
            confirmed);
        return confirmed;
    }
}
