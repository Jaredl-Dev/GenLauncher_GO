using System;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Remote;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.UI.Features.Startup.Contracts;
using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.UI.Features.Startup.ViewModels;

/// <summary>
/// Coordinates launcher initialization before the main window opens.
/// </summary>
internal sealed class InitWindowViewModel
{
    private readonly IRemoteConnectionProbe _connectionProbe;

    private readonly ILaunchPreparationService _launchPreparationService;

    private readonly ILauncherPathResolver _launcherPathResolver;

    private readonly ILauncherContentCatalog _catalog;

    private readonly LauncherRuntimeContext _runtimeContext;

    private readonly ILauncherStringLocalizer _stringLocalizer;

    private readonly IStartupDialogService _startupDialogService;

    private bool _startupAborted;

    public InitWindowViewModel(
        IRemoteConnectionProbe connectionProbe,
        ILaunchPreparationService launchPreparationService,
        ILauncherPathResolver launcherPathResolver,
        ILauncherContentCatalog catalog,
        LauncherRuntimeContext runtimeContext,
        ILauncherStringLocalizer stringLocalizer,
        IStartupDialogService startupDialogService)
    {
        _connectionProbe = connectionProbe ?? throw new ArgumentNullException(nameof(connectionProbe));
        _launchPreparationService = launchPreparationService ??
                                    throw new ArgumentNullException(nameof(launchPreparationService));
        _launcherPathResolver = launcherPathResolver ?? throw new ArgumentNullException(nameof(launcherPathResolver));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _runtimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _startupDialogService = startupDialogService ?? throw new ArgumentNullException(nameof(startupDialogService));
    }

    /// <summary>
    /// Occurs when launcher startup has completed and the main window can be shown.
    /// </summary>
    public event EventHandler<InitWindowStartupCompletedEventArgs>? StartupCompleted;

    /// <summary>
    /// Occurs when startup cancellation requires application shutdown.
    /// </summary>
    public event EventHandler? ShutdownRequested;

    /// <summary>
    /// Runs startup preparation and raises completion events for the owning view.
    /// </summary>
    /// <returns>A task that completes when startup has either completed or aborted.</returns>
    public async Task StartAsync()
    {
        await Task.Run(() => _launcherPathResolver.PrepareGameDirectories(
            _runtimeContext.LauncherPaths,
            cleanTemporaryDirectory: true));

        bool connected = await PrepareLauncherAsync();
        if (_startupAborted)
        {
            return;
        }

        _runtimeContext.Connected = connected;
        StartupCompleted?.Invoke(this, new InitWindowStartupCompletedEventArgs(connected));
    }

    /// <summary>
    /// Initializes launcher content, runtime state, and remote connection state.
    /// </summary>
    /// <returns><see langword="true"/> when remote services are available.</returns>
    public async Task<bool> PrepareLauncherAsync()
    {
        while (!await RecoverDeploymentAsync())
        {
            if (!await _startupDialogService.ShowRetryCancelWarningAsync(
                    "GenLauncherGO",
                    _stringLocalizer["DeploymentRecoveryFailed"]))
            {
                _startupAborted = true;
                ShutdownRequested?.Invoke(this, EventArgs.Empty);
                return false;
            }
        }

        _runtimeContext.ApplyActiveGameDefaults();

        bool connected = await CheckConnectionAsync();

        await _catalog.InitDataAsync(
            CreateCatalogInitializationRequest(connected),
            CancellationToken.None);

        if (_catalog.RepositoryModificationNames == null)
        {
            connected = false;
        }

        if (connected)
        {
            LauncherContent? lastActivatedMod = _catalog.Data.GetSelectedMod();

            if (lastActivatedMod != null)
            {
                await _catalog.ReadPatchesAndAddonsForModAsync(
                    lastActivatedMod.ContentKey,
                    CancellationToken.None);
            }
        }
        else
        {
            await _startupDialogService.ShowThemedMessageAsync(
                _stringLocalizer["Info"],
                _stringLocalizer["CannotConnect"],
                _runtimeContext.Colors);
        }

        return connected;
    }

    private LauncherContentCatalogInitializationRequest CreateCatalogInitializationRequest(bool connected)
    {
        return new LauncherContentCatalogInitializationRequest(
            CreateRemoteManifestUri(connected),
            _runtimeContext.LauncherPaths);
    }

    private Uri? CreateRemoteManifestUri(bool connected)
    {
        if (!connected || string.IsNullOrWhiteSpace(_runtimeContext.ModsRepositoryUrl))
        {
            return null;
        }

        return new Uri(_runtimeContext.ModsRepositoryUrl, UriKind.Absolute);
    }

    /// <summary>
    /// Attempts to recover from incomplete launch deployment state.
    /// </summary>
    /// <returns><see langword="true"/> when recovery succeeded.</returns>
    private Task<bool> RecoverDeploymentAsync()
    {
        return Task.Run(() =>
        {
            return _launchPreparationService.Recover(
                _runtimeContext.LauncherPaths,
                CancellationToken.None);
        });
    }

    /// <summary>
    /// Checks whether the active remote manifest URL is reachable.
    /// </summary>
    /// <returns><see langword="true"/> when the remote URL is reachable.</returns>
    private async Task<bool> CheckConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(_runtimeContext.ModsRepositoryUrl))
        {
            return false;
        }

        return await _connectionProbe.CanConnectAsync(
            new Uri(_runtimeContext.ModsRepositoryUrl, UriKind.Absolute),
            CancellationToken.None);
    }
}
