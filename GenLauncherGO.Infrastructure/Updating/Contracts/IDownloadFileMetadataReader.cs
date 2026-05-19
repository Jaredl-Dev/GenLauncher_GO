using System;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Updating.Models;

namespace GenLauncherGO.Infrastructure.Updating.Contracts;

internal interface IDownloadFileMetadataReader
{
    Task<DownloadFileMetadata> ReadMetadataAsync(
        Uri downloadUri,
        CancellationToken cancellationToken);
}
