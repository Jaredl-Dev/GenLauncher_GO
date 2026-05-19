using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Shell.Contracts;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Dialogs.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Settings.ViewModels;
using GenLauncherGO.UI.Features.Settings.Views;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Features.Startup.ViewModels;
using GenLauncherGO.UI.Features.Startup.Views;
using GenLauncherGO.UI.Shared.Themes;

namespace GenLauncherGO.Tests.UI.Features.Settings;

[Collection("Avalonia")]
public sealed class LauncherSettingsWindowTests
{
    private const string GeneralsDirectory = @"C:\Games\Generals";

    private const string ZeroHourDirectory = @"C:\Games\ZeroHour";

    private const string RestartPromptTitle = "Restart to apply the language";

    private const string RestartPromptDetails = "The launcher restarts to load the new language.";

    private const string RestartLaterText = "Restart later";

    private const string RestartNowText = "Restart now";

    [Fact]
    public void SuccessfulGameSwitch_ReappliesThemeAndRefreshesArgumentInputs()
    {
        StaTestRunner.Run(() =>
        {
            using ApplicationThemeScope themeScope = new();
            SettingsWindowScenario scenario = CreateScenario();
            ColorsInfo generalsColors = LauncherThemePresets.Create(SupportedGame.Generals);
            SupportedGame? requestedGame = null;
            scenario.Window.ConfigureGameManagement(
                (_, _) => Task.FromResult(false),
                game =>
                {
                    requestedGame = game;
                    scenario.RuntimeContext.RuntimePaths.SwitchActive(
                        scenario.RuntimeContext.StoragePaths.CreateGamePaths(
                            SupportedGame.Generals,
                            GeneralsDirectory));
                    scenario.RuntimeContext.Colors = generalsColors;
                    return Task.FromResult(true);
                });
            SupportedGame expectedTarget = scenario.ViewModel.SwitchTargetGame;

            try
            {
                scenario.Window.Show();

                scenario.Window.FindControl<Button>("SwitchGameButton")!
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                requestedGame.Should().Be(expectedTarget);
                scenario.Window.FindControl<TextBox>("GameArgumentsInput")!.Text.Should().Be("-generals");
                scenario.Window.FindControl<TextBox>("WorldBuilderArgumentsInput")!.Text.Should()
                    .Be("-generals-wb");
                IResourceDictionary applicationResources = Application.Current!.Resources;
                applicationResources["GenLauncherBorderColor"]
                    .Should().BeSameAs(generalsColors.GenLauncherBorderColor);
                applicationResources["GenLauncherActiveColor"]
                    .Should().BeSameAs(generalsColors.GenLauncherActiveColor);
            }
            finally
            {
                scenario.Window.Close();
            }
        });
    }

    [Fact]
    public void LanguageChange_WhenRestartIsConfirmed_RequestsRestartAndCloses()
    {
        StaTestRunner.Run(async () =>
        {
            using ApplicationThemeScope themeScope = new();
            SettingsWindowScenario scenario = CreateScenario(true);
            TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            scenario.Window.Closed += (_, _) => closed.TrySetResult();

            try
            {
                scenario.Window.Show();

                scenario.ViewModel.UseSystemLanguage = true;
                await closed.Task.WaitAsync(TestTimeouts.Wait);

                scenario.Window.RestartRequested.Should().BeTrue();
                (LauncherInfoDialogRequest request, string? continueText) =
                    scenario.DialogService.WarningConfirmationRequests.Should().ContainSingle().Which;
                request.MainMessage.Should().Be(RestartPromptTitle);
                request.DetailMessage.Should().Be(RestartPromptDetails);
                request.CancelText.Should().Be(RestartLaterText);
                continueText.Should().Be(RestartNowText);
            }
            finally
            {
                scenario.Window.Close();
            }
        });
    }

    [Fact]
    public void LanguageChange_WhenRestartIsDeclined_KeepsTheWindowOpen()
    {
        StaTestRunner.Run(async () =>
        {
            using ApplicationThemeScope themeScope = new();
            SettingsWindowScenario scenario = CreateScenario();

            try
            {
                scenario.Window.Show();

                scenario.ViewModel.UseSystemLanguage = true;
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

                scenario.Window.RestartRequested.Should().BeFalse();
                scenario.Window.IsVisible.Should().BeTrue();
                scenario.DialogService.WarningConfirmationRequests.Should().ContainSingle()
                    .Which.ContinueText.Should().Be(RestartNowText);
                scenario.PreferencesService.Current.Shared.UseEnglishLanguage.Should().BeFalse();
            }
            finally
            {
                scenario.Window.Close();
            }
        });
    }

    [Fact]
    public void Close_WhileAGameSwitchIsRunning_IsRefusedUntilItFinishes()
    {
        StaTestRunner.Run(async () =>
        {
            using ApplicationThemeScope themeScope = new();
            SettingsWindowScenario scenario = CreateScenario();
            TaskCompletionSource<bool> switchCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            scenario.Window.ConfigureGameManagement(
                (_, _) => Task.FromResult(false),
                _ => switchCompletion.Task);
            bool closed = false;
            scenario.Window.Closed += (_, _) => closed = true;
            scenario.Window.Show();
            scenario.Window.FindControl<Button>("SwitchGameButton")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            scenario.Window.Close();

            closed.Should().BeFalse();
            scenario.Window.IsVisible.Should().BeTrue();

            // The refusal only means anything if the same request is honoured once the switch finishes.
            switchCompletion.SetResult(true);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            scenario.Window.Close();

            closed.Should().BeTrue();
        });
    }

    [Fact]
    public void GameManagement_BeforeItIsConfigured_IsDisabled()
    {
        StaTestRunner.Run(() =>
        {
            using ApplicationThemeScope themeScope = new();

            SettingsWindowScenario scenario = CreateScenario();

            scenario.Window.FindControl<Border>("GameManagementSection")!.IsEnabled.Should().BeFalse();
        });
    }

    [Fact]
    public void InstallationPaths_WhenTheSetupDialogIsDismissed_RestoresPersistedDrafts()
    {
        StaTestRunner.Run(async () =>
        {
            using ApplicationThemeScope themeScope = new();
            SettingsWindowScenario scenario = CreateScenario();
            scenario.Window.ConfigureGameManagement(
                (_, _) => Task.FromResult(false),
                _ => Task.FromResult(false));
            using IDisposable setupSubscription = Window.WindowOpenedEvent
                .AddClassHandler<LauncherSetupWindow>(
                    (setupWindow, _) => Dispatcher.UIThread.Post(setupWindow.Close));
            TaskCompletionSource restored = new(TaskCreationOptions.RunContinuationsAsynchronously);
            scenario.ViewModel.Installations.PropertyChanged += OnInstallationsPropertyChanged;
            scenario.ViewModel.Installations.GeneralsPath = @"D:\Draft\Generals";

            try
            {
                scenario.Window.Show();
                scenario.Window.UpdateLayout();

                InstallationPathsButton(scenario.Window)
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await restored.Task.WaitAsync(TestTimeouts.Wait);

                scenario.ViewModel.Installations.GeneralsPath.Should().Be(GeneralsDirectory);
                scenario.ViewModel.Installations.ZeroHourPath.Should().Be(ZeroHourDirectory);
            }
            finally
            {
                scenario.ViewModel.Installations.PropertyChanged -= OnInstallationsPropertyChanged;
                scenario.Window.Close();
            }

            void OnInstallationsPropertyChanged(object? sender, PropertyChangedEventArgs args)
            {
                if (args.PropertyName == nameof(LauncherInstallationsViewModel.GeneralsPath) &&
                    scenario.ViewModel.Installations.GeneralsPath == GeneralsDirectory)
                {
                    restored.TrySetResult();
                }
            }
        });
    }

    private static Button InstallationPathsButton(LauncherSettingsWindow window)
    {
        return window.FindControl<Border>("GameManagementSection")!
            .GetVisualDescendants()
            .OfType<Button>()
            .First(button => button.Name != "SwitchGameButton");
    }

    private static SettingsWindowScenario CreateScenario(bool restartConfirmed = false)
    {
        LauncherPreferences preferences = new()
        {
            Installations = new LauncherInstallations
            {
                Generals = GeneralsDirectory,
                ZeroHour = ZeroHourDirectory
            },
            LastSelectedGame = SupportedGame.ZeroHour,
            Shared = new LauncherSharedPreferences { UseEnglishLanguage = true },
            Games = new LauncherGamePreferencesSet
            {
                Generals = new LauncherGamePreferences
                {
                    GameArguments = "-generals",
                    WorldBuilderArguments = "-generals-wb"
                },
                ZeroHour = new LauncherGamePreferences
                {
                    GameArguments = "-win",
                    WorldBuilderArguments = "-zero-hour-wb"
                }
            }
        };
        RecordingLauncherPreferencesService preferencesService = new(preferences);
        LauncherRuntimeContext runtimeContext = TestLauncherRuntimeContext.Create(
            TestLauncherPaths.Create(),
            colors: LauncherThemePresets.Create(SupportedGame.ZeroHour));
        var stringLocalizer = FakeStringLocalizer.Create(
            TestLocalizedStrings.Settings,
            ("LanguageRestartPromptTitle", RestartPromptTitle),
            ("LanguageRestartPromptDetails", RestartPromptDetails),
            ("RestartLater", RestartLaterText),
            ("RestartNow", RestartNowText));
        LauncherSettingsViewModel viewModel = new(
            preferencesService,
            Substitute.For<ILauncherShellService>(),
            runtimeContext,
            TestLauncherInstallations.CreateViewModel(
                preferences.Installations,
                new FakeGameInstallationService(),
                storagePaths: runtimeContext.StoragePaths,
                stringLocalizer: stringLocalizer),
            stringLocalizer);
        RecordingLauncherDialogService dialogService = new()
        {
            WarningConfirmationResult = restartConfirmed
        };
        IGameExecutableDiscoveryService executableDiscovery =
            Substitute.For<IGameExecutableDiscoveryService>();
        LauncherExecutableManagementViewModel executableManagementViewModel = new(
            preferencesService,
            executableDiscovery,
            new LauncherExecutableSelectionService(
                executableDiscovery,
                runtimeContext,
                preferencesService,
                stringLocalizer),
            runtimeContext,
            stringLocalizer);
        LauncherSettingsWindow window = new(
            viewModel,
            stringLocalizer,
            preferencesService,
            new RecordingStartupDialogService(),
            executableManagementViewModel,
            new StubLauncherFilePicker(),
            dialogService);

        return new SettingsWindowScenario(
            window,
            viewModel,
            preferencesService,
            dialogService,
            runtimeContext);
    }

    private sealed record SettingsWindowScenario(
        LauncherSettingsWindow Window,
        LauncherSettingsViewModel ViewModel,
        RecordingLauncherPreferencesService PreferencesService,
        RecordingLauncherDialogService DialogService,
        LauncherRuntimeContext RuntimeContext);
}
