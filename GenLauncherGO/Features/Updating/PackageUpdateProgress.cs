using System;

namespace GenLauncherGO.Features.Updating;

internal sealed record PackageUpdateProgress(
    long? TotalBytes,
    long BytesRead,
    double? ProgressPercentage,
    string? FileName,
    double? DownloadSpeedBytesPerSecond = null,
    TimeSpan? EstimatedTimeRemaining = null);
