using System;

namespace GenLauncherGO.Core.Startup.Models;

/// <summary>
/// Describes an actionable game-installation validation outcome.
/// </summary>
public sealed record GameInstallationValidationResult
{
    private GameInstallationValidationResult(
        GameInstallationValidationFailure failure,
        string? canonicalPath)
    {
        Failure = failure;
        CanonicalPath = canonicalPath;
    }

    public bool IsValid => Failure == GameInstallationValidationFailure.None;

    public GameInstallationValidationFailure Failure { get; }

    public string? CanonicalPath { get; }

    public static GameInstallationValidationResult Valid(string canonicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);

        return new GameInstallationValidationResult(
            GameInstallationValidationFailure.None,
            canonicalPath);
    }

    public static GameInstallationValidationResult Invalid(GameInstallationValidationFailure failure)
    {
        if (failure == GameInstallationValidationFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure), failure, "A validation failure is required.");
        }

        return new GameInstallationValidationResult(failure, null);
    }
}
