using System;

namespace GenLauncherGO.Core.Updating.Models;

public sealed record PackageUpdateProgress(
    long? TotalBytes,
    long BytesRead,
    double? ProgressPercentage,
    string? FileName,
    double? DownloadSpeedBytesPerSecond = null,
TimeSpan? EstimatedTimeRemaining = null);
