using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Remote;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Features.Startup.Contracts;
using GenLauncherGO.UI.Features.Startup.ViewModels;

namespace GenLauncherGO.Tests.UI.Features.Startup.ViewModels;

[Collection("Avalonia")]
public sealed class InitWindowViewModelTests
{
    [Fact]
    public void StartAsync_WhenPreparationSucceeds_RaisesStartupCompletedAndSetsConnected()
    {
        StaTestRunner.Run(async () =>
        {
            ILauncherPathResolver pathResolver = Substitute.For<ILauncherPathResolver>();
            ILaunchPreparationService launchPreparationService = Substitute.For<ILaunchPreparationService>();
            launchPreparationService.Recover(Arg.Any<LauncherPaths>(), Arg.Any<CancellationToken>())
                .Returns(true);
            IRemoteConnectionProbe connectionProbe = Substitute.For<IRemoteConnectionProbe>();
            connectionProbe.CanConnectAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(true);
            var catalog = new FakeLauncherContentCatalog
            {
                RepositoryModificationNames = new[] { "Contra" }
            };
            LauncherRuntimeContext runtimeContext = CreateRuntimeContext();
            InitWindowViewModel viewModel = CreateViewModel(
                pathResolver,
                launchPreparationService,
                connectionProbe,
                catalog,
                runtimeContext,
                new RecordingStartupDialogService());
            InitWindowStartupCompletedEventArgs? completedArgs = null;
            viewModel.StartupCompleted += (_, args) => completedArgs = args;

            await viewModel.StartAsync();

            completedArgs.Should().NotBeNull();
            completedArgs!.Connected.Should().BeTrue();
            runtimeContext.Connected.Should().BeTrue();
            runtimeContext.CurrentlyManagedGame.Should().Be(SupportedGame.ZeroHour);
            catalog.InitializationRequests.Should().ContainSingle()
                .Which.RemoteManifestUri.Should()
                .Be(new Uri(LauncherApplicationDefaults.ZeroHourRepositoryUrl));
            pathResolver.Received(1).PrepareGameDirectories(runtimeContext.LauncherPaths, true);
        });
    }

    [Fact]
    public void StartAsync_WhenRecoveryCanceled_RequestsShutdownWithoutCompletion()
    {
        StaTestRunner.Run(async () =>
        {
            ILaunchPreparationService launchPreparationService = Substitute.For<ILaunchPreparationService>();
            launchPreparationService.Recover(Arg.Any<LauncherPaths>(), Arg.Any<CancellationToken>())
                .Returns(false);
            LauncherRuntimeContext runtimeContext = CreateRuntimeContext();
            var catalog = new FakeLauncherContentCatalog();
            InitWindowViewModel viewModel = CreateViewModel(
                Substitute.For<ILauncherPathResolver>(),
                launchPreparationService,
                Substitute.For<IRemoteConnectionProbe>(),
                catalog,
                runtimeContext,
                new RecordingStartupDialogService());
            bool completed = false;
            bool shutdownRequested = false;
            viewModel.StartupCompleted += (_, _) => completed = true;
            viewModel.ShutdownRequested += (_, _) => shutdownRequested = true;

            await viewModel.StartAsync();

            completed.Should().BeFalse();
            shutdownRequested.Should().BeTrue();
            runtimeContext.Connected.Should().BeFalse();
            catalog.InitializationRequests.Should().BeEmpty();
        });
    }

    [Fact]
    public void StartAsync_WhenConnectionFails_ShowsThemedStartupMessageAndCompletesOffline()
    {
        StaTestRunner.Run(async () =>
        {
            ILaunchPreparationService launchPreparationService = Substitute.For<ILaunchPreparationService>();
            launchPreparationService.Recover(Arg.Any<LauncherPaths>(), Arg.Any<CancellationToken>())
                .Returns(true);
            IRemoteConnectionProbe connectionProbe = Substitute.For<IRemoteConnectionProbe>();
            connectionProbe.CanConnectAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(false);
            var catalog = new FakeLauncherContentCatalog();
            LauncherRuntimeContext runtimeContext = CreateRuntimeContext();
            RecordingStartupDialogService startupDialogService = new();
            InitWindowViewModel viewModel = CreateViewModel(
                Substitute.For<ILauncherPathResolver>(),
                launchPreparationService,
                connectionProbe,
                catalog,
                runtimeContext,
                startupDialogService);
            InitWindowStartupCompletedEventArgs? completedArgs = null;
            viewModel.StartupCompleted += (_, args) => completedArgs = args;

            await viewModel.StartAsync();

            completedArgs.Should().NotBeNull();
            completedArgs!.Connected.Should().BeFalse();
            startupDialogService.TitledMessages.Should().ContainSingle()
                .Which.Should().Be(("Information", "Cannot connect"));
            catalog.InitializationRequests.Should().ContainSingle()
                .Which.RemoteManifestUri.Should().BeNull();
            catalog.ChildManifestRequests.Should().BeEmpty();
        });
    }

    [Fact]
    public void PrepareLauncherAsync_WhenRecoveryRetrySucceeds_ContinuesStartup()
    {
        StaTestRunner.Run(async () =>
        {
            ILaunchPreparationService launchPreparationService = Substitute.For<ILaunchPreparationService>();
            launchPreparationService.Recover(Arg.Any<LauncherPaths>(), Arg.Any<CancellationToken>())
                .Returns(
                    false,
                    true);
            IRemoteConnectionProbe connectionProbe = Substitute.For<IRemoteConnectionProbe>();
            connectionProbe.CanConnectAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(false);
            var catalog = new FakeLauncherContentCatalog();
            RecordingStartupDialogService startupDialogService = new()
            {
                RetryResult = true
            };
            InitWindowViewModel viewModel = CreateViewModel(
                Substitute.For<ILauncherPathResolver>(),
                launchPreparationService,
                connectionProbe,
                catalog,
                CreateRuntimeContext(),
                startupDialogService);
            bool shutdownRequested = false;
            viewModel.ShutdownRequested += (_, _) => shutdownRequested = true;

            bool connected = await viewModel.PrepareLauncherAsync();

            connected.Should().BeFalse();
            shutdownRequested.Should().BeFalse();
            startupDialogService.RetryCancelWarnings.Should().ContainSingle();
            catalog.InitializationRequests.Should().ContainSingle();
        });
    }

    [Fact]
    public void PrepareLauncherAsync_WhenConnectedWithSelectedMod_LoadsSelectedModChildren()
    {
        StaTestRunner.Run(async () =>
        {
            ILaunchPreparationService launchPreparationService = Substitute.For<ILaunchPreparationService>();
            launchPreparationService.Recover(Arg.Any<LauncherPaths>(), Arg.Any<CancellationToken>())
                .Returns(true);
            IRemoteConnectionProbe connectionProbe = Substitute.For<IRemoteConnectionProbe>();
            connectionProbe.CanConnectAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(true);
            var catalog = new FakeLauncherContentCatalog
            {
                RepositoryModificationNames = new[] { "Contra" }
            };
            var selectedVersion = new LauncherContentVersion
            {
                Installation = new LauncherContentInstallation { IsSelected = true },
                Name = "Contra",
                Version = "1.0",
                ModificationType = ModificationType.Mod
            };
            catalog.Data.AddOrUpdate(selectedVersion);
            LauncherContent selectedMod = catalog.Data.Modifications.Single();
            selectedMod.IsSelected = true;
            InitWindowViewModel viewModel = CreateViewModel(
                Substitute.For<ILauncherPathResolver>(),
                launchPreparationService,
                connectionProbe,
                catalog,
                CreateRuntimeContext(),
                new RecordingStartupDialogService());

            bool connected = await viewModel.PrepareLauncherAsync();

            connected.Should().BeTrue();
            catalog.ChildManifestRequests.Should().ContainSingle().Which.Should().Be(selectedMod.ContentKey);
        });
    }

    [Fact]
    public void PrepareLauncherAsync_WhenCatalogNamesAreMissing_TreatsStartupAsOffline()
    {
        StaTestRunner.Run(async () =>
        {
            ILaunchPreparationService launchPreparationService = Substitute.For<ILaunchPreparationService>();
            launchPreparationService.Recover(Arg.Any<LauncherPaths>(), Arg.Any<CancellationToken>())
                .Returns(true);
            IRemoteConnectionProbe connectionProbe = Substitute.For<IRemoteConnectionProbe>();
            connectionProbe.CanConnectAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(true);
            var catalog = new FakeLauncherContentCatalog
            {
                RepositoryModificationNames = null
            };
            RecordingStartupDialogService startupDialogService = new();
            InitWindowViewModel viewModel = CreateViewModel(
                Substitute.For<ILauncherPathResolver>(),
                launchPreparationService,
                connectionProbe,
                catalog,
                CreateRuntimeContext(),
                startupDialogService);

            bool connected = await viewModel.PrepareLauncherAsync();

            connected.Should().BeFalse();
            startupDialogService.TitledMessages.Should().ContainSingle()
                .Which.Should().Be(("Information", "Cannot connect"));
            catalog.ChildManifestRequests.Should().BeEmpty();
        });
    }

    private static InitWindowViewModel CreateViewModel(
        ILauncherPathResolver pathResolver,
        ILaunchPreparationService launchPreparationService,
        IRemoteConnectionProbe connectionProbe,
        ILauncherContentCatalog catalog,
        LauncherRuntimeContext runtimeContext,
        IStartupDialogService startupDialogService)
    {
        return new InitWindowViewModel(
            connectionProbe,
            launchPreparationService,
            pathResolver,
            catalog,
            runtimeContext,
            new FakeStringLocalizer(new Dictionary<string, string>
            {
                ["CannotConnect"] = "Cannot connect",
                ["DeploymentRecoveryFailed"] = "Deployment recovery failed",
                ["Info"] = "Information"
            }),
            startupDialogService);
    }

    private static LauncherRuntimeContext CreateRuntimeContext()
    {
        return new LauncherRuntimeContext(
            TestLauncherPaths.CreateRuntimePathContext(TestLauncherPaths.Create()),
            "1.2.3");
    }
}
