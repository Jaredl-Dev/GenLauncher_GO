using System;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Updating.Models;

namespace GenLauncherGO.UI.Features.Integrity;

/// <summary>
///     Owns exclusive launcher package activity and the complete lifecycle of the active package download.
/// </summary>
internal sealed class LauncherPackageActivityService
{
    private readonly Lock _syncRoot = new();

    private bool _acceptDownloadProgress;

    private bool _active;

    private long _activeActivityId;
    private bool _activeDownloadCanceledByUser;

    private CancellationTokenSource? _activeDownloadCancellation;

    private TaskCompletionSource<PackageDownloadResult>? _activeDownloadCompletion;

    private object? _activeDownloadOwner;

    private PackageDownloadPauseController? _activeDownloadPauseController;

    private bool _activeDownloadSuspending;

    private TaskCompletionSource<bool>? _idleCompletion;

    private long _nextActivityId;

    public bool IsActive
    {
        get
        {
            lock (_syncRoot)
            {
                return _active;
            }
        }
    }

    /// <summary>
    ///     Gets the task for the active package download lifecycle, including cancellation cleanup and terminal
    ///     publication.
    /// </summary>
    public Task<PackageDownloadResult>? ActiveDownloadTask
    {
        get
        {
            lock (_syncRoot)
            {
                return _activeDownloadCompletion?.Task;
            }
        }
    }

    public string ActiveDisplayName
    {
        get
        {
            lock (_syncRoot)
            {
                return field;
            }
        }

        private set;
    } = string.Empty;

    public double? ProgressPercentage
    {
        get
        {
            lock (_syncRoot)
            {
                return field;
            }
        }

        private set;
    }

    /// <summary>
    ///     Gets whether the active package download is currently paused by the user.
    /// </summary>
    public bool IsDownloadPaused
    {
        get
        {
            lock (_syncRoot)
            {
                return _activeDownloadPauseController?.IsPaused == true;
            }
        }
    }

    public event EventHandler? ActivityChanged;

    /// <summary>
    ///     Gets a task that completes after the current package activity releases all lifecycle cleanup.
    /// </summary>
    public Task WaitForIdleAsync()
    {
        lock (_syncRoot)
        {
            return _active
                ? _idleCompletion?.Task ?? Task.CompletedTask
                : Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Attempts to reserve exclusive activity for package work whose cancellation is owned by its caller.
    /// </summary>
    public bool TryBegin(string displayName, out LauncherPackageActivityLease? lease)
    {
        long activityId;
        lock (_syncRoot)
        {
            if (_active)
            {
                lease = null;
                return false;
            }

            activityId = BeginActivity(displayName);
            lease = new LauncherPackageActivityLease(this, activityId);
        }

        OnActivityChanged();
        return true;
    }

    /// <summary>
    ///     Atomically starts the sole active package download with cooperative pause and resume control.
    /// </summary>
    public bool TryStartDownload(
        object owner,
        string displayName,
        Func<
            IProgress<PackageUpdateProgress>,
            PackageDownloadPauseController,
            CancellationToken,
            Task<PackageDownloadResult>> operation,
        Action started,
        Action<PackageUpdateProgress> progressChanged,
        Action canceledCleanup,
        Action<PackageDownloadResult> terminalStateChanged,
        out Task<PackageDownloadResult>? operationTask)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(started);
        ArgumentNullException.ThrowIfNull(progressChanged);
        ArgumentNullException.ThrowIfNull(canceledCleanup);
        ArgumentNullException.ThrowIfNull(terminalStateChanged);

        long activityId;
        CancellationTokenSource cancellation;
        PackageDownloadPauseController pauseController;
        TaskCompletionSource<PackageDownloadResult> completion;
        lock (_syncRoot)
        {
            if (_active)
            {
                operationTask = null;
                return false;
            }

            activityId = BeginActivity(displayName);
            cancellation = new CancellationTokenSource();
            pauseController = new PackageDownloadPauseController();
            completion = new TaskCompletionSource<PackageDownloadResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _activeDownloadOwner = owner;
            _activeDownloadCancellation = cancellation;
            _activeDownloadPauseController = pauseController;
            _activeDownloadCompletion = completion;
            _acceptDownloadProgress = true;
            operationTask = completion.Task;
        }

        _ = RunDownloadAsync(
            activityId,
            operation,
            started,
            progressChanged,
            canceledCleanup,
            terminalStateChanged,
            cancellation,
            pauseController,
            completion);
        return true;
    }

    /// <summary>
    ///     Gets the active lifecycle task when the supplied tile owns it.
    /// </summary>
    public Task<PackageDownloadResult>? GetActiveDownloadTask(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        lock (_syncRoot)
        {
            return ReferenceEquals(_activeDownloadOwner, owner)
                ? _activeDownloadCompletion?.Task
                : null;
        }
    }

    /// <summary>
    ///     Requests cancellation only when the supplied tile still owns the active package download.
    /// </summary>
    public bool RequestDownloadCancellation(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        CancellationTokenSource? cancellation;
        lock (_syncRoot)
        {
            cancellation = ReferenceEquals(_activeDownloadOwner, owner)
                ? _activeDownloadCancellation
                : null;
            if (cancellation != null)
            {
                // An explicit cancel outranks a close-time suspension in either order: the user asked for the
                // partial content to go away, so shutting down at the same moment must not preserve it.
                _activeDownloadCanceledByUser = true;
            }
        }

        return Cancel(cancellation);
    }

    /// <summary>
    ///     Toggles pause state only when the supplied tile still owns the active package download.
    /// </summary>
    public bool TryToggleDownloadPause(object owner, out bool isPaused)
    {
        ArgumentNullException.ThrowIfNull(owner);

        bool changed;
        lock (_syncRoot)
        {
            PackageDownloadPauseController? pauseController =
                ReferenceEquals(_activeDownloadOwner, owner)
                    ? _activeDownloadPauseController
                    : null;
            if (pauseController == null)
            {
                isPaused = false;
                return false;
            }

            changed = pauseController.IsPaused
                ? pauseController.Resume()
                : pauseController.Pause();
            isPaused = pauseController.IsPaused;
        }

        if (changed)
        {
            OnActivityChanged();
        }

        return true;
    }

    /// <summary>
    ///     Releases the exclusive activity a paused download is holding and completes once its lifecycle has
    ///     published a terminal state. Does nothing when no download is paused.
    /// </summary>
    /// <remarks>
    ///     A paused transfer is already stopped as far as the user is concerned, so it must not keep the launcher
    ///     from adding content, starting another download, switching game or launching. Stopping it here is the same
    ///     suspension the launcher performs at close: the partial content stays on disk and the tile still offers to
    ///     resume it.
    /// </remarks>
    public Task ReleasePausedDownloadAsync()
    {
        if (!IsDownloadPaused)
        {
            return Task.CompletedTask;
        }

        Task idle = WaitForIdleAsync();
        return RequestActiveDownloadSuspension()
            ? idle
            : Task.CompletedTask;
    }

    /// <summary>
    ///     Stops the active package download during application shutdown while keeping its partial content.
    /// </summary>
    /// <remarks>
    ///     The transport stops exactly as it does for a cancel; the difference is that the lifecycle then skips the
    ///     cleanup that discards partial content, so the next session can resume instead of downloading again.
    /// </remarks>
    public bool RequestActiveDownloadSuspension()
    {
        CancellationTokenSource? cancellation;
        lock (_syncRoot)
        {
            cancellation = _activeDownloadCancellation;
            if (cancellation != null)
            {
                _activeDownloadSuspending = true;
            }
        }

        return Cancel(cancellation);
    }

    /// <summary>
    ///     Reports aggregate progress for an active non-download package activity.
    /// </summary>
    public void ReportProgress(double? progressPercentage)
    {
        lock (_syncRoot)
        {
            if (!_active)
            {
                return;
            }

            ProgressPercentage = ClampProgress(progressPercentage);
        }

        OnActivityChanged();
    }

    private async Task RunDownloadAsync(
        long activityId,
        Func<
            IProgress<PackageUpdateProgress>,
            PackageDownloadPauseController,
            CancellationToken,
            Task<PackageDownloadResult>> operation,
        Action started,
        Action<PackageUpdateProgress> progressChanged,
        Action canceledCleanup,
        Action<PackageDownloadResult> terminalStateChanged,
        CancellationTokenSource cancellation,
        PackageDownloadPauseController pauseController,
        TaskCompletionSource<PackageDownloadResult> completion)
    {
        try
        {
            started();
            OnActivityChanged();

            IProgress<PackageUpdateProgress> progress =
                new Progress<PackageUpdateProgress>(value =>
                    PublishDownloadProgress(activityId, value, progressChanged));
            PackageDownloadResult result = await operation(
                progress,
                pauseController,
                cancellation.Token);

            StopDownloadProgress(activityId);
            // A suspended stop reports itself as canceled from the transport's point of view. Reclassifying it here
            // is what keeps the partial content on disk: the cleanup below is what discards it.
            if (result.Status == PackageDownloadStatus.Canceled && ConsumeSuspensionRequest(activityId))
            {
                result = PackageDownloadResult.Suspended();
            }

            if (result.Status == PackageDownloadStatus.Canceled)
            {
                canceledCleanup();
            }

            StopProjectingDownload(activityId);
            terminalStateChanged(result);
            End(activityId);
            completion.TrySetResult(result);
        }
        catch (Exception exception)
        {
            StopProjectingDownload(activityId);
            End(activityId);
            completion.TrySetException(exception);
        }
        finally
        {
            pauseController.Resume();
            cancellation.Dispose();
        }
    }

    private void PublishDownloadProgress(
        long activityId,
        PackageUpdateProgress progress,
        Action<PackageUpdateProgress> progressChanged)
    {
        lock (_syncRoot)
        {
            if (!_active ||
                _activeActivityId != activityId ||
                !_acceptDownloadProgress)
            {
                return;
            }

            ProgressPercentage = ClampProgress(progress.ProgressPercentage);
            progressChanged(progress);
        }

        OnActivityChanged();
    }

    /// <summary>
    ///     Reports whether shutdown asked to suspend this download, clearing the request so it applies only once.
    /// </summary>
    private bool ConsumeSuspensionRequest(long activityId)
    {
        lock (_syncRoot)
        {
            if (!_active ||
                _activeActivityId != activityId ||
                !_activeDownloadSuspending ||
                _activeDownloadCanceledByUser)
            {
                return false;
            }

            _activeDownloadSuspending = false;
            return true;
        }
    }

    private void StopDownloadProgress(long activityId)
    {
        lock (_syncRoot)
        {
            if (_active && _activeActivityId == activityId)
            {
                _acceptDownloadProgress = false;
            }
        }
    }

    private void StopProjectingDownload(long activityId)
    {
        lock (_syncRoot)
        {
            if (!_active || _activeActivityId != activityId)
            {
                return;
            }

            ClearActiveDownloadState();
        }
    }

    private long BeginActivity(string displayName)
    {
        _active = true;
        // Cleared per activity so a request that its own download outran cannot leak into the next one.
        _activeDownloadSuspending = false;
        _activeDownloadCanceledByUser = false;
        _activeActivityId = ++_nextActivityId;
        ActiveDisplayName = displayName ?? string.Empty;
        ProgressPercentage = null;
        _idleCompletion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        return _activeActivityId;
    }

    private void End(long activityId)
    {
        TaskCompletionSource<bool>? idleCompletion;
        lock (_syncRoot)
        {
            if (!_active || _activeActivityId != activityId)
            {
                return;
            }

            _active = false;
            ActiveDisplayName = string.Empty;
            ProgressPercentage = null;
            ClearActiveDownloadState();
            idleCompletion = _idleCompletion;
            _idleCompletion = null;
        }

        idleCompletion?.TrySetResult(true);
        OnActivityChanged();
    }

    /// <summary>
    ///     Clears download-specific state while the caller holds <see cref="_syncRoot" />.
    /// </summary>
    private void ClearActiveDownloadState()
    {
        _acceptDownloadProgress = false;
        _activeDownloadOwner = null;
        _activeDownloadCancellation = null;
        _activeDownloadPauseController = null;
        _activeDownloadCompletion = null;
    }

    private static bool Cancel(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
        {
            return false;
        }

        try
        {
            cancellation.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static double? ClampProgress(double? progressPercentage)
    {
        return progressPercentage.HasValue
            ? Math.Clamp(progressPercentage.Value, 0D, 100D)
            : null;
    }

    private void OnActivityChanged()
    {
        ActivityChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     Releases caller-owned package activity after its asynchronous lifecycle has finished.
    /// </summary>
    internal sealed class LauncherPackageActivityLease : IDisposable
    {
        private readonly long _activityId;
        private readonly LauncherPackageActivityService _owner;

        private int _disposed;

        internal LauncherPackageActivityLease(
            LauncherPackageActivityService owner,
            long activityId)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _activityId = activityId;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.End(_activityId);
            }
        }
    }
}
