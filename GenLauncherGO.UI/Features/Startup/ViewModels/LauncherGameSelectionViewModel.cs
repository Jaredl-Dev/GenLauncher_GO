using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Exceptions;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.UI.Features.Startup.ViewModels;

/// <summary>
/// Owns the explicit initial game choice when both installations are available.
/// </summary>
internal sealed class LauncherGameSelectionViewModel : ObservableObject
{
    private readonly ILauncherPreferencesService _preferencesService;
    private SupportedGame _selectedGame = SupportedGame.ZeroHour;

    public LauncherGameSelectionViewModel(ILauncherPreferencesService preferencesService)
    {
        _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        SelectGameCommand = new RelayCommand<object?>(SelectGame);
        ContinueCommand = new RelayCommand(Continue);
        CancelCommand = new RelayCommand(() => CancelRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? Completed;

    public event EventHandler? CancelRequested;

    public event EventHandler? SaveFailed;

    public event EventHandler? SelectionChanged;

    public IRelayCommand<object?> SelectGameCommand { get; }

    public IRelayCommand ContinueCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public SupportedGame SelectedGame
    {
        get => _selectedGame;
        private set
        {
            if (_selectedGame == value)
            {
                return;
            }

            _selectedGame = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsGeneralsSelected));
            OnPropertyChanged(nameof(IsZeroHourSelected));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsGeneralsSelected => SelectedGame == SupportedGame.Generals;

    public bool IsZeroHourSelected => SelectedGame == SupportedGame.ZeroHour;

    private void SelectGame(object? parameter)
    {
        if (parameter is SupportedGame game &&
            game is SupportedGame.Generals or SupportedGame.ZeroHour)
        {
            SelectedGame = game;
        }
    }

    private void Continue()
    {
        LauncherPreferences updated = _preferencesService.Current with { LastSelectedGame = SelectedGame };
        try
        {
            _preferencesService.Update(updated);
        }
        catch (LauncherPreferencesPersistenceException)
        {
            SaveFailed?.Invoke(this, EventArgs.Empty);
            return;
        }

        Completed?.Invoke(this, EventArgs.Empty);
    }

}
