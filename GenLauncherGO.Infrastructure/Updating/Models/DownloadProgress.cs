namespace GenLauncherGO.Infrastructure.Updating.Models;

internal sealed record DownloadProgress(
    long? TotalBytes,
    long BytesDownloaded,
    double? ProgressPercentage);
