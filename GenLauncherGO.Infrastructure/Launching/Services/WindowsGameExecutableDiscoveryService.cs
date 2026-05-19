using System;
using System.Collections.Generic;
using System.IO;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Common;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Launching.Services;

/// <summary>
///     Discovers Windows game and World Builder executables through file-system probes.
/// </summary>
internal sealed class WindowsGameExecutableDiscoveryService : IGameExecutableDiscoveryService
{
    private readonly ILogger<WindowsGameExecutableDiscoveryService> _logger;
    private readonly LauncherRuntimePathContext _runtimePathContext;

    public WindowsGameExecutableDiscoveryService(
        LauncherRuntimePathContext runtimePathContext,
        ILogger<WindowsGameExecutableDiscoveryService> logger)
    {
        _runtimePathContext = runtimePathContext ?? throw new ArgumentNullException(nameof(runtimePathContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<BuiltInExecutable> GetGameClients()
    {
        LauncherPaths paths = _runtimePathContext.ActivePaths;
        return Discover(
            LauncherFileSystemLayout.GetBuiltInGameExecutableNames(paths.Game),
            paths);
    }

    public IReadOnlyList<BuiltInExecutable> GetWorldBuilders()
    {
        LauncherPaths paths = _runtimePathContext.ActivePaths;
        return Discover(
            LauncherFileSystemLayout.GetBuiltInWorldBuilderExecutableNames(paths.Game),
            paths);
    }

    public bool IsExecutableAvailable(string? executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return false;
        }

        LauncherPaths paths = _runtimePathContext.ActivePaths;
        return IsExecutableAvailable(executableName, paths);
    }

    /// <summary>
    ///     Probes one executable against an immutable active-path snapshot.
    /// </summary>
    private bool IsExecutableAvailable(
        string executableName,
        LauncherPaths paths)
    {
        try
        {
            string normalizedName = LauncherFileSystemLayout.NormalizeExecutableFileName(executableName);
            string executablePath = Path.Combine(paths.GameDirectory, normalizedName);
            return File.Exists(executablePath) && !FileSystemPathSafety.IsReparsePoint(executablePath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                exception,
                "Could not inspect executable availability for {ExecutableName}.",
                Path.GetFileName(executableName));
            return false;
        }
    }

    private IReadOnlyList<BuiltInExecutable> Discover(
        IReadOnlyList<string> executableNames,
        LauncherPaths paths)
    {
        var executables = new List<BuiltInExecutable>(executableNames.Count);
        foreach (string executableName in executableNames)
        {
            executables.Add(new BuiltInExecutable(
                executableName,
                IsExecutableAvailable(executableName, paths)));
        }

        return executables;
    }
}
