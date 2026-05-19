using System;
using System.Threading;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Core.Startup.Models;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.UI.Features.Startup.Services;

/// <summary>
///     Completes shutdown cleanup once and starts an accepted replacement process only after releasing the
///     single-instance guard.
/// </summary>
internal sealed class LauncherShutdownCoordinator
{
    private readonly ILauncherHostEnvironmentService _hostEnvironmentService;
    private readonly ILaunchPreparationService _launchPreparationService;
    private readonly ILogger<LauncherShutdownCoordinator> _logger;
    private bool _shutdownCompleted;

    public LauncherShutdownCoordinator(
        ILaunchPreparationService launchPreparationService,
        ILauncherHostEnvironmentService hostEnvironmentService,
        ILogger<LauncherShutdownCoordinator> logger)
    {
        _launchPreparationService = launchPreparationService ??
                                    throw new ArgumentNullException(nameof(launchPreparationService));
        _hostEnvironmentService = hostEnvironmentService ??
                                  throw new ArgumentNullException(nameof(hostEnvironmentService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Restores transient game-folder state, then releases the process guard and restarts when requested.
    /// </summary>
    public void Shutdown(
        LauncherPaths paths,
        bool restartRequested,
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
        if (!restartRequested)
        {
            return;
        }

        releaseSingleInstance();
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
