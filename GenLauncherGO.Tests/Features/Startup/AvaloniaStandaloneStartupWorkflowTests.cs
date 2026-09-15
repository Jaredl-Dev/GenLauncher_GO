using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using GenLauncherGO.Features.Settings;
using GenLauncherGO.Features.Startup;
using GenLauncherGO.Features.Startup.Views;
using GenLauncherGO.Shared.Themes;

namespace GenLauncherGO.Tests.Features.Startup;

[Collection("Avalonia")]
public sealed class AvaloniaStandaloneStartupWorkflowTests
{
    [Theory]
    [InlineData(SupportedGame.Generals, SupportedGame.ZeroHour, @"C:\Games\Generals")]
    [InlineData(SupportedGame.ZeroHour, SupportedGame.Generals, @"C:\Games\Zero Hour")]
    public async Task RunAsync_OneValidInstallation_UsesCanonicalPathAndPersistsFallbackSelectionAsync(
        object availableGameValue,
        object unavailablePreferenceValue,
        string canonicalPath)
    {
        var availableGame = (SupportedGame)availableGameValue;
        var unavailablePreference = (SupportedGame)unavailablePreferenceValue;
        var storagePaths = new LauncherStoragePaths(@"C:\Launcher");
        const string ConfiguredPath = @"C:\Games\Alias";
        var installationService = new FakeGameInstallationService
        {
            ValidationRule = (game, _) => game == availableGame
                ? GameInstallationValidationResult.Valid(canonicalPath)
                : GameInstallationValidationResult.Invalid(
                    GameInstallationValidationFailure.DirectoryNotFound)
        };
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences
        {
            Installations = new LauncherInstallations().WithPath(availableGame, ConfiguredPath),
            LastSelectedGame = unavailablePreference
        });
        AvaloniaStandaloneStartupWorkflow workflow = CreateWorkflow(installationService);

        StandaloneStartupResult result = await workflow.RunAsync(storagePaths, preferencesService);

        result.CanStart.Should().BeTrue();
        result.Game.Should().Be(availableGame);
        result.GameDirectory.Should().Be(canonicalPath);
        installationService.ValidateCalls.Should().OnlyContain(
            call => call.ExecutableDirectory == storagePaths.ExecutableDirectory);
        preferencesService.Updates.Should().ContainSingle()
            .Which.LastSelectedGame.Should().Be(availableGame);
    }

    [Fact]
    public void RunAsync_OverlappingSavedFolders_RequiresCorrectionAndPreservesSelections()
    {
        StaTestRunner.Run(async () =>
        {
            var savedInstallations = new LauncherInstallations
            {
                Generals = @"C:\Games",
                ZeroHour = @"C:\Games\Zero Hour"
            };
            var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences
            {
                Installations = savedInstallations,
                LastSelectedGame = SupportedGame.Generals
            });
            AvaloniaStandaloneStartupWorkflow workflow = CreateWorkflow(new FakeGameInstallationService());
            using var themeScope = new ApplicationThemeScope();
            LauncherSetupViewModel? setup = null;
            using IDisposable subscription = Window.WindowOpenedEvent.AddClassHandler<LauncherSetupWindow>((window, _) =>
            {
                setup = (LauncherSetupViewModel)window.DataContext!;
                Dispatcher.UIThread.Post(window.Close);
            });

            StandaloneStartupResult result = await workflow.RunAsync(new LauncherStoragePaths(@"C:\Launcher"), preferencesService);

            result.CanStart.Should().BeFalse();
            setup.Should().NotBeNull();
            setup!.ContinueCommand.CanExecute(null).Should().BeFalse();
            preferencesService.Current.Installations.Should().Be(savedInstallations);
            preferencesService.Updates.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task RunAsync_FallbackSelectionCannotBePersisted_ShowsSettingsFailureAndCancelsAsync()
    {
        var storagePaths = new LauncherStoragePaths(@"C:\Launcher");
        const string CanonicalPath = @"C:\Games\Generals";
        var installationService = new FakeGameInstallationService
        {
            ValidationRule = (game, _) => game == SupportedGame.Generals
                ? GameInstallationValidationResult.Valid(CanonicalPath)
                : GameInstallationValidationResult.Invalid(
                    GameInstallationValidationFailure.DirectoryNotFound)
        };
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences
        {
            Installations = new LauncherInstallations { Generals = CanonicalPath },
            LastSelectedGame = SupportedGame.ZeroHour
        })
        {
            UpdateFailure = new LauncherPreferencesPersistenceException(new IOException("Disk unavailable."))
        };
        var startupDialogService = new RecordingStartupDialogService();
        AvaloniaStandaloneStartupWorkflow workflow = CreateWorkflow(
            installationService,
            startupDialogService);

        StandaloneStartupResult result = await workflow.RunAsync(storagePaths, preferencesService);

        result.CanStart.Should().BeFalse();
        preferencesService.Updates.Should().BeEmpty();
        startupDialogService.TitledMessages.Should().ContainSingle();
    }

    [Fact]
    public void ShowBlockingLauncherLocationAsync_LauncherInsideAGame_ShowsTheGameLocationBlocker()
    {
        StaTestRunner.Run(async () =>
        {
            var storagePaths = new LauncherStoragePaths(@"C:\Games\Generals\Launcher");
            var containingInstallation = new GameInstallationLocation(
                SupportedGame.Generals,
                @"C:\Games\Generals");
            IGameInstallationService installationService = Substitute.For<IGameInstallationService>();
            installationService.FindContainingInstallation(storagePaths.ExecutableDirectory)
                .Returns(containingInstallation);
            AvaloniaStandaloneStartupWorkflow workflow = CreateWorkflow(installationService);
            using ApplicationThemeScope themeScope = new();
            LauncherLocationWarningWindow? warningWindow = null;
            using IDisposable blockerSubscription = Window.WindowOpenedEvent
                .AddClassHandler<LauncherLocationWarningWindow>((window, _) =>
                {
                    warningWindow = window;
                    Dispatcher.UIThread.Post(window.Close);
                });

            bool blocked = await workflow.ShowBlockingLauncherLocationAsync(storagePaths);

            blocked.Should().BeTrue();
            warningWindow.Should().NotBeNull();
            warningWindow!.FindControl<TextBlock>("LauncherLocationText")!.Text.Should()
                .Be(storagePaths.ExecutableDirectory);
            warningWindow.FindControl<TextBlock>("GameLocationText")!.Text.Should()
                .Be(containingInstallation.Directory);
            ((ISolidColorBrush)Application.Current!.Resources["GenLauncherBorderColor"]!).Color.Should()
                .Be(LauncherThemePresets.Create(SupportedGame.Generals).GenLauncherBorderColor.Color);
        });
    }

    private static AvaloniaStandaloneStartupWorkflow CreateWorkflow(
        IGameInstallationService installationService,
        RecordingStartupDialogService? startupDialogService = null)
    {
        return new AvaloniaStandaloneStartupWorkflow(
            installationService,
            Substitute.For<ILauncherHostEnvironmentService>(),
            new StubLauncherFilePicker(),
            new FakeStringLocalizer(),
            startupDialogService ?? new RecordingStartupDialogService());
    }
}
