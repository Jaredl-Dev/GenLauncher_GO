using System;
using System.Collections.Generic;
using System.Threading;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Core.Startup.Models;
using GenLauncherGO.UI.Features.Startup.Services;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Tests.UI.Features.Startup.Services;

public sealed class LauncherShutdownCoordinatorTests
{
    [Fact]
    public void Shutdown_RestartRequested_CleansReleasesAndRestartsInOrder()
    {
        ShutdownScenario scenario = new();

        scenario.Shutdown(true);

        scenario.Events.Should().Equal("cleanup", "release", "restart");
    }

    [Fact]
    public void Shutdown_CleanupReturnsFalse_LogsWarningAndRestarts()
    {
        ShutdownScenario scenario = new()
        {
            CleanupSucceeded = false
        };

        scenario.Shutdown(true);

        scenario.Events.Should().Equal("cleanup", "release", "restart");
        scenario.Logger.Entries.Should().ContainSingle(entry =>
            entry.LogLevel == LogLevel.Warning &&
            entry.Exception == null &&
            entry.Message.Contains("did not complete successfully", StringComparison.Ordinal));
    }

    [Fact]
    public void Shutdown_CleanupThrows_LogsExceptionAndRestarts()
    {
        var expectedException = new InvalidOperationException("Cleanup failed.");
        ShutdownScenario scenario = new()
        {
            CleanupException = expectedException
        };

        scenario.Shutdown(true);

        scenario.Events.Should().Equal("cleanup", "release", "restart");
        scenario.Logger.Entries.Should().ContainSingle(entry =>
            entry.LogLevel == LogLevel.Warning &&
            ReferenceEquals(entry.Exception, expectedException) &&
            entry.Message.Contains("original state", StringComparison.Ordinal));
    }

    [Fact]
    public void Shutdown_RestartCannotStart_LogsFailure()
    {
        const string RestartError = "Replacement process was not started.";
        ShutdownScenario scenario = new()
        {
            RestartResult = LauncherRestartResult.Failure(RestartError)
        };

        scenario.Shutdown(true);

        scenario.Events.Should().Equal("cleanup", "release", "restart");
        scenario.Logger.Entries.Should().ContainSingle(entry =>
            entry.LogLevel == LogLevel.Error &&
            entry.Message.Contains(RestartError, StringComparison.Ordinal));
    }

    [Fact]
    public void Shutdown_MultipleCalls_CleansOnceAndDoesNotAcceptALateRestart()
    {
        ShutdownScenario scenario = new();

        scenario.Shutdown(false);
        scenario.Shutdown(true);

        scenario.Events.Should().Equal("cleanup");
        scenario.HostEnvironmentService.DidNotReceive().TryRestartCurrentProcess();
    }

    private sealed class ShutdownScenario
    {
        private readonly LauncherShutdownCoordinator _coordinator;
        private readonly LauncherPaths _paths = TestLauncherPaths.Create();

        public ShutdownScenario()
        {
            ILaunchPreparationService launchPreparationService = Substitute.For<ILaunchPreparationService>();
            launchPreparationService.Cleanup(_paths, CancellationToken.None)
                .Returns(_ =>
                {
                    Events.Add("cleanup");
                    if (CleanupException != null)
                    {
                        throw CleanupException;
                    }

                    return CleanupSucceeded;
                });
            HostEnvironmentService.TryRestartCurrentProcess()
                .Returns(_ =>
                {
                    Events.Add("restart");
                    return RestartResult;
                });
            _coordinator = new LauncherShutdownCoordinator(
                launchPreparationService,
                HostEnvironmentService,
                Logger);
        }

        public bool CleanupSucceeded { get; init; } = true;

        public Exception? CleanupException { get; init; }

        public List<string> Events { get; } = [];

        public ILauncherHostEnvironmentService HostEnvironmentService { get; } =
            Substitute.For<ILauncherHostEnvironmentService>();

        public RecordingLogger<LauncherShutdownCoordinator> Logger { get; } = new();

        public LauncherRestartResult RestartResult { get; init; } = LauncherRestartResult.Success;

        public void Shutdown(bool restartRequested)
        {
            _coordinator.Shutdown(_paths, restartRequested, () => Events.Add("release"));
        }
    }
}
