using Avalonia.Controls;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Startup.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Services;

[Collection("Avalonia")]
public sealed class LauncherRestartCoordinatorTests
{
    [Fact]
    public void TryRequestRestart_WhenPackageActivityIsActive_IsBlockedWithoutCloseOverride()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPackageActivityService packageActivityService = new();
            packageActivityService.TryBegin(
                    "Shockwave",
                    out LauncherPackageActivityService.LauncherPackageActivityLease? lease)
                .Should()
                .BeTrue();
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            LauncherRestartCoordinator restartCoordinator = CreateCoordinator(
                packageActivityService,
                dialogService);
            var owner = new Window();

            try
            {
                bool restartAccepted = await restartCoordinator.TryRequestRestartAsync(owner);

                restartAccepted.Should().BeFalse();
                restartCoordinator.IsRestartRequested.Should().BeFalse();
                await dialogService.Received(1).ShowInfoAsync(
                    Arg.Is<LauncherInfoDialogRequest>(request =>
                        request != null &&
                        request.MainMessage == "Restart unavailable" &&
                        request.DetailMessage == "Finish Shockwave before restarting."),
                    owner);
                await dialogService.DidNotReceive().ShowWarningConfirmationAsync(
                    Arg.Any<LauncherInfoDialogRequest>(),
                    Arg.Any<string?>(),
                    Arg.Any<Window?>());
            }
            finally
            {
                lease?.Dispose();
                owner.Close();
            }
        });
    }

    [Fact]
    public void TryRequestRestart_WhenNoOperationIsActive_IsRecorded()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherRestartCoordinator restartCoordinator = CreateCoordinator(
                new LauncherPackageActivityService(),
                Substitute.For<ILauncherDialogService>());
            var owner = new Window();

            bool restartAccepted = await restartCoordinator.TryRequestRestartAsync(owner);

            restartAccepted.Should().BeTrue();
            restartCoordinator.IsRestartRequested.Should().BeTrue();
            owner.Close();
        });
    }

    private static LauncherRestartCoordinator CreateCoordinator(
        LauncherPackageActivityService packageActivityService,
        ILauncherDialogService dialogService)
    {
        var stringLocalizer = FakeStringLocalizer.Create(TestLocalizedStrings.Launcher);
        LauncherCloseGuard closeGuard = new(
            TestLauncherLaunchCoordinator.Create(
                packageActivityService,
                stringLocalizer: stringLocalizer,
                dialogService: dialogService),
            packageActivityService,
            dialogService,
            stringLocalizer,
            NullLogger<LauncherCloseGuard>.Instance);

        return new LauncherRestartCoordinator(closeGuard, NullLogger<LauncherRestartCoordinator>.Instance);
    }
}
