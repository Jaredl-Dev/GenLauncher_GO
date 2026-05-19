using System.IO;
using GenLauncherGO.Core.Settings.Exceptions;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Models;
using GenLauncherGO.UI.Features.Startup.ViewModels;

namespace GenLauncherGO.Tests.UI.Features.Startup.ViewModels;

public sealed class LauncherSetupViewModelTests
{
    private const string ValidPath = @"C:\Games\Generals";

    [Fact]
    public void ContinueCommand_WithoutAValidInstallation_IsUnavailable()
    {
        LauncherInstallationsViewModel installations = TestLauncherInstallations.CreateViewModel();

        var viewModel = new LauncherSetupViewModel(
            new RecordingLauncherPreferencesService(new LauncherPreferences()),
            installations,
            false);

        viewModel.ContinueCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void InstallationBecomesValid_NotifiesContinueCommandAvailability()
    {
        LauncherInstallationsViewModel installations = TestLauncherInstallations.CreateViewModel();
        var viewModel = new LauncherSetupViewModel(
            new RecordingLauncherPreferencesService(new LauncherPreferences()),
            installations,
            false);
        int availabilityChangedCount = 0;
        viewModel.ContinueCommand.CanExecuteChanged += (_, _) => availabilityChangedCount++;

        installations.GeneralsPath = ValidPath;

        viewModel.ContinueCommand.CanExecute(null).Should().BeTrue();
        availabilityChangedCount.Should().Be(1);
    }

    [Fact]
    public void OpeningFromSettings_DoesNotAutomaticallyDiscoverInstallations()
    {
        FakeGameInstallationService installationService = new();
        LauncherInstallationsViewModel installations =
            TestLauncherInstallations.CreateViewModel(installationService: installationService);

        _ = new LauncherSetupViewModel(
            new RecordingLauncherPreferencesService(new LauncherPreferences()),
            installations,
            false);

        installationService.DiscoverValidInstallationsCalls.Should().BeEmpty();
    }

    [Fact]
    public void ContinuingWithSoleValidInstallation_PersistsCanonicalPathAndSelectsGame()
    {
        const string ConfiguredPath = @"C:\Games\GENERALS";
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences
        {
            LastSelectedGame = SupportedGame.ZeroHour
        });
        LauncherInstallationsViewModel installations = TestLauncherInstallations.CreateViewModel(
            new LauncherInstallations { Generals = ConfiguredPath },
            new FakeGameInstallationService
            {
                ValidationRule = (_, directory) => string.IsNullOrWhiteSpace(directory)
                    ? GameInstallationValidationResult.Invalid(
                        GameInstallationValidationFailure.PathMissing)
                    : GameInstallationValidationResult.Valid(ValidPath)
            });
        var viewModel = new LauncherSetupViewModel(preferencesService, installations);
        int completedCount = 0;
        viewModel.Completed += (_, _) => completedCount++;

        viewModel.ContinueCommand.Execute(null);

        preferencesService.Updates.Should().ContainSingle();
        preferencesService.Current.Installations.Should().Be(
            new LauncherInstallations { Generals = ValidPath });
        preferencesService.Current.LastSelectedGame.Should().Be(SupportedGame.Generals);
        completedCount.Should().Be(1);
    }

    [Fact]
    public void Continue_WhenPersistenceFails_RaisesSaveFailedWithoutCompleting()
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences())
        {
            UpdateFailure = new LauncherPreferencesPersistenceException(new IOException("locked"))
        };
        LauncherInstallationsViewModel installations = TestLauncherInstallations.CreateViewModel(
            new LauncherInstallations { Generals = ValidPath });
        var viewModel = new LauncherSetupViewModel(preferencesService, installations, false);
        int saveFailedCount = 0;
        int completedCount = 0;
        viewModel.SaveFailed += (_, _) => saveFailedCount++;
        viewModel.Completed += (_, _) => completedCount++;

        viewModel.ContinueCommand.Execute(null);

        saveFailedCount.Should().Be(1);
        completedCount.Should().Be(0);
        preferencesService.Updates.Should().BeEmpty();
        preferencesService.Current.Installations.Should().Be(new LauncherInstallations());
    }
}
