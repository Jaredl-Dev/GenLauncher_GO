using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GenLauncherGO.Features.Mods;
using GenLauncherGO.Features.Startup;

namespace GenLauncherGO.Features.Launching;

/// <summary>
///     Orchestrates launch preparation by translating selected content into deployment packages.
/// </summary>
internal sealed class DeploymentLaunchPreparationService : ILaunchPreparationService
{
    /// <summary>
    ///     The base game script files that must be hidden while a modded game launch is deployed.
    /// </summary>
    private static readonly IReadOnlyList<string> _baseGameScriptRelativePaths =
    [
        "Data/Scripts/MultiplayerScripts.scb",
        "Data/Scripts/SkirmishScripts.scb",
        "Data/Scripts/Scripts.ini"
    ];

    private readonly FileSystemDeploymentService _deploymentEngine;

    public DeploymentLaunchPreparationService(FileSystemDeploymentService deploymentEngine)
    {
        _deploymentEngine = deploymentEngine ?? throw new ArgumentNullException(nameof(deploymentEngine));
    }

    public bool Prepare(
        LaunchPreparationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<DeploymentPackage> packages = CreateDeploymentPackages(request);
        IReadOnlyList<string> disabledTargetRelativePaths = request.DisableBaseGameScriptFiles
            ? _baseGameScriptRelativePaths
            : Array.Empty<string>();
        DeploymentResult result = _deploymentEngine.Prepare(
            request.Paths,
            packages,
            disabledTargetRelativePaths,
            cancellationToken);
        return result.Succeeded;
    }

    public bool Cleanup(
        LauncherPaths paths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);

        DeploymentResult result = _deploymentEngine.Cleanup(paths, cancellationToken);
        return result.Succeeded;
    }

    public bool Recover(
        LauncherPaths paths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);

        DeploymentResult result = _deploymentEngine.Recover(paths, cancellationToken);
        return result.Succeeded;
    }

    private static IReadOnlyList<DeploymentPackage> CreateDeploymentPackages(LaunchPreparationRequest request)
    {
        return request.Versions
            .Select((version, index) => CreateDeploymentPackage(request, version, index))
            .ToList();
    }

    private static DeploymentPackage CreateDeploymentPackage(
        LaunchPreparationRequest request,
        LauncherContentVersion version,
        int index)
    {
        ArgumentNullException.ThrowIfNull(version);

        string packageRoot = LauncherContentPathResolver.ResolveVersionPath(
                                 request.Paths,
                                 version.ContentKey)?.FullPath
                             ?? string.Empty;
        return new DeploymentPackage(packageRoot, index);
    }

}
