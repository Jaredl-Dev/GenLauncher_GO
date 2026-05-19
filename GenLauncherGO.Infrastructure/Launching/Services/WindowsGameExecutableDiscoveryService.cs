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
/// Discovers Windows game and World Builder executables through file-system probes.
/// </summary>
internal sealed class WindowsGameExecutableDiscoveryService : IGameExecutableDiscoveryService
{
    private readonly LauncherRuntimePathContext _runtimePathContext;

    private readonly ILogger<WindowsGameExecutableDiscoveryService> _logger;

    public WindowsGameExecutableDiscoveryService(
        LauncherRuntimePathContext runtimePathContext,
        ILogger<WindowsGameExecutableDiscoveryService> logger)
    {
        _runtimePathContext = runtimePathContext ?? throw new ArgumentNullException(nameof(runtimePathContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<GameClientExecutable> GetGameClients()
    {
        LauncherPaths paths = _runtimePathContext.ActivePaths;
        var executables = new List<GameClientExecutable>();
        string communityExecutable = LauncherFileSystemLayout.GetCommunityGameExecutableName(paths.Game);
        executables.Add(new GameClientExecutable(
            communityExecutable,
            GameClientExecutableKind.Community,
            IsExecutableAvailable(communityExecutable, paths)));

        if (paths.Game == SupportedGame.ZeroHour)
        {
            executables.Add(new GameClientExecutable(
                LauncherFileSystemLayout.GeneralsOnlineExecutableFileName,
                GameClientExecutableKind.GeneralsOnline,
                IsExecutableAvailable(LauncherFileSystemLayout.GeneralsOnlineExecutableFileName, paths)));
        }

        return executables;
    }

    public IReadOnlyList<WorldBuilderExecutable> GetWorldBuilders()
    {
        LauncherPaths paths = _runtimePathContext.ActivePaths;
        var executables = new List<WorldBuilderExecutable>();

        executables.Add(new WorldBuilderExecutable(
            LauncherFileSystemLayout.VanillaWorldBuilderExecutableFileName,
            WorldBuilderExecutableKind.Vanilla,
            IsExecutableAvailable(LauncherFileSystemLayout.VanillaWorldBuilderExecutableFileName, paths)));

        string communityExecutable = LauncherFileSystemLayout.GetCommunityWorldBuilderExecutableName(paths.Game);
        executables.Add(new WorldBuilderExecutable(
            communityExecutable,
            WorldBuilderExecutableKind.Community,
            IsExecutableAvailable(communityExecutable, paths)));

        return executables;
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
    /// Probes one executable against an immutable active-path snapshot.
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
}
