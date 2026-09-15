namespace GenLauncherGO.Features.Updating;

internal sealed record DownloadProgress(
    long? TotalBytes,
    long BytesDownloaded,
    double? ProgressPercentage);
