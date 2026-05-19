using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Core.Startup.Models;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Dialogs.Contracts;
using GenLauncherGO.UI.Features.Launcher.Contracts;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Settings.ViewModels;
using GenLauncherGO.UI.Features.Settings.Views;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Features.Startup.Contracts;
using GenLauncherGO.UI.Features.Startup.ViewModels;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.Tests.UI.Features.Settings;

public sealed class LauncherSettingsWindowTests
{
    [Fact]
    public void SuccessfulGameSwitchReappliesBuiltInGameTheme()
    {
        StaTestRunner.Run(() =>
        {
            LauncherPreferences preferences = new()
            {
                Installations = new LauncherInstallations
                {
                    Generals = @"C:\Games\Generals",
                    ZeroHour = @"C:\Games\ZeroHour",
                },
                LastSelectedGame = SupportedGame.ZeroHour,
            };
            ILauncherPreferencesService preferencesService = Substitute.For<ILauncherPreferencesService>();
            preferencesService.Current.Returns(preferences);
            LauncherRuntimeContext runtimeContext = new(
                TestLauncherPaths.CreateRuntimePathContext(TestLauncherPaths.Create()),
                "1.0")
            {
                Colors = LauncherThemePresets.Create(SupportedGame.ZeroHour),
            };
            IGameInstallationService installationService = Substitute.For<IGameInstallationService>();
            installationService
                .Validate(
                    Arg.Any<SupportedGame>(),
                    Arg.Any<string?>(),
                    Arg.Any<string>())
                .Returns(call =>
                {
                    string? directory = call.ArgAt<string?>(1);
                    return string.IsNullOrWhiteSpace(directory)
                        ? GameInstallationValidationResult.Invalid(
                            GameInstallationValidationFailure.PathMissing)
                        : GameInstallationValidationResult.Valid(directory);
                });
            TestStringLocalizer stringLocalizer = new();
            LauncherSettingsViewModel viewModel = new(
                preferencesService,
                Substitute.For<GenLauncherGO.Core.Shell.Contracts.ILauncherShellService>(),
                runtimeContext,
                new LauncherInstallationsViewModel(
                    preferences.Installations,
                    runtimeContext.StoragePaths,
                    installationService,
                    Substitute.For<ILauncherHostEnvironmentService>(),
                    new NullLauncherFilePicker(),
                    stringLocalizer),
                stringLocalizer);
            ILauncherDialogService dialogService = Substitute.For<ILauncherDialogService>();
            IGameExecutableDiscoveryService executableDiscovery =
                Substitute.For<IGameExecutableDiscoveryService>();
            var executableSelectionService = new LauncherExecutableSelectionService(
                executableDiscovery,
                runtimeContext,
                preferencesService,
                stringLocalizer);
            var executableManagementViewModel = new LauncherExecutableManagementViewModel(
                preferencesService,
                executableDiscovery,
                executableSelectionService,
                runtimeContext,
                stringLocalizer);
            var filePicker = new NullLauncherFilePicker();
            LauncherSettingsWindow window = new(
                viewModel,
                stringLocalizer,
                preferencesService,
                runtimeContext,
                new NullStartupDialogService(),
                executableManagementViewModel,
                filePicker,
                dialogService);
            ColorsInfo generalsColors = LauncherThemePresets.Create(SupportedGame.Generals);

            window.ConfigureGameManagement(
                (_, _) => Task.FromResult(false),
                _ =>
                {
                    runtimeContext.Colors = generalsColors;
                    return Task.FromResult(true);
                });

            try
            {
                window.Show();
                LauncherThemeResourceApplier.Apply(window, runtimeContext.Colors);

                Button switchGameButton = window.FindControl<Button>("SwitchGameButton")!;
                switchGameButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                window.Resources["GenLauncherBorderColor"]
                    .Should().BeSameAs(generalsColors.GenLauncherBorderColor);
                window.Resources["GenLauncherActiveColor"]
                    .Should().BeSameAs(generalsColors.GenLauncherBorderColor);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private sealed class NullLauncherFilePicker : ILauncherFilePicker
    {
        public Task<string?> PickGameInstallationFolderAsync(Window owner, string? initialDirectory)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<IReadOnlyList<string>> PickManualPackageFilesAsync(Window owner)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        public Task<string?> PickModificationImageFileAsync(Window owner, string imageFilterLabel)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> PickGameExecutableFileAsync(Window owner, string gameDirectory)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class NullStartupDialogService : IStartupDialogService
    {
        public Task ShowMessageAsync(string message)
        {
            return Task.CompletedTask;
        }

        public Task ShowThemedMessageAsync(string title, string message, ColorsInfo colors)
        {
            return Task.CompletedTask;
        }

        public Task<bool> ShowRetryCancelWarningAsync(string title, string message)
        {
            return Task.FromResult(false);
        }
    }
}
