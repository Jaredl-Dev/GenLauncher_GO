using System.Threading;
using System.Threading.Tasks;

namespace GenLauncherGO.Core.Updating.Models;

/// <summary>
/// Provides cooperative asynchronous pause and resume control for one package download.
/// </summary>
public sealed class PackageDownloadPauseController
{
    private readonly object _syncRoot = new();

    private TaskCompletionSource? _resumeCompletion;

    public bool IsPaused
    {
        get
        {
            lock (_syncRoot)
            {
                return _resumeCompletion != null;
            }
        }
    }

    /// <summary>
    /// Pauses cooperative download work at its next checkpoint and reports whether the state changed.
    /// </summary>
    public bool Pause()
    {
        lock (_syncRoot)
        {
            if (_resumeCompletion != null)
            {
                return false;
            }

            _resumeCompletion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return true;
        }
    }

    /// <summary>
    /// Resumes paused download work and reports whether the state changed.
    /// </summary>
    public bool Resume()
    {
        TaskCompletionSource? resumeCompletion;
        lock (_syncRoot)
        {
            resumeCompletion = _resumeCompletion;
            _resumeCompletion = null;
        }

        return resumeCompletion?.TrySetResult() == true;
    }

    /// <summary>
    /// Asynchronously waits until download work is resumed.
    /// </summary>
    public async ValueTask WaitWhilePausedAsync(CancellationToken cancellationToken)
    {
        Task? resumeTask;
        lock (_syncRoot)
        {
            resumeTask = _resumeCompletion?.Task;
        }

        if (resumeTask != null)
        {
            await resumeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
