using System.Collections.Generic;
using System.IO;
using GenLauncherGO.Core.Settings.Exceptions;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Shell.Contracts;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Settings.ViewModels;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Features.Startup.ViewModels;

namespace GenLauncherGO.Tests.UI.Features.Settings;

public sealed class LauncherSettingsViewModelTests
{
    [Fact]
    public void GameArguments_WhenChanged_PersistsImmediately()
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences
        {
            Games = new LauncherGamePreferencesSet
            {
                Generals = new LauncherGamePreferences { GameArguments = "-generals" },
                ZeroHour = new LauncherGamePreferences { GameArguments = "-old" }
            }
        });
        LauncherSettingsViewModel viewModel = CreateViewModel(preferencesService);

        viewModel.GameArguments = "-quickstart";

        preferencesService.Updates.Should().ContainSingle();
        preferencesService.Current.Games.Generals.GameArguments.Should().Be("-generals");
        preferencesService.Current.Games.ZeroHour.GameArguments.Should().Be("-quickstart");
        viewModel.GameArguments.Should().Be("-quickstart");
    }

    [Fact]
    public void GameArguments_WhenAssignedTheCurrentValue_PersistsNothing()
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences
        {
            Games = new LauncherGamePreferencesSet
            {
                ZeroHour = new LauncherGamePreferences { GameArguments = "-win" }
            }
        });
        LauncherSettingsViewModel viewModel = CreateViewModel(preferencesService);

        viewModel.GameArguments = "-win";

        preferencesService.Updates.Should().BeEmpty();
        viewModel.GameArguments.Should().Be("-win");
    }

    [Fact]
    public void AutoDeleteOldVersions_WhenEnabled_PersistsOnlyItsOwnSharedField()
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences());
        LauncherSettingsViewModel viewModel = CreateViewModel(preferencesService);

        viewModel.AutoDeleteOldVersions = true;

        preferencesService.Current.Shared.Should().Be(new LauncherSharedPreferences
        {
            AutoDeleteOldVersions = true
        });
        viewModel.AutoDeleteOldVersions.Should().BeTrue();
        viewModel.HideLauncherAfterGameStart.Should().BeFalse();
    }

    [Fact]
    public void HideLauncherAfterGameStart_WhenEnabled_PersistsOnlyItsOwnSharedField()
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences());
        LauncherSettingsViewModel viewModel = CreateViewModel(preferencesService);

        viewModel.HideLauncherAfterGameStart = true;

        preferencesService.Current.Shared.Should().Be(new LauncherSharedPreferences
        {
            HideLauncherAfterGameStart = true
        });
        viewModel.HideLauncherAfterGameStart.Should().BeTrue();
        viewModel.AutoDeleteOldVersions.Should().BeFalse();
    }

    [Theory]
    [InlineData(SupportedGame.ZeroHour, SupportedGame.Generals)]
    [InlineData(SupportedGame.Generals, SupportedGame.ZeroHour)]
    public void SwitchTargetGame_IsTheGameTheSessionIsNotManaging(
        SupportedGame managedGame,
        SupportedGame expectedTarget)
    {
        LauncherRuntimeContext runtimeContext = TestLauncherRuntimeContext.Create(
            TestLauncherPaths.Create(@"C:\Game", managedGame));

        LauncherSettingsViewModel viewModel = CreateViewModel(
            new RecordingLauncherPreferencesService(new LauncherPreferences()),
            runtimeContext: runtimeContext);

        viewModel.SwitchTargetGame.Should().Be(expectedTarget);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(@"C:\Games\Generals", true)]
    public void CanSwitchGame_FollowsWhetherTheTargetGameHasAPath(string? generalsPath, bool expected)
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences
        {
            Installations = new LauncherInstallations
            {
                Generals = generalsPath,
                ZeroHour = @"C:\Games\ZeroHour"
            }
        });

        LauncherSettingsViewModel viewModel = CreateViewModel(preferencesService);

        viewModel.CanSwitchGame.Should().Be(expected);
    }

    [Fact]
    public void UseSystemLanguage_WhenSelected_PersistsAndRequestsRestartPrompt()
    {
        var preferences = new LauncherPreferences
        {
            Shared = new LauncherSharedPreferences { UseEnglishLanguage = true }
        };
        var preferencesService = new RecordingLauncherPreferencesService(preferences);
        LauncherSettingsViewModel viewModel = CreateViewModel(preferencesService);
        int promptRequestCount = 0;
        viewModel.LanguagePreferenceChanged += (_, _) => promptRequestCount++;

        viewModel.UseSystemLanguage = true;

        preferencesService.Updates.Should().ContainSingle();
        preferencesService.Current.Shared.UseEnglishLanguage.Should().BeFalse();
        promptRequestCount.Should().Be(1);
        viewModel.UseEnglishLanguage.Should().BeFalse();
        viewModel.UseSystemLanguage.Should().BeTrue();
    }

    [Fact]
    public void UseSystemLanguage_WhenPersistenceFails_RestoresPreviousValueWithoutRequestingRestart()
    {
        var preferences = new LauncherPreferences
        {
            Shared = new LauncherSharedPreferences { UseEnglishLanguage = true }
        };
        var preferencesService = new RecordingLauncherPreferencesService(preferences)
        {
            UpdateFailure = new LauncherPreferencesPersistenceException(new IOException("locked"))
        };
        LauncherSettingsViewModel viewModel = CreateViewModel(preferencesService);
        int failureRequestCount = 0;
        int restartRequestCount = 0;
        viewModel.PreferencesSaveFailed += (_, _) => failureRequestCount++;
        viewModel.LanguagePreferenceChanged += (_, _) => restartRequestCount++;

        viewModel.UseSystemLanguage = true;

        preferencesService.Current.Should().Be(preferences);
        viewModel.UseEnglishLanguage.Should().BeTrue();
        viewModel.UseSystemLanguage.Should().BeFalse();
        failureRequestCount.Should().Be(1);
        restartRequestCount.Should().Be(0);
    }

    [Fact]
    public void RefreshAfterGameManagementChange_RefreshesCachedPreferencesForActiveSession()
    {
        var preferences = new LauncherPreferences
        {
            Installations = new LauncherInstallations
            {
                Generals = @"C:\Games\Generals",
                ZeroHour = @"C:\Games\ZeroHour"
            },
            LastSelectedGame = SupportedGame.ZeroHour
        };
        var preferencesService = new RecordingLauncherPreferencesService(preferences);
        LauncherSettingsViewModel viewModel = CreateViewModel(preferencesService);
        preferencesService.Update(preferences with
        {
            Installations = preferences.Installations with { ZeroHour = @"D:\Games\ZeroHour" }
        });

        viewModel.RefreshAfterGameManagementChange();
        viewModel.GameArguments = "-quickstart";

        preferencesService.Current.Installations.Should().Be(new LauncherInstallations
        {
            Generals = @"C:\Games\Generals",
            ZeroHour = @"D:\Games\ZeroHour"
        });
        preferencesService.Current.LastSelectedGame.Should().Be(SupportedGame.ZeroHour);
        preferencesService.Current.Games.ZeroHour.GameArguments.Should().Be("-quickstart");
    }

    [Fact]
    public void RefreshAfterGameManagementChange_AfterRuntimeSwitch_RefreshesGameSpecificArgumentFields()
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences
        {
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
        });
        LauncherRuntimeContext runtimeContext = TestLauncherRuntimeContext.Create(
            TestLauncherPaths.Create(@"C:\Game"));
        LauncherSettingsViewModel viewModel = CreateViewModel(
            preferencesService,
            runtimeContext: runtimeContext);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) => changedProperties.Add(eventArgs.PropertyName);

        viewModel.GameArguments.Should().Be("-win");
        viewModel.WorldBuilderArguments.Should().Be("-zero-hour-wb");
        runtimeContext.RuntimePaths.SwitchActive(
            TestLauncherPaths.Create(@"C:\Game", SupportedGame.Generals));

        viewModel.RefreshAfterGameManagementChange();

        viewModel.GameArguments.Should().Be("-generals");
        viewModel.WorldBuilderArguments.Should().Be("-generals-wb");
        changedProperties.Should().Contain(nameof(LauncherSettingsViewModel.GameArguments));
        changedProperties.Should().Contain(nameof(LauncherSettingsViewModel.WorldBuilderArguments));
    }

    [Fact]
    public void LinkCommands_WhenExecuted_OpenExpectedShellTargets()
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences());
        ILauncherShellService shellService = Substitute.For<ILauncherShellService>();
        LauncherPaths launcherPaths = TestLauncherPaths.Create(@"C:\Game");
        LauncherSettingsViewModel viewModel = CreateViewModel(
            preferencesService,
            shellService,
            TestLauncherRuntimeContext.Create(launcherPaths));

        viewModel.OpenGeneralsOnlineDiscordCommand.Execute(null);
        viewModel.OpenLogsDirectoryCommand.Execute(null);
        viewModel.OpenGitHubRepositoryCommand.Execute(null);
        viewModel.OpenDonationCommand.Execute(null);

        shellService.Received(1).OpenUri("https://discord.playgenerals.online");
        shellService.Received(1).OpenFolder(
            TestLauncherPaths.CreateRuntimePathContext(launcherPaths).StoragePaths.LogsDirectory,
            false,
            true);
        shellService.Received(1).OpenUri("https://github.com/x64-dev/GenLauncher_GO");
        shellService.Received(1).OpenUri(
            "https://boosty.to/genlauncher/single-payment/donation/157147?share=target_link");
    }

    private static LauncherSettingsViewModel CreateViewModel(
        RecordingLauncherPreferencesService preferencesService,
        ILauncherShellService? shellService = null,
        LauncherRuntimeContext? runtimeContext = null)
    {
        LauncherRuntimeContext resolvedRuntimeContext = runtimeContext ??
                                                        TestLauncherRuntimeContext.Create(
                                                            TestLauncherPaths.Create(@"C:\Game"));
        FakeStringLocalizer stringLocalizer = new();
        LauncherInstallationsViewModel installations = TestLauncherInstallations.CreateViewModel(
            preferencesService.Current.Installations,
            new FakeGameInstallationService(),
            storagePaths: resolvedRuntimeContext.StoragePaths,
            stringLocalizer: stringLocalizer);

        return new LauncherSettingsViewModel(
            preferencesService,
            shellService ?? Substitute.For<ILauncherShellService>(),
            resolvedRuntimeContext,
            installations,
            stringLocalizer);
    }
}
