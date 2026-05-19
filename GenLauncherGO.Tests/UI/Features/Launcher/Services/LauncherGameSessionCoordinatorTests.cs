using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Remote;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Core.Startup.Models;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Shared.Localization;
using GenLauncherGO.UI.Shared.Themes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Services;

public sealed class LauncherGameSessionCoordinatorTests
{
    private const string ExecutableDirectory = @"C:\Tools\GenLauncherGO";
    private const string OriginalZeroHourDirectory = @"C:\Games\ZeroHour";
    private const string ReplacementZeroHourDirectory = @"D:\Games\ZeroHour";
    private const string GeneralsDirectory = @"C:\Games\Generals";

    [Fact]
    public void ApplyInstallationChangesSwitchesTheActivePathWithoutStartingAnotherProcess()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherInstallations previousInstallations = new()
            {
                ZeroHour = OriginalZeroHourDirectory,
            };
            LauncherPreferences preferences = new()
            {
                Installations = new LauncherInstallations
                {
                    ZeroHour = ReplacementZeroHourDirectory,
                },
                LastSelectedGame = SupportedGame.ZeroHour,
            };
            using SessionTestContext context = new(preferences);
            LauncherPaths replacementPaths = context.StoragePaths.CreateGamePaths(
                SupportedGame.ZeroHour,
                ReplacementZeroHourDirectory);
            context.ConfigureSuccessfulSwitch(SupportedGame.ZeroHour, replacementPaths);

            bool applied = await context.Coordinator.ApplyInstallationChangesAsync(
                previousInstallations,
                SupportedGame.ZeroHour,
                context.Owner,
                CancellationToken.None);

            applied.Should().BeTrue();
            context.RuntimePaths.ActivePaths.Should().Be(replacementPaths);
            context.PathResolver.Received(1).PrepareGameDirectories(replacementPaths, true);
            context.PreparationService.Received(1).Cleanup(
                context.OriginalPaths,
                Arg.Any<CancellationToken>());
            context.PreparationService.Received(1).Recover(
                replacementPaths,
                Arg.Any<CancellationToken>());
            context.Catalog.InitializationRequests.Should().ContainSingle()
                .Which.Paths.Should().Be(replacementPaths);
            await context.ProcessLauncher.DidNotReceiveWithAnyArgs()
                .StartAsync(default!, default);
        });
    }

    [Fact]
    public void SwitchGamePersistsTheSelectedGameAndInitializesItsCatalog()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPreferences preferences = new()
            {
                Installations = new LauncherInstallations
                {
                    Generals = GeneralsDirectory,
                    ZeroHour = OriginalZeroHourDirectory,
                },
                LastSelectedGame = SupportedGame.ZeroHour,
            };
            using SessionTestContext context = new(preferences);
            LauncherPaths generalsPaths = context.StoragePaths.CreateGamePaths(
                SupportedGame.Generals,
                GeneralsDirectory);
            context.ConfigureSuccessfulSwitch(SupportedGame.Generals, generalsPaths);

            bool switched = await context.Coordinator.SwitchGameAsync(
                SupportedGame.Generals,
                context.Owner,
                CancellationToken.None);

            switched.Should().BeTrue();
            context.Preferences.Current.LastSelectedGame.Should().Be(SupportedGame.Generals);
            context.Preferences.Updates.Should().ContainSingle()
                .Which.LastSelectedGame.Should().Be(SupportedGame.Generals);
            context.RuntimePaths.ActivePaths.Should().Be(generalsPaths);
            context.Catalog.InitializationRequests.Should().ContainSingle()
                .Which.Paths.Game.Should().Be(SupportedGame.Generals);
        });
    }

    [Fact]
    public void ActivePackageWorkBlocksInstallationChangesAndRestoresSavedPreferences()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherInstallations previousInstallations = new()
            {
                ZeroHour = OriginalZeroHourDirectory,
            };
            LauncherPreferences preferences = new()
            {
                Installations = new LauncherInstallations
                {
                    ZeroHour = ReplacementZeroHourDirectory,
                },
                LastSelectedGame = SupportedGame.ZeroHour,
            };
            using SessionTestContext context = new(preferences);
            context.PackageActivity.TryBegin("download", out LauncherPackageActivityService.LauncherPackageActivityLease? lease)
                .Should().BeTrue();
            using (lease)
            {
                bool applied = await context.Coordinator.ApplyInstallationChangesAsync(
                    previousInstallations,
                    SupportedGame.ZeroHour,
                    context.Owner,
                    CancellationToken.None);

                applied.Should().BeFalse();
            }

            context.Preferences.Current.Installations.Should().Be(previousInstallations);
            context.Preferences.Current.LastSelectedGame.Should().Be(SupportedGame.ZeroHour);
            context.RuntimePaths.ActivePaths.Should().Be(context.OriginalPaths);
            context.Catalog.InitializationRequests.Should().BeEmpty();
            context.Owner.Resources.Count.Should().Be(0);
            await context.DialogService.Received(1).ShowInfoAsync(
                Arg.Any<GenLauncherGO.UI.Features.Dialogs.Models.LauncherInfoDialogRequest>(),
                context.Owner);
        });
    }

    [Fact]
    public void FailedGameSwitchRestoresPreferencesPathsThemeAndPreviousCatalog()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPreferences preferences = new()
            {
                Installations = new LauncherInstallations
                {
                    Generals = GeneralsDirectory,
                    ZeroHour = OriginalZeroHourDirectory,
                },
                LastSelectedGame = SupportedGame.ZeroHour,
            };
            using SessionTestContext context = new(preferences);
            LauncherPaths generalsPaths = context.StoragePaths.CreateGamePaths(
                SupportedGame.Generals,
                GeneralsDirectory);
            context.ConfigureSuccessfulSwitch(SupportedGame.Generals, generalsPaths);
            context.Catalog.InitializationHandler = (request, _) =>
                request.Paths.Game == SupportedGame.Generals
                    ? Task.FromException(new InvalidOperationException("catalog failed"))
                    : Task.CompletedTask;
            ColorsInfo originalColors = context.RuntimeContext.Colors;
            context.RuntimeContext.ModsRepositoryUrl = "https://old.example/catalog.yml";
            context.RuntimeContext.Connected = true;

            bool switched = await context.Coordinator.SwitchGameAsync(
                SupportedGame.Generals,
                context.Owner,
                CancellationToken.None);

            switched.Should().BeFalse();
            context.Preferences.Updates.Select(update => update.LastSelectedGame)
                .Should().Equal(SupportedGame.Generals, SupportedGame.ZeroHour);
            context.Preferences.Current.LastSelectedGame.Should().Be(SupportedGame.ZeroHour);
            context.Preferences.Current.Installations.Should().Be(preferences.Installations);
            context.RuntimePaths.ActivePaths.Should().Be(context.OriginalPaths);
            context.RuntimeContext.Colors.Should().BeSameAs(originalColors);
            context.RuntimeContext.ModsRepositoryUrl.Should().Be("https://old.example/catalog.yml");
            context.RuntimeContext.Connected.Should().BeTrue();
            context.Catalog.InitializationRequests.Select(request => request.Paths)
                .Should().Equal(generalsPaths, context.OriginalPaths);
            context.Owner.Resources["GenLauncherBorderColor"]
                .Should().Be(originalColors.GenLauncherBorderColor);
            await context.DialogService.Received(1).ShowErrorAsync(
                Arg.Any<GenLauncherGO.UI.Features.Dialogs.Models.LauncherInfoDialogRequest>(),
                context.Owner);
        });
    }

    [Fact]
    public void CanceledGameSwitchRestoresPreferencesRuntimePathsAndPreviousCatalog()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPreferences preferences = new()
            {
                Installations = new LauncherInstallations
                {
                    Generals = GeneralsDirectory,
                    ZeroHour = OriginalZeroHourDirectory,
                },
                LastSelectedGame = SupportedGame.ZeroHour,
            };
            using SessionTestContext context = new(preferences);
            LauncherPaths generalsPaths = context.StoragePaths.CreateGamePaths(
                SupportedGame.Generals,
                GeneralsDirectory);
            context.ConfigureSuccessfulSwitch(SupportedGame.Generals, generalsPaths);
            using var cancellation = new CancellationTokenSource();
            context.Catalog.InitializationHandler = (request, _) =>
            {
                if (request.Paths.Game != SupportedGame.Generals)
                {
                    return Task.CompletedTask;
                }

                cancellation.Cancel();
                return Task.FromCanceled(cancellation.Token);
            };

            Func<Task> act = () => context.Coordinator.SwitchGameAsync(
                SupportedGame.Generals,
                context.Owner,
                cancellation.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            context.Preferences.Updates.Select(update => update.LastSelectedGame)
                .Should().Equal(SupportedGame.Generals, SupportedGame.ZeroHour);
            context.Preferences.Current.LastSelectedGame.Should().Be(SupportedGame.ZeroHour);
            context.RuntimePaths.ActivePaths.Should().Be(context.OriginalPaths);
            context.Catalog.InitializationRequests.Select(request => request.Paths)
                .Should().Equal(generalsPaths, context.OriginalPaths);
            await context.DialogService.DidNotReceiveWithAnyArgs().ShowErrorAsync(default!, default);
        });
    }

    [Fact]
    public void GameSwitchIsBlockedThroughoutIntegrityVerification()
    {
        StaTestRunner.Run(async () =>
        {
            ILaunchContentIntegrityResolutionService resolutionService =
                Substitute.For<ILaunchContentIntegrityResolutionService>();
            var verificationStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var verificationCompletion =
                new TaskCompletionSource<LaunchContentIntegrityVerificationResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            resolutionService.VerifyAsync(
                    Arg.Any<LaunchContentIntegrityTargetRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    verificationStarted.TrySetResult();
                    return verificationCompletion.Task;
                });
            LauncherPreferences preferences = new()
            {
                Installations = new LauncherInstallations
                {
                    Generals = GeneralsDirectory,
                    ZeroHour = OriginalZeroHourDirectory,
                },
                LastSelectedGame = SupportedGame.ZeroHour,
            };
            using SessionTestContext context = new(preferences, resolutionService);
            Task<LauncherLaunchResult> launchTask = context.LaunchCoordinator.LaunchAsync(
                new LauncherLaunchRequest(
                    GameLaunchTargetKind.GameClient,
                    "generalszh.exe",
                    useGeneralsOnline: false,
                    Array.Empty<LauncherContentVersion>()),
                Array.Empty<ILaunchContentIntegrityProgressTarget>(),
                context.Owner,
                CancellationToken.None);
            await verificationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            bool switched = await context.Coordinator.SwitchGameAsync(
                SupportedGame.Generals,
                context.Owner,
                CancellationToken.None);

            switched.Should().BeFalse();
            context.Preferences.Current.LastSelectedGame.Should().Be(SupportedGame.ZeroHour);
            context.RuntimePaths.ActivePaths.Should().Be(context.OriginalPaths);
            await context.DialogService.Received(1).ShowInfoAsync(
                Arg.Any<GenLauncherGO.UI.Features.Dialogs.Models.LauncherInfoDialogRequest>(),
                context.Owner);

            verificationCompletion.SetCanceled();
            LauncherLaunchResult launchResult = await launchTask;
            launchResult.LaunchStarted.Should().BeFalse();
            launchResult.FailureKind.Should().Be(LauncherLaunchFailureKind.VerificationCanceled);
            context.LaunchCoordinator.IsLaunchInProgress.Should().BeFalse();
        });
    }

    private sealed class SessionTestContext : IDisposable
    {
        public SessionTestContext(
            LauncherPreferences preferences,
            ILaunchContentIntegrityResolutionService? integrityResolutionService = null)
        {
            StoragePaths = new LauncherStoragePaths(ExecutableDirectory);
            OriginalPaths = StoragePaths.CreateGamePaths(
                SupportedGame.ZeroHour,
                OriginalZeroHourDirectory);
            RuntimePaths = new LauncherRuntimePathContext(StoragePaths, OriginalPaths);
            RuntimeContext = new LauncherRuntimeContext(RuntimePaths, "1.0")
            {
                Colors = TestLauncherTheme.Create(),
            };
            Preferences = new RecordingPreferencesService(preferences);
            InstallationService = Substitute.For<IGameInstallationService>();
            PathResolver = Substitute.For<ILauncherPathResolver>();
            PreparationService = Substitute.For<ILaunchPreparationService>();
            ConnectionProbe = Substitute.For<IRemoteConnectionProbe>();
            ConnectionProbe.CanConnectAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(false);
            Catalog = new FakeLauncherContentCatalog();
            PackageActivity = new LauncherPackageActivityService();
            ProcessLauncher = Substitute.For<IGameProcessLauncher>();
            DialogService = Substitute.For<ILauncherDialogService>();
            LaunchCoordinator = TestLauncherLaunchCoordinator.Create(
                packageActivityService: PackageActivity,
                preferencesService: Preferences,
                catalog: Catalog,
                processLauncher: ProcessLauncher,
                dialogService: DialogService,
                integrityResolutionService: integrityResolutionService);
            Owner = new Window();

            Coordinator = new LauncherGameSessionCoordinator(
                RuntimeContext,
                Preferences,
                InstallationService,
                PathResolver,
                PreparationService,
                ConnectionProbe,
                Catalog,
                PackageActivity,
                LaunchCoordinator,
                DialogService,
                CreateStringLocalizer(),
                NullLogger<LauncherGameSessionCoordinator>.Instance);
        }

        public LauncherStoragePaths StoragePaths { get; }

        public LauncherPaths OriginalPaths { get; }

        public LauncherRuntimePathContext RuntimePaths { get; }

        public LauncherRuntimeContext RuntimeContext { get; }

        public RecordingPreferencesService Preferences { get; }

        public IGameInstallationService InstallationService { get; }

        public ILauncherPathResolver PathResolver { get; }

        public ILaunchPreparationService PreparationService { get; }

        public IRemoteConnectionProbe ConnectionProbe { get; }

        public FakeLauncherContentCatalog Catalog { get; }

        public LauncherPackageActivityService PackageActivity { get; }

        public IGameProcessLauncher ProcessLauncher { get; }

        public ILauncherDialogService DialogService { get; }

        public LauncherLaunchCoordinator LaunchCoordinator { get; }

        public Window Owner { get; }

        public LauncherGameSessionCoordinator Coordinator { get; }

        public void ConfigureSuccessfulSwitch(SupportedGame game, LauncherPaths paths)
        {
            InstallationService.Validate(
                    game,
                    paths.GameDirectory,
                    StoragePaths.ExecutableDirectory)
                .Returns(GameInstallationValidationResult.Valid(paths.GameDirectory));
            PreparationService.Cleanup(OriginalPaths, Arg.Any<CancellationToken>())
                .Returns(true);
            PreparationService.Recover(paths, Arg.Any<CancellationToken>())
                .Returns(true);
        }

        public void Dispose()
        {
            Owner.Close();
        }

        private static ILauncherStringLocalizer CreateStringLocalizer()
        {
            return new TestStringLocalizer(new Dictionary<string, string>
            {
                ["DeploymentRecoveryFailed"] = "Deployment recovery failed.",
                ["GameSwitchBlockedDetails"] = "Wait for active work to finish.",
                ["GameSwitchBlockedTitle"] = "Game switch blocked",
                ["GameSwitchFailedDetails"] = "Could not switch: {0}",
                ["GameSwitchFailedTitle"] = "Game switch failed",
                ["InstallationPathUnavailable"] = "The installation path is unavailable.",
                ["PreferencesSaveFailedDetails"] = "Preferences could not be saved.",
            });
        }

    }

    private sealed class RecordingPreferencesService : ILauncherPreferencesService
    {
        public RecordingPreferencesService(LauncherPreferences current)
        {
            Current = current;
        }

        public event EventHandler<LauncherPreferences>? PreferencesChanged;

        public LauncherPreferences Current { get; private set; }

        public List<LauncherPreferences> Updates { get; } = new();

        public void Update(LauncherPreferences preferences)
        {
            Current = preferences;
            Updates.Add(preferences);
            PreferencesChanged?.Invoke(
                this,
                preferences);
        }
    }
}
