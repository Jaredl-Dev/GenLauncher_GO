using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Features.Launcher;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Features.Startup;

/// <summary>
///     Records a safe restart request for the application host to execute after Avalonia shutdown and cleanup.
/// </summary>
internal sealed class LauncherRestartCoordinator
{
    /// <summary>
    ///     The shared active-operation close guard.
    /// </summary>
    private readonly LauncherCloseGuard _closeGuard;

    /// <summary>
    ///     The logger used for restart-request diagnostics.
    /// </summary>
    private readonly ILogger<LauncherRestartCoordinator> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LauncherRestartCoordinator" /> class.
    /// </summary>
    /// <param name="closeGuard">The shared active-operation close guard.</param>
    /// <param name="logger">The logger used for restart-request diagnostics.</param>
    public LauncherRestartCoordinator(
        LauncherCloseGuard closeGuard,
        ILogger<LauncherRestartCoordinator> logger)
    {
        _closeGuard = closeGuard ?? throw new ArgumentNullException(nameof(closeGuard));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Gets the replacement path requested for application shutdown.
    /// </summary>
    public LauncherRestartKind RestartKind { get; private set; }

    /// <summary>
    ///     Attempts to record an application restart request after applying the shared active-operation guard.
    /// </summary>
    /// <param name="owner">The window that owns any blocking safety message.</param>
    /// <param name="restartKind">The replacement path shutdown should start.</param>
    /// <returns><see langword="true" /> when the restart request was accepted.</returns>
    public async Task<bool> TryRequestRestartAsync(
        Window owner,
        LauncherRestartKind restartKind = LauncherRestartKind.Normal)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (restartKind == LauncherRestartKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(restartKind));
        }

        if (!await _closeGuard.CanCloseAsync(owner, LauncherCloseReason.Restart))
        {
            return false;
        }

        RestartKind = restartKind;
        _logger.LogInformation("A safe launcher restart was requested with kind {RestartKind}.", restartKind);
        return true;
    }
}
