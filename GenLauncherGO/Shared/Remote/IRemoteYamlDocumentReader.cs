using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenLauncherGO.Shared.Remote;

internal interface IRemoteYamlDocumentReader
{
    Task<T> ReadYamlAsync<T>(Uri documentUri, CancellationToken cancellationToken);
}
