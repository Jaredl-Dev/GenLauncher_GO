using System;
using System.Threading;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Shared.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Builds the integrity coordinator with a resolution service that reports nothing to fix, which is the
///     arrangement every test that is not about integrity needs.
/// </summary>
internal static class TestLaunchContentIntegrityCoordinator
{
    public static LaunchContentIntegrityCoordinator Create(
        ILaunchContentIntegrityResolutionService? resolutionService = null,
        ILauncherContentCatalog? catalog = null,
        LauncherRuntimePathContext? runtimePaths = null,
        LauncherPackageActivityService? packageActivityService = null,
        ILauncherDialogService? dialogService = null,
        ILauncherStringLocalizer? stringLocalizer = null)
    {
        return new LaunchContentIntegrityCoordinator(
            resolutionService ?? CreateNoIssueResolutionService(),
            catalog ?? new FakeLauncherContentCatalog(),
            runtimePaths ?? TestLauncherPaths.CreateRuntimePathContext(TestLauncherPaths.Create()),
            packageActivityService ?? new LauncherPackageActivityService(),
            stringLocalizer ?? FakeStringLocalizer.Create(TestLocalizedStrings.Integrity),
            dialogService ?? Substitute.For<ILauncherDialogService>(),
            NullLogger<LaunchContentIntegrityCoordinator>.Instance);
    }

    private static ILaunchContentIntegrityResolutionService CreateNoIssueResolutionService()
    {
        ILaunchContentIntegrityResolutionService resolutionService =
            Substitute.For<ILaunchContentIntegrityResolutionService>();
        resolutionService.VerifyAsync(
                Arg.Any<LaunchContentIntegrityTargetRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new LaunchContentIntegrityVerificationResult(
                new ContentIntegrityReport(Array.Empty<ContentIntegrityIssue>()),
                Array.Empty<LaunchContentIntegrityTargetContext>()));
        return resolutionService;
    }
}
