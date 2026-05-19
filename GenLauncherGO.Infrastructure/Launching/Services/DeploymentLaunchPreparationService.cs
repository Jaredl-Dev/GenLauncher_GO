using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Mods.Services;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Launching.Support;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Launching.Services;

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

    private readonly ILogger<DeploymentLaunchPreparationService> _logger;

    public DeploymentLaunchPreparationService(
        FileSystemDeploymentService deploymentEngine,
        ILogger<DeploymentLaunchPreparationService> logger)
    {
        _deploymentEngine = deploymentEngine ?? throw new ArgumentNullException(nameof(deploymentEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        LogDeploymentFailures("prepare launch content", result);
        return result.Succeeded;
    }

    public bool Cleanup(
        LauncherPaths paths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);

        DeploymentResult result = _deploymentEngine.Cleanup(paths, cancellationToken);
        LogDeploymentFailures("clean up launch content", result);
        return result.Succeeded;
    }

    public bool Recover(
        LauncherPaths paths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);

        DeploymentResult result = _deploymentEngine.Recover(paths, cancellationToken);
        LogDeploymentFailures("recover launch content", result);
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

    private void LogDeploymentFailures(string operationName, DeploymentResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        foreach (DeploymentFailure failure in result.Failures)
        {
            _logger.LogError(
                "Failed to {OperationName}. Kind: {FailureKind}; Path: {Path}; Message: {Message}",
                operationName,
                failure.Kind,
                failure.Path,
                failure.Message);
        }
    }
}
