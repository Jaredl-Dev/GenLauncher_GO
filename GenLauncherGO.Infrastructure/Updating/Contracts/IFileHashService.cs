using System.Threading;
using System.Threading.Tasks;

namespace GenLauncherGO.Infrastructure.Updating.Contracts;

internal interface IFileHashService
{
    /// <summary>
    /// Computes an uppercase hexadecimal MD5 hash for a local file.
    /// </summary>
    Task<string> ComputeMd5HashAsync(string filePath, CancellationToken cancellationToken);
}
