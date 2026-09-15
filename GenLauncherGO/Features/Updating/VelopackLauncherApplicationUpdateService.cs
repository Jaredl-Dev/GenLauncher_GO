using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Features.Startup;
using GenLauncherGO.Shared.Persistence;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace GenLauncherGO.Features.Updating;

/// <summary>
///     Owns portable-only Velopack update checks, daily throttling, downloads, and updater handoff.
/// </summary>
internal sealed class VelopackLauncherApplicationUpdateService : ILauncherApplicationUpdateService
{
    private static readonly TimeSpan _automaticCheckInterval = TimeSpan.FromHours(24);

    private readonly IAtomicFileWriter _atomicFileWriter;
    private readonly Func<CancellationToken, Task<string?>> _checkAndDownloadUpdateAsync;
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private readonly Func<string?> _getPendingUpdateVersion;

    private readonly ILogger<VelopackLauncherApplicationUpdateService> _logger;
    private readonly LauncherStoragePaths _storagePaths;
    private readonly TimeProvider _timeProvider;
    private readonly bool _updatesEnabled;
    private readonly Func<bool> _tryHandoffToUpdateAndRestart;

    public VelopackLauncherApplicationUpdateService(
        LauncherStoragePaths storagePaths,
        IAtomicFileWriter atomicFileWriter,
        TimeProvider timeProvider,
        string repositoryUrl,
        ILogger<VelopackLauncherApplicationUpdateService> logger)
    {
        ArgumentNullException.ThrowIfNull(storagePaths);
        ArgumentNullException.ThrowIfNull(atomicFileWriter);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryUrl);
        ArgumentNullException.ThrowIfNull(logger);

        _storagePaths = storagePaths;
        _atomicFileWriter = atomicFileWriter;
        _timeProvider = timeProvider;
        _logger = logger;

        UpdateInfo? downloadedUpdate = null;
        UpdateManager updateManager = new(new GithubSource(repositoryUrl, null, false));
        _updatesEnabled = updateManager.IsInstalled && updateManager.IsPortable;
        _getPendingUpdateVersion = () => updateManager.UpdatePendingRestart?.Version.ToString();
        _checkAndDownloadUpdateAsync = async cancellationToken =>
        {
            UpdateInfo? update = await updateManager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update == null)
            {
                return null;
            }

            await updateManager.DownloadUpdatesAsync(update, cancelToken: cancellationToken).ConfigureAwait(false);
            downloadedUpdate = update;
            return update.TargetFullRelease.Version.ToString();
        };
        _tryHandoffToUpdateAndRestart = () =>
        {
            VelopackAsset? target = downloadedUpdate?.TargetFullRelease ?? updateManager.UpdatePendingRestart;
            if (target == null)
            {
                return false;
            }

            updateManager.WaitExitThenApplyUpdates(target, silent: true, restart: true);
            return true;
        };
    }

    internal VelopackLauncherApplicationUpdateService(
        LauncherStoragePaths storagePaths,
        IAtomicFileWriter atomicFileWriter,
        TimeProvider timeProvider,
        bool updatesEnabled,
        Func<CancellationToken, Task<string?>> checkAndDownloadUpdateAsync,
        Func<bool> tryHandoffToUpdateAndRestart,
        ILogger<VelopackLauncherApplicationUpdateService> logger,
        Func<string?>? getPendingUpdateVersion = null)
    {
        ArgumentNullException.ThrowIfNull(storagePaths);
        ArgumentNullException.ThrowIfNull(atomicFileWriter);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _storagePaths = storagePaths;
        _atomicFileWriter = atomicFileWriter;
        _timeProvider = timeProvider;
        _logger = logger;
        _updatesEnabled = updatesEnabled;
        _checkAndDownloadUpdateAsync = checkAndDownloadUpdateAsync ??
                                       throw new ArgumentNullException(nameof(checkAndDownloadUpdateAsync));
        _tryHandoffToUpdateAndRestart = tryHandoffToUpdateAndRestart ??
                                        throw new ArgumentNullException(nameof(tryHandoffToUpdateAndRestart));
        _getPendingUpdateVersion = getPendingUpdateVersion ?? (static () => null);
    }

    public async Task<string?> CheckAndDownloadUpdateAsync(CancellationToken cancellationToken)
    {
        if (!_updatesEnabled)
        {
            return null;
        }

        await _checkGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? pendingVersion = TryGetPendingUpdateVersion();
            if (pendingVersion != null)
            {
                return pendingVersion;
            }

            if (!IsAutomaticCheckDue())
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            bool attemptCompleted = false;
            try
            {
                string? version = await _checkAndDownloadUpdateAsync(cancellationToken).ConfigureAwait(false);
                attemptCompleted = true;
                return version;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                attemptCompleted = true;
                _logger.LogWarning(
                    exception,
                    "The automatic launcher application-update check or download failed.");
                return null;
            }
            finally
            {
                if (attemptCompleted)
                {
                    RecordCompletedAttempt();
                }
            }
        }
        finally
        {
            _checkGate.Release();
        }
    }

    private string? TryGetPendingUpdateVersion()
    {
        try
        {
            return _getPendingUpdateVersion();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "The downloaded application-update state could not be read.");
            return null;
        }
    }

    public bool TryHandoffToUpdateAndRestart()
    {
        if (!_updatesEnabled)
        {
            return false;
        }

        try
        {
            return _tryHandoffToUpdateAndRestart();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not start the Velopack application-update handoff.");
            return false;
        }
    }

    private bool IsAutomaticCheckDue()
    {
        string timestampPath = _storagePaths.ApplicationUpdateCheckTimestampFilePath;
        try
        {
            if (!File.Exists(timestampPath))
            {
                return true;
            }

            string timestampText = File.ReadAllText(timestampPath).Trim();
            if (!DateTimeOffset.TryParseExact(
                    timestampText,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTimeOffset lastCheckUtc))
            {
                return true;
            }

            DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
            lastCheckUtc = lastCheckUtc.ToUniversalTime();
            return lastCheckUtc > nowUtc || nowUtc - lastCheckUtc >= _automaticCheckInterval;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "The application-update check timestamp could not be read; an update check will run now.");
            return true;
        }
    }

    private void RecordCompletedAttempt()
    {
        try
        {
            string timestamp = _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);
            _atomicFileWriter.WriteText(
                _storagePaths.ApplicationUpdateCheckTimestampFilePath,
                timestamp);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "The completed application-update check timestamp could not be persisted.");
        }
    }
}
