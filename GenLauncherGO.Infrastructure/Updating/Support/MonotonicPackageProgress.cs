using System;
using GenLauncherGO.Core.Updating.Models;

namespace GenLauncherGO.Infrastructure.Updating.Support;

/// <summary>
/// Normalizes concurrent provider reports so package progress never moves backwards.
/// </summary>
internal sealed class MonotonicPackageProgress : IProgress<PackageUpdateProgress>
{
    private readonly IProgress<PackageUpdateProgress> _inner;
    private readonly object _syncRoot = new();

    private long _bytesRead;
    private double? _percentage;
    private long? _totalBytes;

    public MonotonicPackageProgress(IProgress<PackageUpdateProgress> inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public void Report(PackageUpdateProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);

        lock (_syncRoot)
        {
            _bytesRead = Math.Max(_bytesRead, Math.Max(0, value.BytesRead));
            if (value.TotalBytes.HasValue)
            {
                _totalBytes = Math.Max(_totalBytes ?? 0, Math.Max(0, value.TotalBytes.Value));
            }

            if (_totalBytes.HasValue && _bytesRead > _totalBytes.Value)
            {
                _totalBytes = _bytesRead;
            }

            double? percentage = value.ProgressPercentage;
            if (!percentage.HasValue && _totalBytes is > 0)
            {
                percentage = (double)_bytesRead / _totalBytes.Value * 100D;
            }

            if (percentage.HasValue)
            {
                _percentage = Math.Max(
                    _percentage ?? 0D,
                    Math.Clamp(percentage.Value, 0D, 100D));
            }

            PackageUpdateProgress normalized = value with
            {
                TotalBytes = _totalBytes,
                BytesRead = _bytesRead,
                ProgressPercentage = _percentage,
            };

            // Keep normalization and delivery ordered for concurrent S3 reporters. The caller still owns dispatch
            // semantics; the inner reporter posts these ordered values to the UI context.
            _inner.Report(normalized);
        }
    }
}
