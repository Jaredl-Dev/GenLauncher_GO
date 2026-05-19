using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Updating.Models;

namespace GenLauncherGO.Infrastructure.Updating.Contracts;

internal interface IS3ObjectManifestReader
{
    Task<IReadOnlyList<RemoteFileManifestEntry>> ReadManifestAsync(
        S3ObjectManifestRequest request,
        CancellationToken cancellationToken);
}
