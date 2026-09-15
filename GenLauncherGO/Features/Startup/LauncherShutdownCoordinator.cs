using System;
using System.Threading;
using GenLauncherGO.Features.Launching;
using GenLauncherGO.Features.Updating;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Features.Startup;

/// <summary>
///     Completes shutdown cleanup once and starts an accepted replacement process only after releasing the
///     single-instance guard.
/// </summary>
internal sealed class LauncherShutdownCoordinator
{
    private readonly ILauncherHostEnvironmentService _hostEnvironmentService;
    private readonly ILauncherApplicationUpdateService _applicationUpdateService;
    private readonly ILaunchPreparationService _launchPreparationService;
    private readonly ILogger<LauncherShutdownCoordinator> _logger;
    private bool _shutdownCompleted;

    public LauncherShutdownCoordinator(
        ILaunchPreparationService launchPreparationService,
        ILauncherHostEnvironmentService hostEnvironmentService,
        ILauncherApplicationUpdateService applicationUpdateService,
        ILogger<LauncherShutdownCoordinator> logger)
    {
        _launchPreparationService = launchPreparationService ??
                                    throw new ArgumentNullException(nameof(launchPreparationService));
        _hostEnvironmentService = hostEnvironmentService ??
                                  throw new ArgumentNullException(nameof(hostEnvironmentService));
        _applicationUpdateService = applicationUpdateService ??
                                    throw new ArgumentNullException(nameof(applicationUpdateService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Restores transient game-folder state, then releases the process guard and restarts when requested.
    /// </summary>
    public void Shutdown(
        LauncherPaths paths,
        LauncherRestartKind restartKind,
        Action releaseSingleInstance)
    {
        if (_shutdownCompleted)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(releaseSingleInstance);

        _shutdownCompleted = true;
        Cleanup(paths);
        _logger.LogInformation(
            "Launcher shutdown cleanup finished. Restart requested: {RestartRequested}.",
            restartKind != LauncherRestartKind.None);
        if (restartKind == LauncherRestartKind.None)
        {
            return;
        }

        releaseSingleInstance();
        if (restartKind == LauncherRestartKind.ApplicationUpdate)
        {
            if (_applicationUpdateService.TryHandoffToUpdateAndRestart())
            {
                _logger.LogInformation("The downloaded launcher application update was handed to Velopack.");
                return;
            }

            _logger.LogWarning(
                "The Velopack application-update handoff failed; falling back to a normal launcher restart.");
        }

        LauncherRestartResult result = _hostEnvironmentService.TryRestartCurrentProcess();
        if (!result.Succeeded)
        {
            _logger.LogError(
                "The requested launcher restart could not start a replacement process. {RestartError}",
                result.ErrorMessage);
        }
    }

    private void Cleanup(LauncherPaths paths)
    {
        try
        {
            if (!_launchPreparationService.Cleanup(paths, CancellationToken.None))
            {
                _logger.LogWarning("Deployment cleanup did not complete successfully on shutdown.");
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Could not return the game folder to its original state on shutdown.");
        }
    }
}
