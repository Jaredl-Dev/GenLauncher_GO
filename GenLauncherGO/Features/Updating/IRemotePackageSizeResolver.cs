using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Features.Mods;

namespace GenLauncherGO.Features.Updating;

/// <summary>
///     Resolves the total fresh-install payload size advertised by a remote launcher package source.
/// </summary>
/// <remarks>
///     Implementations inspect remote metadata only and must not start or stage a package download.
/// </remarks>
internal interface IRemotePackageSizeResolver
{
    /// <summary>
    ///     Resolves the total remote payload size, or returns <see langword="null" /> when the source cannot provide it.
    /// </summary>
    Task<long?> GetTotalBytesAsync(
        LauncherContentVersion version,
        CancellationToken cancellationToken);
}
