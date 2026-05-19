using System;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup.Models;

namespace GenLauncherGO.Core.Startup.Contracts;

/// <summary>
/// Validates user-selected game directories and discovers valid Windows installations.
/// </summary>
public interface IGameInstallationService
{
    /// <summary>
    /// Finds a supported game root at or above the launcher executable directory, or returns
    /// <see langword="null"/> for a standalone launcher.
    /// </summary>
    GameInstallationLocation? FindContainingInstallation(string executableDirectory);

    /// <summary>
    /// Validates one installation root, including its relationship to the launcher executable directory.
    /// </summary>
    GameInstallationValidationResult Validate(
        SupportedGame game,
        string? directory,
        string executableDirectory);

    /// <summary>
    /// Retains valid configured paths and fills only missing or invalid paths from trusted registry views.
    /// </summary>
    LauncherInstallations DiscoverValidInstallations(
        LauncherInstallations current,
        string executableDirectory);
}

/// <summary>
/// Applies installation-set invariants on top of the canonical per-game validation boundary.
/// </summary>
public static class GameInstallationServiceExtensions
{
    /// <summary>
    /// Validates and canonicalizes the complete supported installation set.
    /// Empty paths remain optional, but every nonempty path must be valid, at least one installation must remain,
    /// and the two games must not resolve to the same physical directory.
    /// </summary>
    public static LauncherInstallationsValidationResult ValidateInstallations(
        this IGameInstallationService installationService,
        LauncherInstallations installations,
        string executableDirectory)
    {
        ArgumentNullException.ThrowIfNull(installationService);
        ArgumentNullException.ThrowIfNull(installations);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableDirectory);

        GameInstallationValidationResult generals = installationService.Validate(
            SupportedGame.Generals,
            installations.Generals,
            executableDirectory);
        GameInstallationValidationResult zeroHour = installationService.Validate(
            SupportedGame.ZeroHour,
            installations.ZeroHour,
            executableDirectory);

        bool hasInvalidNonemptyPath =
            (!String.IsNullOrWhiteSpace(installations.Generals) && !generals.IsValid) ||
            (!String.IsNullOrWhiteSpace(installations.ZeroHour) && !zeroHour.IsValid);
        bool hasDuplicatePath = generals is { IsValid: true, CanonicalPath: not null } &&
                                zeroHour is { IsValid: true, CanonicalPath: not null } &&
                                String.Equals(
                                    generals.CanonicalPath,
                                    zeroHour.CanonicalPath,
                                    StringComparison.OrdinalIgnoreCase);
        var canonicalInstallations = new LauncherInstallations
        {
            Generals = generals.IsValid ? generals.CanonicalPath : null,
            ZeroHour = zeroHour.IsValid ? zeroHour.CanonicalPath : null,
        };
        bool isValid = !hasInvalidNonemptyPath &&
                       !hasDuplicatePath &&
                       (generals.IsValid || zeroHour.IsValid);

        return new LauncherInstallationsValidationResult(
            generals,
            zeroHour,
            canonicalInstallations,
            hasDuplicatePath,
            isValid);
    }
}
