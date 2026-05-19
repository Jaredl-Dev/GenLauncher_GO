using System;
using System.Collections.Generic;
using System.Threading;
using GenLauncherGO.Core.Updating.Models;

namespace GenLauncherGO.Infrastructure.Updating.Support;

internal sealed class PackageProgressTracker
{
    private static readonly TimeSpan _reportInterval = TimeSpan.FromMilliseconds(100);

    private readonly Dictionary<string, long> _itemProgressBytes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _progressGate = new();

    private readonly long _resumedBytes;
    private readonly long _startTimestamp;
    private readonly TimeProvider _timeProvider;

    private TimeSpan _lastReportElapsed;
    private long _lastReportedBytesRead;
    private double? _lastReportedPercentage;
    private long? _totalBytes;
    private long _totalBytesRead;

    /// <summary>
    ///     Creates a tracker for one package transfer.
    /// </summary>
    /// <param name="totalBytes">The package's full size, including whatever is already on disk.</param>
    /// <param name="resumedBytes">
    ///     Bytes already present when this transfer started. They count towards progress, so a resumed download picks
    ///     up where it stopped instead of restarting at zero, but never towards the transfer rate: nothing moved them
    ///     during this session, and crediting them would report a speed and an estimate no connection is achieving.
    /// </param>
    /// <param name="timeProvider">The clock used for rate and estimate calculations.</param>
    public PackageProgressTracker(long? totalBytes, long resumedBytes = 0, TimeProvider? timeProvider = null)
    {
        _totalBytes = totalBytes;
        _resumedBytes = Math.Max(0, resumedBytes);
        _totalBytesRead = _resumedBytes;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _startTimestamp = _timeProvider.GetTimestamp();
    }

    public void AddExpectedBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return;
        }

        lock (_progressGate)
        {
            if (_totalBytes.HasValue)
            {
                _totalBytes += bytes;
            }
        }
    }

    public void CompleteItemSilently(
        string itemName,
        long bytesRead)
    {
        lock (_progressGate)
        {
            UpdateItemProgress(itemName, bytesRead);
        }
    }

    public PackageUpdateProgress? Update(
        string itemName,
        long bytesRead,
        bool forceReport = false)
    {
        lock (_progressGate)
        {
            UpdateItemProgress(itemName, bytesRead);

            TimeSpan elapsed = _timeProvider.GetElapsedTime(_startTimestamp);
            long? totalBytes = _totalBytes;
            bool completed = totalBytes.HasValue && _totalBytesRead >= totalBytes.Value;
            if (!forceReport &&
                !completed &&
                elapsed - _lastReportElapsed < _reportInterval &&
                _lastReportedBytesRead != 0)
            {
                return null;
            }

            _lastReportElapsed = elapsed;
            _lastReportedBytesRead = _totalBytesRead;

            double? progressPercentage = null;
            if (totalBytes is > 0)
            {
                progressPercentage = Math.Clamp(
                    Math.Round((double)_totalBytesRead / totalBytes.Value * 100, 2),
                    0D,
                    100D);
                progressPercentage = Math.Max(_lastReportedPercentage ?? 0D, progressPercentage.Value);
                _lastReportedPercentage = progressPercentage;
            }

            double? speedBytesPerSecond = null;
            TimeSpan? estimatedTimeRemaining = null;
            long transferredThisSession = _totalBytesRead - _resumedBytes;
            if (elapsed.TotalSeconds > 0.25 && transferredThisSession > 0)
            {
                speedBytesPerSecond = transferredThisSession / elapsed.TotalSeconds;
                if (totalBytes.HasValue && speedBytesPerSecond > 0)
                {
                    long remainingBytes = Math.Max(0, totalBytes.Value - _totalBytesRead);
                    estimatedTimeRemaining = TimeSpan.FromSeconds(remainingBytes / speedBytesPerSecond.Value);
                }
            }

            return new PackageUpdateProgress(
                totalBytes,
                _totalBytesRead,
                progressPercentage,
                null,
                speedBytesPerSecond,
                estimatedTimeRemaining);
        }
    }

    private void UpdateItemProgress(string itemName, long bytesRead)
    {
        long previousBytesRead = _itemProgressBytes.GetValueOrDefault(itemName);
        long normalizedBytesRead = Math.Max(previousBytesRead, Math.Max(0, bytesRead));
        _itemProgressBytes[itemName] = normalizedBytesRead;
        _totalBytesRead += normalizedBytesRead - previousBytesRead;
    }
}
