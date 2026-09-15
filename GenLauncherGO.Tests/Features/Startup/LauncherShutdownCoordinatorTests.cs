using System.Collections.Generic;
using System.Threading;
using GenLauncherGO.Features.Launching;
using GenLauncherGO.Features.Startup;
using GenLauncherGO.Features.Updating;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Features.Startup;

public sealed class LauncherShutdownCoordinatorTests
{
    [Fact]
    public void ShutdownForApplicationUpdate_CleansAndReleasesGuardBeforeUpdaterHandoff()
    {
        var events = new List<string>();
        LauncherShutdownCoordinator coordinator = CreateCoordinator(events, updateHandoffSucceeded: true);

        coordinator.Shutdown(
            TestLauncherPaths.Create(),
            LauncherRestartKind.ApplicationUpdate,
            () => events.Add("release-single-instance"));

        events.Should().Equal("cleanup", "release-single-instance", "update-handoff");
    }

    [Fact]
    public void ShutdownWhenApplicationUpdateHandoffFails_StartsNormalRestartAfterCleanupAndRelease()
    {
        var events = new List<string>();
        LauncherShutdownCoordinator coordinator = CreateCoordinator(events, updateHandoffSucceeded: false);

        coordinator.Shutdown(
            TestLauncherPaths.Create(),
            LauncherRestartKind.ApplicationUpdate,
            () => events.Add("release-single-instance"));

        events.Should().Equal(
            "cleanup",
            "release-single-instance",
            "update-handoff",
            "normal-restart");
    }

    private static LauncherShutdownCoordinator CreateCoordinator(
        ICollection<string> events,
        bool updateHandoffSucceeded)
    {
        ILaunchPreparationService launchPreparationService = Substitute.For<ILaunchPreparationService>();
        launchPreparationService.Cleanup(Arg.Any<LauncherPaths>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                events.Add("cleanup");
                return true;
            });
        ILauncherApplicationUpdateService updateService = Substitute.For<ILauncherApplicationUpdateService>();
        updateService.TryHandoffToUpdateAndRestart().Returns(_ =>
        {
            events.Add("update-handoff");
            return updateHandoffSucceeded;
        });
        ILauncherHostEnvironmentService hostEnvironmentService = Substitute.For<ILauncherHostEnvironmentService>();
        hostEnvironmentService.TryRestartCurrentProcess().Returns(_ =>
        {
            events.Add("normal-restart");
            return LauncherRestartResult.Success;
        });

        return new LauncherShutdownCoordinator(
            launchPreparationService,
            hostEnvironmentService,
            updateService,
            NullLogger<LauncherShutdownCoordinator>.Instance);
    }
}
