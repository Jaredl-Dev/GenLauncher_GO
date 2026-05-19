using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Remote;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Features.Startup.Contracts;
using GenLauncherGO.UI.Features.Startup.ViewModels;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.Tests.UI.Features.Startup.ViewModels;

public sealed class InitWindowViewModelTests
{
    [Fact]
    public void StartAsync_WhenPreparationSucceeds_RaisesStartupCompletedAndSetsConnected()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherContentCatalogInitializationRequest? initializationRequest = null;
            ILauncherPathResolver pathResolver = Substitute.For<ILauncherPathResolver>();
            ILaunchPreparationService launchPreparationService = Substitute.For<ILaunchPreparationService>();
            launchPreparationService.Recover(Arg.Any<LauncherPaths>(), Arg.Any<CancellationToken>())
                .Returns(true);
            IRemoteConnectionProbe connectionProbe = Substitute.For<IRemoteConnectionProbe>();
            connectionProbe.CanConnectAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(true);
            var catalog = new FakeLauncherContentCatalog
            {
                RepositoryModificationNames = new[] { "Contra" },
                InitializationHandler = (request, _) =>
                {
                    initializationRequest = request;
                    return Task.CompletedTask;
                }
            };
            LauncherRuntimeContext runtimeContext = CreateRuntimeContext();
            InitWindowViewModel viewModel = CreateViewModel(
                pathResolver,
                launchPreparationService,
                connectionProbe,
                catalog,
                runtimeContext,
                new FakeStartupDialogService());
            InitWindowStartupCompletedEventArgs? completedArgs = null;
            viewModel.StartupCompleted += (_, args) => completedArgs = args;

            await viewModel.StartAsync();

            completedArgs.Should().NotBeNull();
            completedArgs!.Connected.Should().BeTrue();
            runtimeContext.Connected.Should().BeTrue();
            runtimeContext.CurrentlyManagedGame.Should().Be(SupportedGame.ZeroHour);
            initializationRequest.Should().NotBeNull();
            initializationRequest!.RemoteManifestUri.Should()
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
            InitWindowViewModel viewModel = CreateViewModel(
                Substitute.For<ILauncherPathResolver>(),
                launchPreparationService,
                Substitute.For<IRemoteConnectionProbe>(),
                new FakeLauncherContentCatalog(),
                runtimeContext,
                new FakeStartupDialogService(retryRecovery: false));
            bool completed = false;
            bool shutdownRequested = false;
            viewModel.StartupCompleted += (_, _) => completed = true;
            viewModel.ShutdownRequested += (_, _) => shutdownRequested = true;

            await viewModel.StartAsync();

            completed.Should().BeFalse();
            shutdownRequested.Should().BeTrue();
            runtimeContext.Connected.Should().BeFalse();
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
            FakeStartupDialogService startupDialogService = new();
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
            startupDialogService.ThemedTitle.Should().Be("Information");
            startupDialogService.ThemedMessage.Should().Be("Cannot connect");
            startupDialogService.ThemedColors.Should().BeSameAs(runtimeContext.Colors);
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
            FakeStartupDialogService startupDialogService = new();
            InitWindowViewModel viewModel = CreateViewModel(
                Substitute.For<ILauncherPathResolver>(),
                launchPreparationService,
                connectionProbe,
                catalog,
                CreateRuntimeContext(),
                startupDialogService);

            bool connected = await viewModel.PrepareLauncherAsync();

            connected.Should().BeFalse();
            startupDialogService.RetryCancelWarningCount.Should().Be(1);
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
                ModificationType = ModificationType.Mod,
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
                new FakeStartupDialogService());

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
            FakeStartupDialogService startupDialogService = new();
            InitWindowViewModel viewModel = CreateViewModel(
                Substitute.For<ILauncherPathResolver>(),
                launchPreparationService,
                connectionProbe,
                catalog,
                CreateRuntimeContext(),
                startupDialogService);

            bool connected = await viewModel.PrepareLauncherAsync();

            connected.Should().BeFalse();
            startupDialogService.ThemedTitle.Should().Be("Information");
            startupDialogService.ThemedMessage.Should().Be("Cannot connect");
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
            new TestStringLocalizer(new Dictionary<string, string>
            {
                ["CannotConnect"] = "Cannot connect",
                ["DeploymentRecoveryFailed"] = "Deployment recovery failed",
                ["Info"] = "Information",
            }),
            startupDialogService);
    }

    private static LauncherPaths CreateLauncherPaths()
    {
        return TestLauncherPaths.Create();
    }

    private static LauncherRuntimeContext CreateRuntimeContext()
    {
        return new LauncherRuntimeContext(
            TestLauncherPaths.CreateRuntimePathContext(CreateLauncherPaths()),
            "1.2.3");
    }

    private sealed class FakeStartupDialogService : IStartupDialogService
    {
        private readonly bool _retryRecovery;

        public FakeStartupDialogService(bool retryRecovery = true)
        {
            _retryRecovery = retryRecovery;
        }

        public string? ThemedTitle { get; private set; }

        public string? ThemedMessage { get; private set; }

        public ColorsInfo? ThemedColors { get; private set; }

        public int RetryCancelWarningCount { get; private set; }

        public Task ShowMessageAsync(string message)
        {
            return Task.CompletedTask;
        }

        public Task ShowThemedMessageAsync(string title, string message, ColorsInfo colors)
        {
            ThemedTitle = title;
            ThemedMessage = message;
            ThemedColors = colors;
            return Task.CompletedTask;
        }

        public Task<bool> ShowRetryCancelWarningAsync(string title, string message)
        {
            RetryCancelWarningCount++;
            return Task.FromResult(_retryRecovery);
        }
    }
}
