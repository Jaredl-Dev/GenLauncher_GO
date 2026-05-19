using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Core.Startup.Models;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Features.Startup.Contracts;
using GenLauncherGO.UI.Features.Startup.Models;
using GenLauncherGO.UI.Shared.Localization;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.Tests.UI.Features.Startup;

[Collection("AvaloniaLocalization")]
public sealed class LauncherApplicationHostTests
{
    [Fact]
    public async Task RunWhenStandaloneStorageCannotBeResolvedShowsStartupMessageAsync()
    {
        AvaloniaLauncherStringLocalizer localizer = new();
        var pathResolver = new StubLauncherPathResolver();
        var hostEnvironment = new StubLauncherHostEnvironmentService();
        var startupDialogService = new RecordingStartupDialogService();
        using LauncherApplicationHost host = new(
            pathResolver,
            hostEnvironment,
            localizer,
            startupDialogService);

        await host.RunAsync();

        startupDialogService.Messages.Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();
        pathResolver.ResolvedExecutableDirectory.Should().Be(hostEnvironment.ExecutableDirectory);
        pathResolver.PrepareLauncherDirectoriesCount.Should().Be(0);
    }

    [Fact]
    public async Task RunWhenProcessIsNotElevatedStopsBeforeResolvingStorageAsync()
    {
        AvaloniaLauncherStringLocalizer localizer = new();
        var pathResolver = new StubLauncherPathResolver();
        var hostEnvironment = new StubLauncherHostEnvironmentService
        {
            CurrentProcessElevated = false,
        };
        var startupDialogService = new RecordingStartupDialogService();
        using LauncherApplicationHost host = new(
            pathResolver,
            hostEnvironment,
            localizer,
            startupDialogService);

        await host.RunAsync();

        startupDialogService.Messages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(localizer["AdministratorPermissionRequired"]);
        pathResolver.ResolvedExecutableDirectory.Should().BeNull();
        pathResolver.PrepareLauncherDirectoriesCount.Should().Be(0);
    }

    [Fact]
    public async Task RunWhenLauncherIsInsideGameStopsBeforeCreatingStandaloneStorageAsync()
    {
        using var directory = new TestDirectory();
        AvaloniaLauncherStringLocalizer localizer = new();
        var hostEnvironment = new StubLauncherHostEnvironmentService();
        var storagePaths = new LauncherStoragePaths(directory.Path);
        var pathResolver = new StubLauncherPathResolver
        {
            ResolvedPaths = storagePaths,
        };
        var startupDialogService = new RecordingStartupDialogService();
        var startupWorkflow = new StubStandaloneStartupWorkflow
        {
            LauncherLocationBlocked = true,
        };
        using LauncherApplicationHost host = new(
            pathResolver,
            hostEnvironment,
            localizer,
            startupDialogService,
            startupWorkflow);

        await host.RunAsync();

        startupWorkflow.LocationChecks.Should().Be(1);
        startupWorkflow.RunCount.Should().Be(0);
        pathResolver.PrepareLauncherDirectoriesCount.Should().Be(0);
        Directory.Exists(storagePaths.LogsDirectory).Should().BeFalse();
        startupDialogService.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task RunUsesInjectedWorkflowInStandaloneStartupOrderAsync()
    {
        using var directory = new TestDirectory();
        var events = new List<string>();
        AvaloniaLauncherStringLocalizer localizer = new();
        var pathResolver = new StubLauncherPathResolver
        {
            Events = events,
            ResolvedPaths = new LauncherStoragePaths(directory.Path),
        };
        var hostEnvironment = new StubLauncherHostEnvironmentService
        {
            Events = events,
        };
        var startupWorkflow = new StubStandaloneStartupWorkflow
        {
            Events = events,
        };
        using LauncherApplicationHost host = new(
            pathResolver,
            hostEnvironment,
            localizer,
            new RecordingStartupDialogService(),
            startupWorkflow);

        await host.RunAsync();

        events.Should().Equal(
            "elevation",
            "resolve-storage",
            "location",
            "prepare-storage",
            "single-instance",
            "setup");
        startupWorkflow.LocationChecks.Should().Be(1);
        startupWorkflow.RunCount.Should().Be(1);
    }

    [Fact]
    public void StartHandlesDispatcherFailuresRaisedDuringStandaloneSetup()
    {
        StaTestRunner.Run(async () =>
        {
            using var directory = new TestDirectory();
            var expectedException = new InvalidOperationException("Picker failed during setup.");
            AvaloniaLauncherStringLocalizer localizer = new();
            var pathResolver = new StubLauncherPathResolver
            {
                ResolvedPaths = new LauncherStoragePaths(directory.Path),
            };
            var startupDialogService = new RecordingStartupDialogService();
            var startupWorkflow = new StubStandaloneStartupWorkflow
            {
                DispatcherException = expectedException,
            };
            using LauncherApplicationHost host = new(
                pathResolver,
                new StubLauncherHostEnvironmentService(),
                localizer,
                startupDialogService,
                startupWorkflow);
            IClassicDesktopStyleApplicationLifetime desktop =
                Substitute.For<IClassicDesktopStyleApplicationLifetime>();

            bool started = await host.StartAsync(desktop);
            await Dispatcher.UIThread.InvokeAsync(() => { });

            started.Should().BeFalse();
            startupDialogService.Messages.Should()
                .ContainSingle(message => message.Contains(expectedException.Message, StringComparison.Ordinal));
        });
    }

    private sealed class StubLauncherPathResolver : ILauncherPathResolver
    {
        public List<string>? Events { get; init; }

        public LauncherStoragePaths? ResolvedPaths { get; init; }

        public string? ResolvedExecutableDirectory { get; private set; }

        public int PrepareLauncherDirectoriesCount { get; private set; }

        public LauncherStoragePaths Resolve(string executableDirectory)
        {
            Events?.Add("resolve-storage");
            ResolvedExecutableDirectory = executableDirectory;
            return ResolvedPaths ??
                   throw new InvalidOperationException("The standalone storage path could not be resolved.");
        }

        public void PrepareLauncherDirectories(LauncherStoragePaths paths)
        {
            Events?.Add("prepare-storage");
            PrepareLauncherDirectoriesCount++;
        }

        public void PrepareGameDirectories(LauncherPaths paths, bool cleanTemporaryDirectory)
        {
        }
    }

    private sealed class StubLauncherHostEnvironmentService : ILauncherHostEnvironmentService
    {
        public List<string>? Events { get; init; }

        public string ExecutableDirectory { get; init; } = @"C:\Launcher";

        public bool CurrentProcessElevated { get; init; } = true;

        public void ActivateCurrentProcessWindow()
        {
        }

        public string GetExecutableDirectory()
        {
            return ExecutableDirectory;
        }

        public bool IsCurrentProcessElevated()
        {
            Events?.Add("elevation");
            return CurrentProcessElevated;
        }

        public bool IsProtectedProgramFilesDirectory(string directory)
        {
            return false;
        }

        public LauncherRestartResult TryRestartCurrentProcess()
        {
            return LauncherRestartResult.Success;
        }

        public ILauncherSingleInstanceGuard TryAcquireSingleInstance(string instanceName, TimeSpan retryDelay)
        {
            Events?.Add("single-instance");
            return new StubLauncherSingleInstanceGuard();
        }
    }

    private sealed class StubLauncherSingleInstanceGuard : ILauncherSingleInstanceGuard
    {
        public bool IsAcquired => true;

        public void Dispose()
        {
        }
    }

    private sealed class StubStandaloneStartupWorkflow : IStandaloneStartupWorkflow
    {
        public List<string>? Events { get; init; }

        public bool LauncherLocationBlocked { get; init; }

        public Exception? DispatcherException { get; init; }

        public int LocationChecks { get; private set; }

        public int RunCount { get; private set; }

        public Task<bool> ShowBlockingLauncherLocationAsync(
            LauncherStoragePaths storagePaths)
        {
            Events?.Add("location");
            LocationChecks++;
            return Task.FromResult(LauncherLocationBlocked);
        }

        public async Task<StandaloneStartupResult> RunAsync(
            LauncherStoragePaths storagePaths,
            ILauncherPreferencesService preferencesService)
        {
            Events?.Add("setup");
            RunCount++;
            if (DispatcherException != null)
            {
                Exception exception = DispatcherException;
                Dispatcher.UIThread.Post(() => throw exception);
                await Dispatcher.UIThread.InvokeAsync(() => { });
            }

            return StandaloneStartupResult.Canceled;
        }
    }

    private sealed class RecordingStartupDialogService : IStartupDialogService
    {
        public List<string> Messages { get; } = new();

        public Task ShowMessageAsync(string message)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }

        public Task ShowThemedMessageAsync(string title, string message, ColorsInfo colors)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }

        public Task<bool> ShowRetryCancelWarningAsync(string title, string message)
        {
            Messages.Add(message);
            return Task.FromResult(false);
        }
    }
}
