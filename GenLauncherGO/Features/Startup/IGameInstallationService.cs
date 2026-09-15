using System;
using GenLauncherGO.Features.Settings;
using GenLauncherGO.Shared.IO;

namespace GenLauncherGO.Features.Startup;

/// <summary>
///     Validates user-selected game directories and discovers valid Windows installations.
/// </summary>
internal interface IGameInstallationService
{
    /// <summary>
    ///     Finds a supported game root at or above the launcher executable directory, or returns
    ///     <see langword="null" /> for a standalone launcher.
    /// </summary>
    GameInstallationLocation? FindContainingInstallation(string executableDirectory);

    /// <summary>
    ///     Validates that one selected deployment root is an existing safe directory whose relationship to the launcher
    ///     is allowed. Executable selection is independent of this directory.
    /// </summary>
    GameInstallationValidationResult Validate(
        SupportedGame game,
        string? directory,
        string executableDirectory);

    /// <summary>
    ///     Retains valid configured paths and fills only missing or invalid paths from trusted registry views.
    /// </summary>
    LauncherInstallations DiscoverValidInstallations(
        LauncherInstallations current,
        string executableDirectory);
}

/// <summary>
///     Applies installation-set invariants on top of the canonical per-game validation boundary.
/// </summary>
internal static class GameInstallationServiceExtensions
{
    /// <summary>
    ///     Validates and canonicalizes the complete supported installation set.
    ///     Empty paths remain optional, but every nonempty path must be valid, at least one installation must remain,
    ///     and the two games must not resolve to overlapping physical directory trees.
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

        string? enteredGeneralsPath = installations.Generals?.Trim();
        string? enteredZeroHourPath = installations.ZeroHour?.Trim();
        bool hasGeneralsPath = !string.IsNullOrEmpty(enteredGeneralsPath);
        bool hasZeroHourPath = !string.IsNullOrEmpty(enteredZeroHourPath);
        bool hasInvalidNonemptyPath =
            (hasGeneralsPath && !generals.IsValid) ||
            (hasZeroHourPath && !zeroHour.IsValid);
        bool hasOverlappingEnteredPaths =
            hasGeneralsPath &&
            hasZeroHourPath &&
            LexicalPath.AreOverlapping(enteredGeneralsPath, enteredZeroHourPath);
        bool hasOverlappingCanonicalPaths =
            generals is { IsValid: true, CanonicalPath: not null } &&
            zeroHour is { IsValid: true, CanonicalPath: not null } &&
            LexicalPath.AreOverlapping(generals.CanonicalPath, zeroHour.CanonicalPath);
        bool hasOverlappingPaths = hasOverlappingEnteredPaths || hasOverlappingCanonicalPaths;
        var canonicalInstallations = new LauncherInstallations
        {
            Generals = generals.IsValid ? generals.CanonicalPath : null,
            ZeroHour = zeroHour.IsValid ? zeroHour.CanonicalPath : null
        };
        bool isValid = !hasInvalidNonemptyPath &&
                       !hasOverlappingPaths &&
                       (generals.IsValid || zeroHour.IsValid);

        return new LauncherInstallationsValidationResult(
            generals,
            zeroHour,
            canonicalInstallations,
            hasOverlappingPaths,
            isValid);
    }
}
