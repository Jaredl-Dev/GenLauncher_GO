using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Features.Launcher;
using GenLauncherGO.Features.Launching;
using GenLauncherGO.Features.Startup;
using GenLauncherGO.Features.Updating;
using GenLauncherGO.Shared.Dialogs;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Testing;

internal static class TestLauncherApplicationUpdateCoordinator
{
    public static LauncherApplicationUpdateCoordinator Create(
        out LauncherRestartCoordinator restartCoordinator,
        LauncherPackageActivityService? packageActivityService = null,
        ILauncherDialogService? dialogService = null,
        LauncherLaunchCoordinator? launchCoordinator = null,
        ILauncherApplicationUpdateService? updateService = null,
        string? downloadedVersion = "1.2.0")
    {
        LauncherPackageActivityService resolvedPackageActivityService =
            packageActivityService ?? new LauncherPackageActivityService();
        ILauncherDialogService resolvedDialogService =
            dialogService ?? Substitute.For<ILauncherDialogService>();
        LauncherLaunchCoordinator resolvedLaunchCoordinator =
            launchCoordinator ?? TestLauncherLaunchCoordinator.Create(
                resolvedPackageActivityService,
                dialogService: resolvedDialogService);
        FakeStringLocalizer stringLocalizer = new();
        LauncherCloseGuard closeGuard = new(
            resolvedLaunchCoordinator,
            resolvedPackageActivityService,
            resolvedDialogService,
            stringLocalizer,
            NullLogger<LauncherCloseGuard>.Instance);
        restartCoordinator = new LauncherRestartCoordinator(
            closeGuard,
            NullLogger<LauncherRestartCoordinator>.Instance);
        ILauncherApplicationUpdateService resolvedUpdateService =
            updateService ?? Substitute.For<ILauncherApplicationUpdateService>();
        if (updateService == null)
        {
            resolvedUpdateService.CheckAndDownloadUpdateAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(downloadedVersion));
        }

        return new LauncherApplicationUpdateCoordinator(
            resolvedUpdateService,
            resolvedDialogService,
            closeGuard,
            restartCoordinator,
            stringLocalizer,
            NullLogger<LauncherApplicationUpdateCoordinator>.Instance,
            _ => true);
    }
}
