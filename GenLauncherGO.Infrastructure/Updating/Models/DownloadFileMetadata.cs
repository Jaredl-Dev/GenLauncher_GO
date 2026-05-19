using System;

namespace GenLauncherGO.Infrastructure.Updating.Models;

internal sealed record DownloadFileMetadata(
    Uri DownloadUri,
    string FileName,
    long? TotalBytes);
