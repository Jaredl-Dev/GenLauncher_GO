using System;

namespace GenLauncherGO.Features.Updating;

internal sealed record DownloadFileMetadata(
    Uri DownloadUri,
    string FileName,
    long? TotalBytes);
