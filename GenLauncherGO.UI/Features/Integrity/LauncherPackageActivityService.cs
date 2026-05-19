using System;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Updating.Models;

namespace GenLauncherGO.UI.Features.Integrity;

/// <summary>
/// Owns exclusive launcher package activity and the complete lifecycle of the active package download.
/// </summary>
internal sealed class LauncherPackageActivityService
{
    private readonly object _syncRoot = new();

    private long _nextActivityId;

    private long _activeActivityId;

    private string _activeDisplayName = string.Empty;

    private double? _progressPercentage;

    private bool _active;

    private bool _acceptDownloadProgress;

    private object? _activeDownloadOwner;

    private TaskCompletionSource<PackageDownloadResult>? _activeDownloadCompletion;

    private CancellationTokenSource? _activeDownloadCancellation;

    private PackageDownloadPauseController? _activeDownloadPauseController;

    private TaskCompletionSource<bool>? _idleCompletion;

    public event EventHandler? ActivityChanged;

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
    /// Gets the task for the active package download lifecycle, including cancellation cleanup and terminal
    /// publication.
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

    /// <summary>
    /// Gets a task that completes after the current package activity releases all lifecycle cleanup.
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

    public string ActiveDisplayName
    {
        get
        {
            lock (_syncRoot)
            {
                return _activeDisplayName;
            }
        }
    }

    public double? ProgressPercentage
    {
        get
        {
            lock (_syncRoot)
            {
                return _progressPercentage;
            }
        }
    }

    /// <summary>
    /// Attempts to reserve exclusive activity for package work whose cancellation is owned by its caller.
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
    /// Atomically starts the sole active package download with cooperative pause and resume control.
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
    /// Gets the active lifecycle task when the supplied tile owns it.
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
    /// Requests cancellation only when the supplied tile still owns the active package download.
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
        }

        return Cancel(cancellation);
    }

    /// <summary>
    /// Toggles pause state only when the supplied tile still owns the active package download.
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
    /// Requests cancellation of the active package download during application shutdown.
    /// </summary>
    public bool RequestActiveDownloadCancellation()
    {
        CancellationTokenSource? cancellation;
        lock (_syncRoot)
        {
            cancellation = _activeDownloadCancellation;
        }

        return Cancel(cancellation);
    }

    /// <summary>
    /// Reports aggregate progress for an active non-download package activity.
    /// </summary>
    public void ReportProgress(double? progressPercentage)
    {
        lock (_syncRoot)
        {
            if (!_active)
            {
                return;
            }

            _progressPercentage = ClampProgress(progressPercentage);
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

            IProgress<PackageUpdateProgress> progress = new Progress<PackageUpdateProgress>(
                value => PublishDownloadProgress(activityId, value, progressChanged));
            PackageDownloadResult result = await operation(
                progress,
                pauseController,
                cancellation.Token);

            StopDownloadProgress(activityId);
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

            _progressPercentage = ClampProgress(progress.ProgressPercentage);
            progressChanged(progress);
        }

        OnActivityChanged();
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

            _acceptDownloadProgress = false;
            _activeDownloadOwner = null;
            _activeDownloadCancellation = null;
            _activeDownloadPauseController = null;
            _activeDownloadCompletion = null;
        }
    }

    private long BeginActivity(string displayName)
    {
        _active = true;
        _activeActivityId = ++_nextActivityId;
        _activeDisplayName = displayName ?? string.Empty;
        _progressPercentage = null;
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
            _activeDisplayName = string.Empty;
            _progressPercentage = null;
            _acceptDownloadProgress = false;
            _activeDownloadOwner = null;
            _activeDownloadCancellation = null;
            _activeDownloadPauseController = null;
            _activeDownloadCompletion = null;
            idleCompletion = _idleCompletion;
            _idleCompletion = null;
        }

        idleCompletion?.TrySetResult(true);
        OnActivityChanged();
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
    /// Releases caller-owned package activity after its asynchronous lifecycle has finished.
    /// </summary>
    internal sealed class LauncherPackageActivityLease : IDisposable
    {
        private readonly LauncherPackageActivityService _owner;

        private readonly long _activityId;

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
