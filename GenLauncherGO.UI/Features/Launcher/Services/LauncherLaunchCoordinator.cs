using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Shared.Localization;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.UI.Features.Launcher.Services;

/// <summary>
/// Coordinates launch verification, deployment preparation, process launch, and cleanup for the launcher UI.
/// </summary>
internal sealed class LauncherLaunchCoordinator : ObservableObject
{
    private readonly IGameProcessLauncher _gameProcessLauncher;

    private readonly ILaunchPreparationService _launchPreparationService;

    private readonly LaunchContentIntegrityCoordinator _launchContentIntegrityCoordinator;

    private readonly LauncherPackageActivityService _packageActivityService;

    private readonly ILauncherDialogService _launcherDialogService;

    private readonly LauncherRuntimePathContext _runtimePaths;

    private readonly ILauncherPreferencesService _launcherPreferencesService;

    private readonly ILauncherStringLocalizer _stringLocalizer;

    private readonly ILogger<LauncherLaunchCoordinator> _logger;

    private bool _isGameRunning;

    private IGameProcessLaunchOperation? _activeProcessLaunch;

    private bool _isLaunchVerificationRunning;

    private bool _isWorldBuilderRunning;

    private bool _isPreparingLaunch;

    private bool _shouldHideLauncherWindow;

    public LauncherLaunchCoordinator(
        ILauncherPreferencesService launcherPreferencesService,
        ILaunchPreparationService launchPreparationService,
        IGameProcessLauncher gameProcessLauncher,
        LaunchContentIntegrityCoordinator launchContentIntegrityCoordinator,
        LauncherPackageActivityService packageActivityService,
        LauncherRuntimePathContext runtimePaths,
        ILauncherStringLocalizer stringLocalizer,
        ILauncherDialogService launcherDialogService,
        ILogger<LauncherLaunchCoordinator> logger)
    {
        _launcherPreferencesService = launcherPreferencesService ??
                                      throw new ArgumentNullException(nameof(launcherPreferencesService));
        _launchPreparationService = launchPreparationService ??
                                    throw new ArgumentNullException(nameof(launchPreparationService));
        _gameProcessLauncher = gameProcessLauncher ?? throw new ArgumentNullException(nameof(gameProcessLauncher));
        _launchContentIntegrityCoordinator = launchContentIntegrityCoordinator ??
                                             throw new ArgumentNullException(
                                                 nameof(launchContentIntegrityCoordinator));
        _packageActivityService = packageActivityService ?? throw new ArgumentNullException(nameof(packageActivityService));
        _runtimePaths = runtimePaths ?? throw new ArgumentNullException(nameof(runtimePaths));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _launcherDialogService =
            launcherDialogService ?? throw new ArgumentNullException(nameof(launcherDialogService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsPreparingLaunch => _isPreparingLaunch;

    public bool HasActiveProcess => _activeProcessLaunch != null;

    public bool IsLaunchInProgress => _isGameRunning || _isWorldBuilderRunning;

    public string? ActiveProcessName => _activeProcessLaunch?.CurrentExecutableName;

    public bool ShouldHideLauncherWindow => _shouldHideLauncherWindow;

    /// <summary>
    /// Force closes the currently tracked launched process family.
    /// </summary>
    public bool ForceCloseActiveProcess()
    {
        IGameProcessLaunchOperation? activeProcessLaunch = _activeProcessLaunch;
        if (activeProcessLaunch == null)
        {
            return false;
        }

        activeProcessLaunch.ForceClose();
        return true;
    }

    /// <summary>
    /// Verifies content, prepares deployment, tracks the launched process family, and cleans up afterward.
    /// </summary>
    public async Task<LauncherLaunchResult> LaunchAsync(
        LauncherLaunchRequest request,
        IReadOnlyList<ILaunchContentIntegrityProgressTarget> activeProgressTargets,
        Window owner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(activeProgressTargets);
        ArgumentNullException.ThrowIfNull(owner);

        LauncherLaunchFailureKind? busyFailure = await TryBeginLaunchAsync(request.TargetKind);
        if (busyFailure.HasValue)
        {
            return LauncherLaunchResult.Stopped(busyFailure.Value);
        }

        try
        {
            LauncherPaths launchPaths = _runtimePaths.ActivePaths;
            return await LaunchCoreAsync(
                request,
                activeProgressTargets,
                owner,
                launchPaths,
                cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            EndLaunch(request.TargetKind);
        }
    }

    /// <summary>
    /// Marks the requested launch target as running when no conflicting launch workflow is active.
    /// </summary>
    private async Task<LauncherLaunchFailureKind?> TryBeginLaunchAsync(GameLaunchTargetKind targetKind)
    {
        if (_isLaunchVerificationRunning)
        {
            await ShowErrorAsync(_stringLocalizer["LaunchAborted"], _stringLocalizer["LaunchVerificationRunning"]);
            return LauncherLaunchFailureKind.VerificationAlreadyRunning;
        }

        if (_packageActivityService.IsActive)
        {
            await ShowErrorAsync(_stringLocalizer["LaunchAborted"], _stringLocalizer["LaunchVerificationRunning"]);
            return LauncherLaunchFailureKind.VerificationAlreadyRunning;
        }

        if (targetKind == GameLaunchTargetKind.GameClient)
        {
            if (_isGameRunning)
            {
                await ShowErrorAsync(_stringLocalizer["LaunchAborted"], _stringLocalizer["GameRunning"]);
                return LauncherLaunchFailureKind.AlreadyRunning;
            }

            _isGameRunning = true;
            OnPropertyChanged(nameof(IsLaunchInProgress));
            return null;
        }

        if (_isWorldBuilderRunning)
        {
            await ShowErrorAsync(_stringLocalizer["LaunchAborted"], _stringLocalizer["WorldBuilderRunning"]);
            return LauncherLaunchFailureKind.AlreadyRunning;
        }

        _isWorldBuilderRunning = true;
        OnPropertyChanged(nameof(IsLaunchInProgress));
        return null;
    }

    private async Task<LauncherLaunchResult> LaunchCoreAsync(
        LauncherLaunchRequest request,
        IReadOnlyList<ILaunchContentIntegrityProgressTarget> activeProgressTargets,
        Window owner,
        LauncherPaths launchPaths,
        CancellationToken cancellationToken)
    {
        bool readyToLaunch = await EnsureReadyToLaunchAsync(
            request,
            activeProgressTargets,
            owner,
            cancellationToken).ConfigureAwait(true);
        if (!readyToLaunch)
        {
            return LauncherLaunchResult.Stopped(LauncherLaunchFailureKind.VerificationCanceled);
        }

        bool gameCanBeStarted = await PrepareGameAsync(request, launchPaths, cancellationToken).ConfigureAwait(true);
        if (!gameCanBeStarted)
        {
            await ShowIncorrectInstallationMessageAsync();
            await CleanupDeploymentAsync(launchPaths, cancellationToken).ConfigureAwait(true);
            return LauncherLaunchResult.Stopped(LauncherLaunchFailureKind.PreparationFailed);
        }

        return await RunPreparedProcessAsync(request, launchPaths, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Verifies selected launch content and resolves integrity issues when the user confirms.
    /// </summary>
    private async Task<bool> EnsureReadyToLaunchAsync(
        LauncherLaunchRequest request,
        IReadOnlyList<ILaunchContentIntegrityProgressTarget> activeProgressTargets,
        Window owner,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _isLaunchVerificationRunning = true;
        try
        {
            return await _launchContentIntegrityCoordinator.EnsureReadyToLaunchAsync(
                request.ActiveVersions,
                activeProgressTargets,
                owner).ConfigureAwait(true);
        }
        finally
        {
            _isLaunchVerificationRunning = false;
        }
    }

    private async Task<bool> PrepareGameAsync(
        LauncherLaunchRequest request,
        LauncherPaths launchPaths,
        CancellationToken cancellationToken)
    {
        SetPreparingLaunch(true);
        try
        {
            return await Task.Run(
                () => _launchPreparationService.Prepare(
                    new LaunchPreparationRequest(
                        launchPaths,
                        request.ActiveVersions,
                        ShouldDisableBaseGameScriptFiles(request)),
                    cancellationToken),
                cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            SetPreparingLaunch(false);
        }
    }

    /// <summary>
    /// Launches the prepared target process and cleans deployment state after it exits.
    /// </summary>
    private async Task<LauncherLaunchResult> RunPreparedProcessAsync(
        LauncherLaunchRequest request,
        LauncherPaths launchPaths,
        CancellationToken cancellationToken)
    {
        LauncherPreferences preferences = _launcherPreferencesService.Current;
        bool hideLauncherWhileRunning = preferences.Shared.HideLauncherAfterGameStart;
        IGameProcessLaunchOperation? operation = null;
        EventHandler? currentExecutableNameChanged = null;
        SynchronizationContext? launchContext = SynchronizationContext.Current;

        if (hideLauncherWhileRunning)
        {
            SetShouldHideLauncherWindow(true);
        }

        try
        {
            operation = await _gameProcessLauncher.StartAsync(
                CreateGameLaunchRequest(request, preferences, launchPaths),
                CancellationToken.None).ConfigureAwait(true);
            _activeProcessLaunch = operation;
            currentExecutableNameChanged = (_, _) => NotifyActiveProcessNameChanged(
                operation,
                launchContext);
            operation.CurrentExecutableNameChanged += currentExecutableNameChanged;
            NotifyActiveProcessStateChanged();

            bool processSucceeded = await operation.Completion.ConfigureAwait(true);
            return LauncherLaunchResult.Attempted(processSucceeded);
        }
        finally
        {
            try
            {
                await CleanupDeploymentAsync(launchPaths, cancellationToken).ConfigureAwait(true);
            }
            finally
            {
                if (operation != null && currentExecutableNameChanged != null)
                {
                    operation.CurrentExecutableNameChanged -= currentExecutableNameChanged;
                }

                if (ReferenceEquals(_activeProcessLaunch, operation))
                {
                    _activeProcessLaunch = null;
                    NotifyActiveProcessStateChanged();
                }

                if (hideLauncherWhileRunning)
                {
                    SetShouldHideLauncherWindow(false);
                }
            }
        }
    }

    /// <summary>
    /// Notifies bindable UI state when the tracked process family changes its current executable.
    /// </summary>
    private void NotifyActiveProcessNameChanged(
        IGameProcessLaunchOperation operation,
        SynchronizationContext? launchContext)
    {
        void Update()
        {
            if (ReferenceEquals(_activeProcessLaunch, operation))
            {
                OnPropertyChanged(nameof(ActiveProcessName));
            }
        }

        if (launchContext == null || ReferenceEquals(SynchronizationContext.Current, launchContext))
        {
            Update();
            return;
        }

        launchContext.Post(_ => Update(), null);
    }

    private GameLaunchRequest CreateGameLaunchRequest(
        LauncherLaunchRequest request,
        LauncherPreferences preferences,
        LauncherPaths launchPaths)
    {
        LauncherGamePreferences gamePreferences = preferences.Games.Get(launchPaths.Game);
        if (request.TargetKind == GameLaunchTargetKind.WorldBuilder)
        {
            return GameLaunchRequest.ForWorldBuilder(
                launchPaths.GameDirectory,
                request.ExecutableName,
                gamePreferences.WorldBuilderArguments);
        }

        string gameArguments = request.UseGeneralsOnline
            ? string.Empty
            : gamePreferences.GameArguments;
        return GameLaunchRequest.ForGameClient(
            launchPaths.GameDirectory,
            request.ExecutableName,
            gameArguments);
    }

    private static bool ShouldDisableBaseGameScriptFiles(LauncherLaunchRequest request)
    {
        return request.ActiveVersions.Any(version => version.ModificationType == ModificationType.Mod);
    }

    /// <summary>
    /// Cleans deployment state after a launch attempt or preparation failure.
    /// </summary>
    private async Task CleanupDeploymentAsync(
        LauncherPaths launchPaths,
        CancellationToken cancellationToken)
    {
        bool succeeded = await Task.Run(
            () => _launchPreparationService.Cleanup(launchPaths, cancellationToken),
            cancellationToken).ConfigureAwait(true);
        if (!succeeded)
        {
            _logger.LogWarning("Launch deployment cleanup did not complete successfully.");
        }

    }

    private void EndLaunch(GameLaunchTargetKind targetKind)
    {
        if (targetKind == GameLaunchTargetKind.GameClient)
        {
            _isGameRunning = false;
        }
        else
        {
            _isWorldBuilderRunning = false;
        }

        OnPropertyChanged(nameof(IsLaunchInProgress));
    }

    private Task ShowErrorAsync(string mainMessage, string detailMessage)
    {
        return _launcherDialogService.ShowErrorAsync(
            new LauncherInfoDialogRequest(mainMessage, detailMessage));
    }

    private Task ShowIncorrectInstallationMessageAsync()
    {
        return _launcherDialogService.ShowErrorAsync(
            new LauncherInfoDialogRequest(
                _stringLocalizer["FilesCorrupted"],
                _stringLocalizer["Reinstall"],
                detailFontSize: 15D));
    }

    private void SetPreparingLaunch(bool isPreparingLaunch)
    {
        if (_isPreparingLaunch == isPreparingLaunch)
        {
            return;
        }

        _isPreparingLaunch = isPreparingLaunch;
        OnPropertyChanged(nameof(IsPreparingLaunch));
    }

    private void SetShouldHideLauncherWindow(bool shouldHideLauncherWindow)
    {
        if (_shouldHideLauncherWindow == shouldHideLauncherWindow)
        {
            return;
        }

        _shouldHideLauncherWindow = shouldHideLauncherWindow;
        OnPropertyChanged(nameof(ShouldHideLauncherWindow));
    }

    private void NotifyActiveProcessStateChanged()
    {
        OnPropertyChanged(nameof(HasActiveProcess));
        OnPropertyChanged(nameof(ActiveProcessName));
    }

}
