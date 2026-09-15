using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenLauncherGO.Features.Updating;

internal interface IS3ObjectManifestReader
{
    Task<IReadOnlyList<RemoteFileManifestEntry>> ReadManifestAsync(
        S3ObjectManifestRequest request,
        CancellationToken cancellationToken);
}
