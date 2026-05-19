using System.Collections.Generic;
using GenLauncherGO.Core.Launching.Models;

namespace GenLauncherGO.Infrastructure.Launching.Contracts;

internal interface ILaunchContentIntegrityTargetBuilder
{
    IReadOnlyList<LaunchContentIntegrityTargetContext> BuildTargets(
        LaunchContentIntegrityTargetRequest request);
}
