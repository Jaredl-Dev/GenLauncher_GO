using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Exceptions;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Shell.Contracts;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Core.Startup.Models;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Launcher.Contracts;
using GenLauncherGO.UI.Features.Settings.ViewModels;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Features.Startup.ViewModels;
using GenLauncherGO.UI.Shared.Localization;

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
                ZeroHour = new LauncherGamePreferences { GameArguments = "-old" },
            },
        });
        LauncherSettingsViewModel viewModel = CreateViewModel(preferencesService);

        viewModel.GameArguments = "-quickstart";

        preferencesService.Updates.Should().ContainSingle();
        preferencesService.Current.Games.Generals.GameArguments.Should().Be("-generals");
        preferencesService.Current.Games.ZeroHour.GameArguments.Should().Be("-quickstart");
        viewModel.GameArguments.Should().Be("-quickstart");
    }

    [Fact]
    public void UseSystemLanguage_WhenSelected_PersistsAndRequestsRestartPrompt()
    {
        var preferences = new LauncherPreferences
        {
            Shared = new LauncherSharedPreferences { UseEnglishLanguage = true },
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
            Shared = new LauncherSharedPreferences { UseEnglishLanguage = true },
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
                ZeroHour = @"C:\Games\ZeroHour",
            },
            LastSelectedGame = SupportedGame.ZeroHour,
        };
        var preferencesService = new RecordingLauncherPreferencesService(preferences);
        LauncherSettingsViewModel viewModel = CreateViewModel(preferencesService);
        preferencesService.Update(preferences with
        {
            Installations = preferences.Installations with { ZeroHour = @"D:\Games\ZeroHour" },
        });

        viewModel.RefreshAfterGameManagementChange();
        viewModel.GameArguments = "-quickstart";

        preferencesService.Current.Installations.Should().Be(new LauncherInstallations
        {
            Generals = @"C:\Games\Generals",
            ZeroHour = @"D:\Games\ZeroHour",
        });
        preferencesService.Current.LastSelectedGame.Should().Be(SupportedGame.ZeroHour);
        preferencesService.Current.Games.ZeroHour.GameArguments.Should().Be("-quickstart");
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
            launcherPaths);

        viewModel.OpenGeneralsOnlineDiscordCommand.Execute(null);
        viewModel.OpenLogsDirectoryCommand.Execute(null);
        viewModel.OpenGitHubRepositoryCommand.Execute(null);
        viewModel.OpenDonationCommand.Execute(null);

        shellService.Received(1).OpenUri("https://discord.playgenerals.online");
        shellService.Received(1).OpenFolder(
            TestLauncherPaths.CreateRuntimePathContext(launcherPaths).StoragePaths.LogsDirectory,
            requireFiles: false,
            createIfMissing: true);
        shellService.Received(1).OpenUri("https://github.com/x64-dev/GenLauncher_GO");
        shellService.Received(1).OpenUri(
            "https://boosty.to/genlauncher/single-payment/donation/157147?share=target_link");
    }

    private static LauncherSettingsViewModel CreateViewModel(
        RecordingLauncherPreferencesService preferencesService,
        ILauncherShellService? shellService = null,
        LauncherPaths? launcherPaths = null)
    {
        LauncherPaths resolvedPaths = launcherPaths ?? TestLauncherPaths.Create(@"C:\Game");
        var runtimeContext = new LauncherRuntimeContext(
            TestLauncherPaths.CreateRuntimePathContext(resolvedPaths),
            "1.2.3");
        IGameInstallationService installationService = CreateInstallationService();
        ILauncherHostEnvironmentService hostEnvironmentService =
            Substitute.For<ILauncherHostEnvironmentService>();
        ILauncherStringLocalizer stringLocalizer = Substitute.For<ILauncherStringLocalizer>();
        stringLocalizer[Arg.Any<string>()].Returns(call => call.ArgAt<string>(0));
        var installations = new LauncherInstallationsViewModel(
            preferencesService.Current.Installations,
            runtimeContext.StoragePaths,
            installationService,
            hostEnvironmentService,
            new NullLauncherFilePicker(),
            stringLocalizer);

        return new LauncherSettingsViewModel(
            preferencesService,
            shellService ?? Substitute.For<ILauncherShellService>(),
            runtimeContext,
            installations,
            stringLocalizer);
    }

    private static IGameInstallationService CreateInstallationService()
    {
        IGameInstallationService installationService =
            Substitute.For<IGameInstallationService>();
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
                    : GameInstallationValidationResult.Valid(Path.GetFullPath(directory));
            });
        return installationService;
    }

    private sealed class NullLauncherFilePicker : ILauncherFilePicker
    {
        public Task<string?> PickGameInstallationFolderAsync(
            Window owner,
            string? initialDirectory)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<IReadOnlyList<string>> PickManualPackageFilesAsync(Window owner)
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        public Task<string?> PickModificationImageFileAsync(
            Window owner,
            string imageFilterLabel)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> PickGameExecutableFileAsync(Window owner, string gameDirectory)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class RecordingLauncherPreferencesService : ILauncherPreferencesService
    {
        public RecordingLauncherPreferencesService(LauncherPreferences preferences)
        {
            Current = preferences;
        }

        public event EventHandler<LauncherPreferences>? PreferencesChanged;

        public LauncherPreferences Current { get; private set; }

        public List<LauncherPreferences> Updates { get; } = new List<LauncherPreferences>();

        public Exception? UpdateFailure { get; init; }

        public void Update(LauncherPreferences preferences)
        {
            if (UpdateFailure != null)
            {
                throw UpdateFailure;
            }

            Current = preferences;
            Updates.Add(preferences);
            PreferencesChanged?.Invoke(this, preferences);
        }
    }
}
