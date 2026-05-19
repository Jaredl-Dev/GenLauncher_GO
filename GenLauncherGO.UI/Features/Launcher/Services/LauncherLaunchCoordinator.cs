using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using GenLauncherGO.Core.Launching;
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
///     Coordinates launch verification, deployment preparation, process launch, and cleanup for the launcher UI.
/// </summary>
internal sealed class LauncherLaunchCoordinator : ObservableObject
{
    private readonly IGameProcessLauncher _gameProcessLauncher;

    private readonly LaunchContentIntegrityCoordinator _launchContentIntegrityCoordinator;

    private readonly ILaunchPreparationService _launchPreparationService;

    private readonly ILauncherDialogService _launcherDialogService;

    private readonly ILauncherPreferencesService _launcherPreferencesService;

    private readonly ILogger<LauncherLaunchCoordinator> _logger;

    private readonly LauncherPackageActivityService _packageActivityService;

    private readonly LauncherRuntimePathContext _runtimePaths;

    private readonly ILauncherStringLocalizer _stringLocalizer;

    private IGameProcessLaunchOperation? _activeProcessLaunch;

    private bool _isGameRunning;

    private bool _isLaunchVerificationRunning;

    private bool _isWorldBuilderRunning;

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
        _packageActivityService =
            packageActivityService ?? throw new ArgumentNullException(nameof(packageActivityService));
        _runtimePaths = runtimePaths ?? throw new ArgumentNullException(nameof(runtimePaths));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _launcherDialogService =
            launcherDialogService ?? throw new ArgumentNullException(nameof(launcherDialogService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsPreparingLaunch { get; private set; }

    public bool HasActiveProcess => _activeProcessLaunch != null;

    public bool IsLaunchInProgress => _isGameRunning || _isWorldBuilderRunning;

    public string? ActiveProcessName => _activeProcessLaunch?.CurrentExecutableName;

    public bool ShouldHideLauncherWindow { get; private set; }

    /// <summary>
    ///     Force closes the currently tracked launched process family.
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
    ///     Verifies content, prepares deployment, tracks the launched process family, and cleans up afterward.
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
                cancellationToken);
        }
        finally
        {
            EndLaunch(request.TargetKind);
        }
    }

    /// <summary>
    ///     Marks the requested launch target as running when no conflicting launch workflow is active.
    /// </summary>
    private async Task<LauncherLaunchFailureKind?> TryBeginLaunchAsync(GameLaunchTargetKind targetKind)
    {
        if (_isLaunchVerificationRunning)
        {
            await ShowErrorAsync(_stringLocalizer["LaunchAborted"], _stringLocalizer["LaunchVerificationRunning"]);
            return LauncherLaunchFailureKind.VerificationAlreadyRunning;
        }

        await _packageActivityService.ReleasePausedDownloadAsync();

        if (_packageActivityService.IsActive)
        {
            await ShowErrorAsync(
                _stringLocalizer["LaunchAborted"],
                string.Format(
                    CultureInfo.CurrentCulture,
                    _stringLocalizer["InstallInProgress"],
                    _packageActivityService.ActiveDisplayName));
            return LauncherLaunchFailureKind.PackageActivityInProgress;
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
            cancellationToken);
        if (!readyToLaunch)
        {
            return LauncherLaunchResult.Stopped(LauncherLaunchFailureKind.VerificationCanceled);
        }

        bool gameCanBeStarted = await PrepareGameAsync(request, launchPaths, cancellationToken);
        if (!gameCanBeStarted)
        {
            await ShowIncorrectInstallationMessageAsync();
            await CleanupDeploymentAsync(launchPaths, cancellationToken);
            return LauncherLaunchResult.Stopped(LauncherLaunchFailureKind.PreparationFailed);
        }

        return await RunPreparedProcessAsync(request, launchPaths, cancellationToken);
    }

    /// <summary>
    ///     Verifies selected launch content and resolves integrity issues when the user confirms.
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
                owner);
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
                cancellationToken);
        }
        finally
        {
            SetPreparingLaunch(false);
        }
    }

    /// <summary>
    ///     Launches the prepared target process and cleans deployment state after it exits.
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
                CancellationToken.None);
            _activeProcessLaunch = operation;
            currentExecutableNameChanged = (_, _) => NotifyActiveProcessNameChanged(
                operation,
                launchContext);
            operation.CurrentExecutableNameChanged += currentExecutableNameChanged;
            NotifyActiveProcessStateChanged();

            bool processSucceeded = await operation.Completion;
            return LauncherLaunchResult.Attempted(processSucceeded);
        }
        finally
        {
            try
            {
                await CleanupDeploymentAsync(launchPaths, cancellationToken);
            }
            finally
            {
                operation?.CurrentExecutableNameChanged -= currentExecutableNameChanged;

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
    ///     Notifies bindable UI state when the tracked process family changes its current executable.
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

    private static GameLaunchRequest CreateGameLaunchRequest(
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

        string gameArguments = gamePreferences.GameArguments;
        if (request.UseGeneralsOnline)
        {
            gameArguments = CreateGeneralsOnlineArguments(request, gameArguments);
        }

        return GameLaunchRequest.ForGameClient(
            launchPaths.GameDirectory,
            request.ExecutableName,
            gameArguments);
    }

    private static string CreateGeneralsOnlineArguments(
        LauncherLaunchRequest request,
        string gameArguments)
    {
        string arguments = gameArguments;
        if (!LauncherGameArgumentService.ContainsArgument(
                arguments,
                LauncherGameArgumentService.WindowedArgument))
        {
            arguments = LauncherGameArgumentService.SetArgumentEnabled(
                arguments,
                LauncherGameArgumentService.GeneralsOnlineFullscreenArgument,
                true);
        }

        if (ShouldDisableBaseGameScriptFiles(request))
        {
            arguments = LauncherGameArgumentService.SetArgumentEnabled(
                arguments,
                LauncherGameArgumentService.GeneralsOnlineDisableCommunityDataPatchArgument,
                true);
        }

        return arguments;
    }

    private static bool ShouldDisableBaseGameScriptFiles(LauncherLaunchRequest request)
    {
        return request.ActiveVersions.Any(version => version.ModificationType == ModificationType.Mod);
    }

    /// <summary>
    ///     Cleans deployment state after a launch attempt or preparation failure.
    /// </summary>
    private async Task CleanupDeploymentAsync(
        LauncherPaths launchPaths,
        CancellationToken cancellationToken)
    {
        bool succeeded = await Task.Run(
            () => _launchPreparationService.Cleanup(launchPaths, cancellationToken),
            cancellationToken);
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
                15D));
    }

    private void SetPreparingLaunch(bool isPreparingLaunch)
    {
        if (IsPreparingLaunch == isPreparingLaunch)
        {
            return;
        }

        IsPreparingLaunch = isPreparingLaunch;
        OnPropertyChanged(nameof(IsPreparingLaunch));
    }

    private void SetShouldHideLauncherWindow(bool shouldHideLauncherWindow)
    {
        if (ShouldHideLauncherWindow == shouldHideLauncherWindow)
        {
            return;
        }

        ShouldHideLauncherWindow = shouldHideLauncherWindow;
        OnPropertyChanged(nameof(ShouldHideLauncherWindow));
    }

    private void NotifyActiveProcessStateChanged()
    {
        OnPropertyChanged(nameof(HasActiveProcess));
        OnPropertyChanged(nameof(ActiveProcessName));
    }
}
