using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Remote;
using GenLauncherGO.Core.Settings.Exceptions;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Core.Startup.Models;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Shared.Localization;
using GenLauncherGO.UI.Shared.Themes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Services;

[Collection("Avalonia")]
public sealed class LauncherGameSessionCoordinatorTests
{
    private const string ExecutableDirectory = @"C:\Tools\GenLauncherGO";
    private const string OriginalZeroHourDirectory = @"C:\Games\ZeroHour";
    private const string ReplacementZeroHourDirectory = @"D:\Games\ZeroHour";
    private const string GeneralsDirectory = @"C:\Games\Generals";

    [Fact]
    public void ApplyInstallation_ChangesSwitchesTheActivePathWithoutStartingAnotherProcess()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherInstallations previousInstallations = new()
            {
                ZeroHour = OriginalZeroHourDirectory
            };
            LauncherPreferences preferences = CreatePreferences(zeroHour: ReplacementZeroHourDirectory);
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
    public void SwitchGame_PersistsTheSelectedGameAndInitializesItsCatalog()
    {
        StaTestRunner.Run(async () =>
        {
            using ApplicationThemeScope themeScope = new();
            LauncherPreferences preferences = CreatePreferences(GeneralsDirectory, OriginalZeroHourDirectory);
            using SessionTestContext context = new(preferences);
            LauncherPaths generalsPaths = context.StoragePaths.CreateGamePaths(
                SupportedGame.Generals,
                GeneralsDirectory);
            context.ConfigureSuccessfulSwitch(SupportedGame.Generals, generalsPaths);
            context.ConnectionProbe.CanConnectAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(true);
            context.Catalog.RepositoryModificationNames = ["Shockwave"];

            bool switched = await context.Coordinator.SwitchGameAsync(
                SupportedGame.Generals,
                context.Owner,
                CancellationToken.None);

            var generalsRepository = new Uri(
                LauncherApplicationDefaults.GeneralsRepositoryUrl,
                UriKind.Absolute);
            ColorsInfo generalsTheme = LauncherThemePresets.Create(SupportedGame.Generals);
            switched.Should().BeTrue();
            context.Preferences.Current.LastSelectedGame.Should().Be(SupportedGame.Generals);
            context.Preferences.Updates.Should().ContainSingle()
                .Which.LastSelectedGame.Should().Be(SupportedGame.Generals);
            context.RuntimePaths.ActivePaths.Should().Be(generalsPaths);
            context.RuntimeContext.ModsRepositoryUrl.Should()
                .Be(LauncherApplicationDefaults.GeneralsRepositoryUrl);
            context.RuntimeContext.Colors.GenLauncherBorderColor.Color.Should()
                .Be(generalsTheme.GenLauncherBorderColor.Color);
            context.RuntimeContext.Colors.ListSelectionStartColor.Should()
                .Be(generalsTheme.ListSelectionStartColor);
            context.RuntimeContext.Connected.Should().BeTrue();
            await context.ConnectionProbe.Received(1).CanConnectAsync(
                generalsRepository,
                Arg.Any<CancellationToken>());
            LauncherContentCatalogInitializationRequest initialization =
                context.Catalog.InitializationRequests.Should().ContainSingle().Subject;
            initialization.Paths.Should().Be(generalsPaths);
            initialization.RemoteManifestUri.Should().Be(generalsRepository);
        });
    }

    /// <summary>
    ///     Reaching the repository is not enough: without a modification list there is nothing to browse, so the
    ///     session has to fall back to its offline presentation.
    /// </summary>
    [Fact]
    public void SwitchGame_WhenTheRepositoryPublishesNoModifications_LeavesTheSessionDisconnected()
    {
        StaTestRunner.Run(async () =>
        {
            using ApplicationThemeScope themeScope = new();
            LauncherPreferences preferences = CreatePreferences(GeneralsDirectory, OriginalZeroHourDirectory);
            using SessionTestContext context = new(preferences);
            LauncherPaths generalsPaths = context.StoragePaths.CreateGamePaths(
                SupportedGame.Generals,
                GeneralsDirectory);
            context.ConfigureSuccessfulSwitch(SupportedGame.Generals, generalsPaths);
            context.ConnectionProbe.CanConnectAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(true);
            context.Catalog.RepositoryModificationNames = null;

            bool switched = await context.Coordinator.SwitchGameAsync(
                SupportedGame.Generals,
                context.Owner,
                CancellationToken.None);

            switched.Should().BeTrue();
            context.RuntimeContext.Connected.Should().BeFalse();
            context.Catalog.InitializationRequests.Should().ContainSingle()
                .Which.RemoteManifestUri.Should().Be(
                    new Uri(LauncherApplicationDefaults.GeneralsRepositoryUrl, UriKind.Absolute));
        });
    }

    /// <summary>
    ///     Windows paths differ freely in case and in a trailing separator; treating those as a new installation
    ///     would tear down and rebuild the running session for no reason.
    /// </summary>
    [Fact]
    public void ApplyInstallationChanges_WhenOnlyThePathSpellingChanged_KeepsTheRunningSession()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherInstallations previousInstallations = new()
            {
                ZeroHour = OriginalZeroHourDirectory
            };
            LauncherPreferences preferences = CreatePreferences(
                zeroHour: OriginalZeroHourDirectory.ToLowerInvariant() + @"\");
            using SessionTestContext context = new(preferences);

            bool applied = await context.Coordinator.ApplyInstallationChangesAsync(
                previousInstallations,
                SupportedGame.ZeroHour,
                context.Owner,
                CancellationToken.None);

            applied.Should().BeTrue();
            context.RuntimePaths.ActivePaths.Should().Be(context.OriginalPaths);
            context.Preferences.Updates.Should().BeEmpty();
            context.PreparationService.DidNotReceiveWithAnyArgs().Cleanup(default!, default);
            context.PathResolver.DidNotReceiveWithAnyArgs().PrepareGameDirectories(default!, default);
            context.Catalog.InitializationRequests.Should().BeEmpty();
        });
    }

    [Fact]
    public void ApplyInstallationChanges_WhenOnlyTheSelectedGameChanged_KeepsTheNewSelectionWithoutReloading()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherInstallations installations = new()
            {
                Generals = GeneralsDirectory,
                ZeroHour = OriginalZeroHourDirectory
            };
            LauncherPreferences preferences = new()
            {
                Installations = installations,
                LastSelectedGame = SupportedGame.ZeroHour
            };
            using SessionTestContext context = new(preferences);

            bool applied = await context.Coordinator.ApplyInstallationChangesAsync(
                installations,
                SupportedGame.Generals,
                context.Owner,
                CancellationToken.None);

            applied.Should().BeTrue();
            context.Preferences.Current.LastSelectedGame.Should().Be(SupportedGame.ZeroHour);
            context.Preferences.Updates.Should().BeEmpty();
            context.RuntimePaths.ActivePaths.Should().Be(context.OriginalPaths);
            context.Catalog.InitializationRequests.Should().BeEmpty();
        });
    }

    [Fact]
    public void ApplyInstallationChanges_WhenTheNewInstallationIsInvalid_RestoresThePreviousInstallations()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherInstallations previousInstallations = new()
            {
                ZeroHour = OriginalZeroHourDirectory
            };
            LauncherPreferences preferences = CreatePreferences(zeroHour: ReplacementZeroHourDirectory);
            using SessionTestContext context = new(preferences);
            context.InstallationService.Validate(
                    SupportedGame.ZeroHour,
                    ReplacementZeroHourDirectory,
                    ExecutableDirectory)
                .Returns(GameInstallationValidationResult.Invalid(
                    GameInstallationValidationFailure.PathMissing));

            bool applied = await context.Coordinator.ApplyInstallationChangesAsync(
                previousInstallations,
                SupportedGame.ZeroHour,
                context.Owner,
                CancellationToken.None);

            applied.Should().BeFalse();
            context.Preferences.Current.Installations.Should().Be(previousInstallations);
            context.RuntimePaths.ActivePaths.Should().Be(context.OriginalPaths);
            context.Catalog.SaveCount.Should().Be(0);
            context.Catalog.InitializationRequests.Should().BeEmpty();
            await context.DialogService.Received(1).ShowErrorAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request.DetailMessage == "Could not switch: The installation path is unavailable."),
                context.Owner);
        });
    }

    [Fact]
    public void SwitchGame_WhenPreferencesPersistenceFails_LeavesSessionUntouchedAndShowsError()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPreferences preferences = CreatePreferences(GeneralsDirectory, OriginalZeroHourDirectory);
            var persistenceFailure = new LauncherPreferencesPersistenceException(
                new InvalidOperationException("preferences write failed"));
            using SessionTestContext context = new(
                preferences,
                preferencesUpdateFailure: persistenceFailure);
            LauncherPaths generalsPaths = context.StoragePaths.CreateGamePaths(
                SupportedGame.Generals,
                GeneralsDirectory);
            context.ConfigureSuccessfulSwitch(SupportedGame.Generals, generalsPaths);
            ColorsInfo originalColors = context.RuntimeContext.Colors;

            bool switched = await context.Coordinator.SwitchGameAsync(
                SupportedGame.Generals,
                context.Owner,
                CancellationToken.None);

            switched.Should().BeFalse();
            context.Preferences.Current.Should().BeSameAs(preferences);
            context.Preferences.Updates.Should().BeEmpty();
            context.RuntimePaths.ActivePaths.Should().Be(context.OriginalPaths);
            context.RuntimeContext.Colors.Should().BeSameAs(originalColors);
            context.Catalog.SaveCount.Should().Be(0);
            context.Catalog.InitializationRequests.Should().BeEmpty();
            context.PathResolver.DidNotReceiveWithAnyArgs().PrepareGameDirectories(default!, default);
            context.PreparationService.DidNotReceiveWithAnyArgs().Cleanup(default!, default);
            await context.DialogService.Received(1).ShowErrorAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request.DetailMessage == "Could not switch: Preferences could not be saved."),
                context.Owner);
        });
    }

    [Theory]
    [InlineData("cleanup")]
    [InlineData("recovery")]
    public void SwitchGame_WhenPreparationPhaseReturnsFalse_RollsBackCompletedPhasesAndShowsError(
        string failedPhase)
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPreferences preferences = CreatePreferences(GeneralsDirectory, OriginalZeroHourDirectory);
            using SessionTestContext context = new(preferences);
            LauncherPaths generalsPaths = context.StoragePaths.CreateGamePaths(
                SupportedGame.Generals,
                GeneralsDirectory);
            context.ConfigureSuccessfulSwitch(SupportedGame.Generals, generalsPaths);
            switch (failedPhase)
            {
                case "cleanup":
                    context.PreparationService.Cleanup(
                            context.OriginalPaths,
                            Arg.Any<CancellationToken>())
                        .Returns(false);
                    break;
                case "recovery":
                    context.PreparationService.Recover(
                            generalsPaths,
                            Arg.Any<CancellationToken>())
                        .Returns(false);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown failure phase '{failedPhase}'.");
            }

            ColorsInfo originalColors = context.RuntimeContext.Colors;

            bool switched = await context.Coordinator.SwitchGameAsync(
                SupportedGame.Generals,
                context.Owner,
                CancellationToken.None);

            switched.Should().BeFalse();
            context.Preferences.Updates.Select(update => update.LastSelectedGame)
                .Should().Equal(SupportedGame.Generals, SupportedGame.ZeroHour);
            context.Preferences.Current.Should().Be(preferences);
            context.RuntimePaths.ActivePaths.Should().Be(context.OriginalPaths);
            context.RuntimeContext.Colors.Should().BeSameAs(originalColors);
            context.Catalog.SaveCount.Should().Be(1);
            LauncherPaths[] expectedCatalogReloads = failedPhase == "recovery"
                ? [context.OriginalPaths]
                : [];
            context.Catalog.InitializationRequests.Select(request => request.Paths)
                .Should().Equal(expectedCatalogReloads);
            await context.DialogService.Received(1).ShowErrorAsync(
                Arg.Is<LauncherInfoDialogRequest>(request =>
                    request.DetailMessage == "Could not switch: Deployment recovery failed."),
                context.Owner);
        });
    }

    [Fact]
    public void ActivePackageWorkBlocksInstallation_ChangesAndRestoresSavedPreferences()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherInstallations previousInstallations = new()
            {
                ZeroHour = OriginalZeroHourDirectory
            };
            LauncherPreferences preferences = CreatePreferences(zeroHour: ReplacementZeroHourDirectory);
            using SessionTestContext context = new(preferences);
            context.PackageActivity.TryBegin("download",
                    out LauncherPackageActivityService.LauncherPackageActivityLease? lease)
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
                Arg.Any<LauncherInfoDialogRequest>(),
                context.Owner);
        });
    }

    /// <summary>
    ///     A paused download is already stopped and keeps its partial content, so switching game suspends it and
    ///     goes ahead rather than telling the user the launcher is busy.
    /// </summary>
    [Fact]
    public void PausedDownload_SuspendsAndAllowsTheInstallationChange()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherInstallations previousInstallations = new()
            {
                ZeroHour = OriginalZeroHourDirectory
            };
            LauncherPreferences preferences = CreatePreferences(zeroHour: ReplacementZeroHourDirectory);
            using SessionTestContext context = new(preferences);
            context.ConfigureSuccessfulSwitch(
                SupportedGame.ZeroHour,
                context.StoragePaths.CreateGamePaths(SupportedGame.ZeroHour, ReplacementZeroHourDirectory));
            Task<PackageDownloadResult> pausedDownload = TestPackageDownload.StartPaused(context.PackageActivity);

            bool applied = await context.Coordinator.ApplyInstallationChangesAsync(
                previousInstallations,
                SupportedGame.ZeroHour,
                context.Owner,
                CancellationToken.None);

            (await pausedDownload).Status.Should().Be(PackageDownloadStatus.Suspended);
            applied.Should().BeTrue();
            await context.DialogService.DidNotReceive().ShowInfoAsync(
                Arg.Any<LauncherInfoDialogRequest>(),
                context.Owner);
        });
    }

    [Fact]
    public void FailedGameSwitch_RestoresPreferencesPathsThemeAndPreviousCatalog()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPreferences preferences = CreatePreferences(GeneralsDirectory, OriginalZeroHourDirectory);
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
            Application.Current!.Resources["GenLauncherBorderColor"]
                .Should().Be(originalColors.GenLauncherBorderColor);
            await context.DialogService.Received(1).ShowErrorAsync(
                Arg.Any<LauncherInfoDialogRequest>(),
                context.Owner);
        });
    }

    [Fact]
    public void CanceledGameSwitch_RestoresPreferencesRuntimePathsAndPreviousCatalog()
    {
        StaTestRunner.Run(async () =>
        {
            LauncherPreferences preferences = CreatePreferences(GeneralsDirectory, OriginalZeroHourDirectory);
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
            await context.DialogService.DidNotReceiveWithAnyArgs().ShowErrorAsync(default!);
        });
    }

    [Fact]
    public void GameSwitch_IsBlockedThroughoutIntegrityVerification()
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
            LauncherPreferences preferences = CreatePreferences(GeneralsDirectory, OriginalZeroHourDirectory);
            using SessionTestContext context = new(preferences, resolutionService);
            Task<LauncherLaunchResult> launchTask = context.LaunchCoordinator.LaunchAsync(
                new LauncherLaunchRequest(
                    GameLaunchTargetKind.GameClient,
                    "generalszh.exe",
                    false,
                    Array.Empty<LauncherContentVersion>()),
                Array.Empty<ILaunchContentIntegrityProgressTarget>(),
                context.Owner,
                CancellationToken.None);
            await verificationStarted.Task.WaitAsync(TestTimeouts.Wait);

            bool switched = await context.Coordinator.SwitchGameAsync(
                SupportedGame.Generals,
                context.Owner,
                CancellationToken.None);

            switched.Should().BeFalse();
            context.Preferences.Current.LastSelectedGame.Should().Be(SupportedGame.ZeroHour);
            context.RuntimePaths.ActivePaths.Should().Be(context.OriginalPaths);
            await context.DialogService.Received(1).ShowInfoAsync(
                Arg.Any<LauncherInfoDialogRequest>(),
                context.Owner);

            verificationCompletion.SetCanceled();
            LauncherLaunchResult launchResult = await launchTask;
            launchResult.LaunchStarted.Should().BeFalse();
            launchResult.FailureKind.Should().Be(LauncherLaunchFailureKind.VerificationCanceled);
            context.LaunchCoordinator.IsLaunchInProgress.Should().BeFalse();
        });
    }

    /// <summary>
    ///     Builds the saved preferences a session starts from, leaving each test to state only which
    ///     installations are configured.
    /// </summary>
    private static LauncherPreferences CreatePreferences(string? generals = null, string? zeroHour = null)
    {
        return new LauncherPreferences
        {
            Installations = new LauncherInstallations
            {
                Generals = generals,
                ZeroHour = zeroHour
            },
            LastSelectedGame = SupportedGame.ZeroHour
        };
    }

    private sealed class SessionTestContext : IDisposable
    {
        public SessionTestContext(
            LauncherPreferences preferences,
            ILaunchContentIntegrityResolutionService? integrityResolutionService = null,
            LauncherPreferencesPersistenceException? preferencesUpdateFailure = null)
        {
            StoragePaths = new LauncherStoragePaths(ExecutableDirectory);
            OriginalPaths = StoragePaths.CreateGamePaths(
                SupportedGame.ZeroHour,
                OriginalZeroHourDirectory);
            RuntimePaths = new LauncherRuntimePathContext(StoragePaths, OriginalPaths);
            RuntimeContext = new LauncherRuntimeContext(RuntimePaths, "1.0")
            {
                Colors = TestLauncherTheme.Create()
            };
            Preferences = new RecordingLauncherPreferencesService(preferences)
            {
                UpdateFailure = preferencesUpdateFailure
            };
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
                PackageActivity,
                Preferences,
                Catalog,
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

        public RecordingLauncherPreferencesService Preferences { get; }

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

        public void Dispose()
        {
            Owner.Close();
        }

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

        private static ILauncherStringLocalizer CreateStringLocalizer()
        {
            return FakeStringLocalizer.Create(TestLocalizedStrings.Launch);
        }
    }
}
