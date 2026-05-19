using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Shared.Localization;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.UI.Features.Launcher.Services;

/// <summary>
///     Applies the shared admission policy for mutually exclusive package workflows.
/// </summary>
internal sealed class LauncherPackageActivityAdmissionService
{
    private readonly ILauncherDialogService _dialogService;
    private readonly ILogger<LauncherPackageActivityAdmissionService> _logger;
    private readonly LauncherPackageActivityService _packageActivityService;
    private readonly ILauncherStringLocalizer _stringLocalizer;

    public LauncherPackageActivityAdmissionService(
        LauncherPackageActivityService packageActivityService,
        ILauncherDialogService dialogService,
        ILauncherStringLocalizer stringLocalizer,
        ILogger<LauncherPackageActivityAdmissionService> logger)
    {
        _packageActivityService =
            packageActivityService ?? throw new ArgumentNullException(nameof(packageActivityService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Releases a paused download and reports whether another package workflow may start.
    /// </summary>
    public async Task<bool> EnsureCanStartAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        await _packageActivityService.ReleasePausedDownloadAsync();
        if (!_packageActivityService.IsActive)
        {
            return true;
        }

        await ShowInProgressAsync(owner);
        return false;
    }

    /// <summary>
    ///     Releases a paused download and reserves package activity for a non-download workflow.
    /// </summary>
    public async Task<LauncherPackageActivityService.LauncherPackageActivityLease?> TryReserveAsync(
        string displayName,
        Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        await _packageActivityService.ReleasePausedDownloadAsync();
        if (_packageActivityService.TryBegin(
                displayName,
                out LauncherPackageActivityService.LauncherPackageActivityLease? activityLease))
        {
            return activityLease ??
                   throw new InvalidOperationException("Package activity lease was not created.");
        }

        await ShowInProgressAsync(owner);
        return null;
    }

    /// <summary>
    ///     Shows the shared active-package-workflow message.
    /// </summary>
    public async Task ShowInProgressAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        await _dialogService.ShowInfoAsync(
            new LauncherInfoDialogRequest(
                _stringLocalizer["PackageActivityInProgress"],
                _stringLocalizer["PackageActivityInProgressDetails"]),
            owner);
        _logger.LogWarning(
            "Package activity {ActivityName} is already active; blocked a new package workflow.",
            _packageActivityService.ActiveDisplayName);
    }
}
