using System.IO;
using System.Linq;
using GenLauncherGO.Core.Settings.Exceptions;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Startup.ViewModels;

namespace GenLauncherGO.Tests.UI.Features.Startup.ViewModels;

public sealed class LauncherGameSelectionViewModelTests
{
    [Fact]
    public void InitialState_SelectsZeroHourByDefault()
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences
        {
            LastSelectedGame = SupportedGame.Generals
        });

        var viewModel = new LauncherGameSelectionViewModel(preferencesService, new FakeStringLocalizer());

        viewModel.SelectedGame.Should().Be(SupportedGame.ZeroHour);
        viewModel.Games.Single(game => game.Game == SupportedGame.Generals).IsSelected.Should().BeFalse();
        viewModel.Games.Single(game => game.Game == SupportedGame.ZeroHour).IsSelected.Should().BeTrue();
    }

    [Fact]
    public void SelectGame_MovesTheChoiceToTheRequestedGame()
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences());
        var viewModel = new LauncherGameSelectionViewModel(preferencesService, new FakeStringLocalizer());
        int selectionChangedCount = 0;
        viewModel.SelectionChanged += (_, _) => selectionChangedCount++;

        viewModel.SelectGameCommand.Execute(SupportedGame.Generals);

        viewModel.SelectedGame.Should().Be(SupportedGame.Generals);
        viewModel.Games.Single(game => game.Game == SupportedGame.Generals).IsSelected.Should().BeTrue();
        viewModel.Games.Single(game => game.Game == SupportedGame.ZeroHour).IsSelected.Should().BeFalse();
        selectionChangedCount.Should().Be(1);
        preferencesService.Updates.Should().BeEmpty();
    }

    [Fact]
    public void Continue_AfterSelectingAGame_PersistsChoiceAndCompletes()
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences());
        var viewModel = new LauncherGameSelectionViewModel(preferencesService, new FakeStringLocalizer());
        int completedCount = 0;
        viewModel.Completed += (_, _) => completedCount++;
        viewModel.SelectGameCommand.Execute(SupportedGame.Generals);

        viewModel.ContinueCommand.Execute(null);

        preferencesService.Updates.Should().ContainSingle();
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
        var viewModel = new LauncherGameSelectionViewModel(preferencesService, new FakeStringLocalizer());
        int saveFailedCount = 0;
        int completedCount = 0;
        viewModel.SaveFailed += (_, _) => saveFailedCount++;
        viewModel.Completed += (_, _) => completedCount++;

        viewModel.ContinueCommand.Execute(null);

        saveFailedCount.Should().Be(1);
        completedCount.Should().Be(0);
        preferencesService.Updates.Should().BeEmpty();
        preferencesService.Current.LastSelectedGame.Should().BeNull();
    }

    [Fact]
    public void Canceling_RequestsStartupExit()
    {
        var preferencesService = new RecordingLauncherPreferencesService(new LauncherPreferences());
        var viewModel = new LauncherGameSelectionViewModel(preferencesService, new FakeStringLocalizer());
        int cancelRequestedCount = 0;
        viewModel.CancelRequested += (_, _) => cancelRequestedCount++;

        viewModel.CancelCommand.Execute(null);

        cancelRequestedCount.Should().Be(1);
        preferencesService.Updates.Should().BeEmpty();
    }
}
