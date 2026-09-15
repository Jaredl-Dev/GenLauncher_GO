using System;

namespace GenLauncherGO.Features.Updating;

internal sealed record DownloadFileRequest(
    Uri SourceUri,
    string DestinationFilePath,
    long? ExpectedBytes = null,
    bool Resume = true,
    PackageDownloadPauseController? PauseController = null);
