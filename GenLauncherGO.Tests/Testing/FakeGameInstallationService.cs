using System;
using System.Collections.Generic;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Core.Startup.Models;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Answers installation validation from a rule the test supplies, and records what it was asked about.
/// </summary>
/// <remarks>
///     The default rule accepts any nonblank directory as its own canonical path, which is the arrangement almost
///     every startup test wants: the interesting behavior is what the caller does with the verdict, not how it was
///     reached.
/// </remarks>
internal sealed class FakeGameInstallationService : IGameInstallationService
{
    public Func<SupportedGame, string?, GameInstallationValidationResult> ValidationRule { get; init; } =
        (_, directory) => string.IsNullOrWhiteSpace(directory)
            ? GameInstallationValidationResult.Invalid(GameInstallationValidationFailure.PathMissing)
            : GameInstallationValidationResult.Valid(directory);

    /// <summary>
    ///     Returned from <see cref="DiscoverValidInstallations" />, or the supplied set when left unset.
    /// </summary>
    public LauncherInstallations? DiscoveredInstallations { get; set; }

    public GameInstallationLocation? ContainingInstallation { get; set; }

    public List<(SupportedGame Game, string? Directory, string ExecutableDirectory)> ValidateCalls { get; } = [];

    public List<(LauncherInstallations Current, string ExecutableDirectory)> DiscoverValidInstallationsCalls
    {
        get;
    } = [];

    public GameInstallationLocation? FindContainingInstallation(string executableDirectory)
    {
        return ContainingInstallation;
    }

    public GameInstallationValidationResult Validate(
        SupportedGame game,
        string? directory,
        string executableDirectory)
    {
        ValidateCalls.Add((game, directory, executableDirectory));
        return ValidationRule(game, directory);
    }

    public LauncherInstallations DiscoverValidInstallations(
        LauncherInstallations current,
        string executableDirectory)
    {
        DiscoverValidInstallationsCalls.Add((current, executableDirectory));
        return DiscoveredInstallations ?? current;
    }
}
