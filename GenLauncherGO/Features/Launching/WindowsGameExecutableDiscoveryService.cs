using System;
using System.Collections.Generic;
using System.IO;
using GenLauncherGO.Features.Startup;
using GenLauncherGO.Shared.IO;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Features.Launching;

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

    public bool IsExecutableAvailable(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        LauncherPaths paths = _runtimePathContext.ActivePaths;
        return IsExecutableAvailable(executablePath, paths);
    }

    /// <summary>
    ///     Probes one executable against an immutable active-path snapshot.
    /// </summary>
    private bool IsExecutableAvailable(
        string executablePath,
        LauncherPaths paths)
    {
        try
        {
            string fullPath = LauncherFileSystemLayout.ResolveExecutablePath(paths.GameDirectory, executablePath);
            return File.Exists(fullPath) &&
                   !FileSystemPathSafety.ExistingPathChainContainsReparsePoint(fullPath, "Executable paths");
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                exception,
                "Could not inspect executable availability for {ExecutableName}.",
                Path.GetFileName(executablePath));
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
