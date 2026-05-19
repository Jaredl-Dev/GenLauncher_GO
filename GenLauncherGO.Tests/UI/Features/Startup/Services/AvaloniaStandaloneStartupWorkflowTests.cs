using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Core.Startup.Models;
using GenLauncherGO.UI.Features.Startup.Models;
using GenLauncherGO.UI.Features.Startup.Services;
using GenLauncherGO.UI.Features.Startup.Views;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.Tests.UI.Features.Startup.Services;

[Collection("Avalonia")]
public sealed class AvaloniaStandaloneStartupWorkflowTests
{
    [Theory]
    [InlineData(SupportedGame.Generals, SupportedGame.ZeroHour, @"C:\Games\Generals")]
    [InlineData(SupportedGame.ZeroHour, SupportedGame.Generals, @"C:\Games\Zero Hour")]
    public async Task RunAsync_OneValidInstallation_UsesCanonicalPathAndPersistsFallbackSelectionAsync(
        SupportedGame availableGame,
        SupportedGame unavailablePreference,
        string canonicalPath)
    {
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
    public async Task ShowBlockingLauncherLocationAsync_StandaloneLocation_DoesNotShowBlockerAsync()
    {
        var storagePaths = new LauncherStoragePaths(@"C:\Launcher");
        IGameInstallationService installationService = Substitute.For<IGameInstallationService>();
        AvaloniaStandaloneStartupWorkflow workflow = CreateWorkflow(installationService);

        bool blocked = await workflow.ShowBlockingLauncherLocationAsync(storagePaths);

        blocked.Should().BeFalse();
        installationService.Received(1).FindContainingInstallation(storagePaths.ExecutableDirectory);
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
        IGameInstallationService installationService)
    {
        return new AvaloniaStandaloneStartupWorkflow(
            installationService,
            Substitute.For<ILauncherHostEnvironmentService>(),
            new StubLauncherFilePicker(),
            new FakeStringLocalizer(),
            new RecordingStartupDialogService());
    }
}
