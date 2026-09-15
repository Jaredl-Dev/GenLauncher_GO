using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenLauncherGO.Features.Updating;

internal interface IDownloadFileMetadataReader
{
    Task<DownloadFileMetadata> ReadMetadataAsync(
        Uri downloadUri,
        CancellationToken cancellationToken);
}
