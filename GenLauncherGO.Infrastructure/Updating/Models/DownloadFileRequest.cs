using System;
using GenLauncherGO.Core.Updating.Models;

namespace GenLauncherGO.Infrastructure.Updating.Models;

internal sealed record DownloadFileRequest(
    Uri SourceUri,
    string DestinationFilePath,
    long? ExpectedBytes = null,
    bool Resume = true,
    PackageDownloadPauseController? PauseController = null);
