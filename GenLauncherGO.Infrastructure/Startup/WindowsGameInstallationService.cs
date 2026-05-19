using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Core.Startup.Models;
using GenLauncherGO.Infrastructure.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Infrastructure.Startup;

/// <summary>
///     Validates selected game directories and discovers candidates from the Windows registry.
/// </summary>
public sealed class WindowsGameInstallationService : IGameInstallationService
{
    // Zero Hour wins when one physical directory satisfies both games, so discovery never assigns the same
    // installation to both titles and containing-installation detection stays deterministic.
    private static readonly SupportedGame[] _gamesInDetectionPriorityOrder =
    [
        SupportedGame.ZeroHour,
        SupportedGame.Generals
    ];

    private readonly ILogger<WindowsGameInstallationService> _logger;

    private readonly IGameInstallationRegistry _registry;

    public WindowsGameInstallationService()
        : this(
            new WindowsGameInstallationRegistry(),
            NullLogger<WindowsGameInstallationService>.Instance)
    {
    }

    public WindowsGameInstallationService(ILogger<WindowsGameInstallationService> logger)
        : this(new WindowsGameInstallationRegistry(), logger)
    {
    }

    internal WindowsGameInstallationService(
        IGameInstallationRegistry registry,
        ILogger<WindowsGameInstallationService> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public GameInstallationLocation? FindContainingInstallation(string executableDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableDirectory);

        string canonicalExecutablePath = PhysicalDirectoryPath.ResolveExisting(executableDirectory);
        for (DirectoryInfo? directory = new(canonicalExecutablePath);
             directory is not null;
             directory = directory.Parent)
        {
            foreach (SupportedGame game in _gamesInDetectionPriorityOrder)
            {
                if (HasRecognizedExecutable(game, directory.FullName))
                {
                    return new GameInstallationLocation(game, directory.FullName);
                }
            }
        }

        return null;
    }

    public GameInstallationValidationResult Validate(
        SupportedGame game,
        string? directory,
        string executableDirectory)
    {
        PerGame.EnsureSupported(game, nameof(game));
        ArgumentException.ThrowIfNullOrWhiteSpace(executableDirectory);

        if (string.IsNullOrWhiteSpace(directory))
        {
            return GameInstallationValidationResult.Invalid(GameInstallationValidationFailure.PathMissing);
        }

        string candidatePath;
        try
        {
            string selectedDirectory = directory.Trim().Trim('"').Trim();
            if (string.IsNullOrWhiteSpace(selectedDirectory))
            {
                return GameInstallationValidationResult.Invalid(GameInstallationValidationFailure.PathMissing);
            }

            if (!Path.IsPathFullyQualified(selectedDirectory))
            {
                return GameInstallationValidationResult.Invalid(GameInstallationValidationFailure.PathUnavailable);
            }

            candidatePath = LexicalPath.NormalizeFullPath(selectedDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return GameInstallationValidationResult.Invalid(GameInstallationValidationFailure.PathUnavailable);
        }

        if (!Directory.Exists(candidatePath))
        {
            return GameInstallationValidationResult.Invalid(GameInstallationValidationFailure.DirectoryNotFound);
        }

        try
        {
            FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
                candidatePath,
                "Game installation paths");
            FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
                executableDirectory,
                "Launcher paths");
        }
        catch (InvalidDataException)
        {
            return GameInstallationValidationResult.Invalid(GameInstallationValidationFailure.UnsafeFileSystemPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                exception,
                "A {SupportedGame} installation candidate path could not be safely inspected.",
                game);
            return GameInstallationValidationResult.Invalid(GameInstallationValidationFailure.PathUnavailable);
        }

        try
        {
            string canonicalGamePath = PhysicalDirectoryPath.ResolveExisting(candidatePath);
            string canonicalExecutablePath = PhysicalDirectoryPath.ResolveExisting(executableDirectory);
            if (LexicalPath.IsPathInDirectory(canonicalExecutablePath, canonicalGamePath))
            {
                return GameInstallationValidationResult.Invalid(
                    GameInstallationValidationFailure.LauncherLocationOverlapsGame);
            }

            string sharedDataPath = Path.Combine(
                canonicalExecutablePath,
                LauncherFileSystemLayout.LauncherDataFolderName);
            if (LexicalPath.IsPathInDirectory(canonicalGamePath, sharedDataPath))
            {
                return GameInstallationValidationResult.Invalid(GameInstallationValidationFailure.UnsafeFileSystemPath);
            }

            if (!HasBuiltInExecutable(game, canonicalGamePath))
            {
                return GameInstallationValidationResult.Invalid(
                    GameInstallationValidationFailure.BuiltInExecutableNotFound);
            }

            return GameInstallationValidationResult.Valid(canonicalGamePath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or NotSupportedException or
                UnauthorizedAccessException or Win32Exception)
        {
            _logger.LogWarning(
                exception,
                "A {SupportedGame} installation candidate could not be safely inspected.",
                game);
            return GameInstallationValidationResult.Invalid(GameInstallationValidationFailure.PathUnavailable);
        }
    }

    public LauncherInstallations DiscoverValidInstallations(
        LauncherInstallations current,
        string executableDirectory)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableDirectory);

        LauncherInstallations discovered = current;
        var occupiedPhysicalPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SupportedGame game in _gamesInDetectionPriorityOrder)
        {
            string? configuredPath = current.GetPath(game);
            GameInstallationValidationResult configuredResult =
                Validate(game, configuredPath, executableDirectory);
            if (configuredResult.IsValid)
            {
                occupiedPhysicalPaths.Add(configuredResult.CanonicalPath!);
                continue;
            }

            foreach (string candidate in _registry.ReadCandidates(game))
            {
                GameInstallationValidationResult candidateResult =
                    Validate(game, candidate, executableDirectory);
                if (!candidateResult.IsValid)
                {
                    continue;
                }

                if (!occupiedPhysicalPaths.Add(candidateResult.CanonicalPath!))
                {
                    continue;
                }

                discovered = discovered.WithPath(game, candidateResult.CanonicalPath);
                _logger.LogInformation("Discovered a valid {SupportedGame} installation.", game);
                break;
            }
        }

        return discovered;
    }

    private static bool HasBuiltInExecutable(SupportedGame game, string directory)
    {
        foreach (string executableName in LauncherFileSystemLayout.GetBuiltInGameExecutableNames(game))
        {
            string executablePath = Path.Combine(directory, executableName);
            if (File.Exists(executablePath) && !FileSystemPathSafety.IsReparsePoint(executablePath))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasRecognizedExecutable(SupportedGame game, string directory)
    {
        return game switch
        {
            SupportedGame.Generals =>
                File.Exists(Path.Combine(
                    directory,
                    LauncherFileSystemLayout.GeneralsCommunityExecutableFileName)),
            SupportedGame.ZeroHour =>
                File.Exists(Path.Combine(
                    directory,
                    LauncherFileSystemLayout.ZeroHourCommunityExecutableFileName)) ||
                File.Exists(Path.Combine(
                    directory,
                    LauncherFileSystemLayout.GeneralsOnlineExecutableFileName)),
            _ => false
        };
    }
}
