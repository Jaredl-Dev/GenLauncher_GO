using System.Collections.Generic;

namespace GenLauncherGO.Features.Startup;

/// <summary>
///     Supplies untrusted Windows registry candidates in installation-source priority order.
/// </summary>
internal interface IGameInstallationRegistry
{
    IReadOnlyList<string> ReadCandidates(SupportedGame game);
}
