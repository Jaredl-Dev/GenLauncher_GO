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
/// Validates supported installations and discovers candidates from the Windows registry.
/// </summary>
public sealed class WindowsGameInstallationService : IGameInstallationService
{
    private static readonly SupportedGame[] _supportedGames =
    {
        SupportedGame.ZeroHour,
        SupportedGame.Generals,
    };

    private readonly IGameInstallationRegistry _registry;
    private readonly ILogger<WindowsGameInstallationService> _logger;

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
            foreach (SupportedGame game in _supportedGames)
            {
                if (HasRequiredFiles(game, directory.FullName))
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
        EnsureSupported(game);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableDirectory);

        if (string.IsNullOrWhiteSpace(directory))
        {
            return Invalid(GameInstallationValidationFailure.PathMissing);
        }

        string candidatePath;
        try
        {
            if (!Path.IsPathFullyQualified(directory.Trim()))
            {
                return Invalid(GameInstallationValidationFailure.PathUnavailable);
            }

            candidatePath = LexicalPath.NormalizeFullPath(directory.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return Invalid(GameInstallationValidationFailure.PathUnavailable);
        }

        if (!Directory.Exists(candidatePath))
        {
            return Invalid(GameInstallationValidationFailure.DirectoryNotFound);
        }

        try
        {
            FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
                candidatePath,
                "Game installation paths must be rooted.",
                "Game installation paths must not traverse reparse points.");
            FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
                executableDirectory,
                "Launcher paths must be rooted.",
                "Launcher paths must not traverse reparse points.");
        }
        catch (InvalidDataException)
        {
            return Invalid(GameInstallationValidationFailure.UnsafeFileSystemPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                exception,
                "A {SupportedGame} installation candidate path could not be safely inspected.",
                game);
            return Invalid(GameInstallationValidationFailure.PathUnavailable);
        }

        try
        {
            string canonicalGamePath = PhysicalDirectoryPath.ResolveExisting(candidatePath);
            SupportedGame otherGame = game == SupportedGame.Generals
                ? SupportedGame.ZeroHour
                : SupportedGame.Generals;
            if (!HasRequiredFiles(game, canonicalGamePath) ||
                HasRequiredFiles(otherGame, canonicalGamePath))
            {
                return Invalid(GameInstallationValidationFailure.RequiredFilesMissing);
            }

            string canonicalExecutablePath = PhysicalDirectoryPath.ResolveExisting(executableDirectory);
            if (LexicalPath.IsPathInDirectory(canonicalExecutablePath, canonicalGamePath))
            {
                return Invalid(GameInstallationValidationFailure.LauncherLocationOverlapsGame);
            }

            string sharedDataPath = Path.Combine(
                canonicalExecutablePath,
                LauncherFileSystemLayout.LauncherDataFolderName);
            if (LexicalPath.IsPathInDirectory(canonicalGamePath, sharedDataPath))
            {
                return Invalid(GameInstallationValidationFailure.UnsafeFileSystemPath);
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
            return Invalid(GameInstallationValidationFailure.PathUnavailable);
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
        foreach (SupportedGame game in _supportedGames)
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

    private static GameInstallationValidationResult Invalid(GameInstallationValidationFailure failure)
    {
        return GameInstallationValidationResult.Invalid(failure);
    }

    private static void EnsureSupported(SupportedGame game)
    {
        if (game is not SupportedGame.Generals and not SupportedGame.ZeroHour)
        {
            throw new ArgumentOutOfRangeException(nameof(game), game, "A supported game is required.");
        }
    }

    private static bool HasRequiredFiles(SupportedGame game, string directory)
    {
        if (!File.Exists(Path.Combine(directory, LauncherFileSystemLayout.BinkLibraryFileName)))
        {
            return false;
        }

        return game switch
        {
            SupportedGame.Generals =>
                HasGameFile(directory, LauncherFileSystemLayout.GeneralsWindowArchiveFileName) &&
                File.Exists(Path.Combine(
                    directory,
                    LauncherFileSystemLayout.GeneralsCommunityExecutableFileName)),
            SupportedGame.ZeroHour =>
                HasGameFile(directory, LauncherFileSystemLayout.ZeroHourWindowArchiveFileName) &&
                (File.Exists(Path.Combine(
                     directory,
                     LauncherFileSystemLayout.ZeroHourCommunityExecutableFileName)) ||
                 File.Exists(Path.Combine(
                     directory,
                     LauncherFileSystemLayout.GeneralsOnlineExecutableFileName))),
            _ => false,
        };
    }

    private static bool HasGameFile(string directory, string fileName)
    {
        return File.Exists(Path.Combine(directory, fileName));
    }
}
