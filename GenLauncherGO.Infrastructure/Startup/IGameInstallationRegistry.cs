using System.Collections.Generic;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Infrastructure.Startup;

/// <summary>
///     Supplies untrusted Windows registry candidates in installation-source priority order.
/// </summary>
internal interface IGameInstallationRegistry
{
    IReadOnlyList<string> ReadCandidates(SupportedGame game);
}
