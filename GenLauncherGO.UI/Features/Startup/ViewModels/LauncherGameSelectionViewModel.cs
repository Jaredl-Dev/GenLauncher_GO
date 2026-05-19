using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Settings;
using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.UI.Features.Startup.ViewModels;

/// <summary>
///     Owns the explicit initial game choice when both installations are available.
/// </summary>
internal sealed class LauncherGameSelectionViewModel : ObservableObject
{
    private readonly ILauncherPreferencesService _preferencesService;

    public LauncherGameSelectionViewModel(
        ILauncherPreferencesService preferencesService,
        ILauncherStringLocalizer stringLocalizer)
    {
        _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        ArgumentNullException.ThrowIfNull(stringLocalizer);

        Games =
        [
            new LauncherGameChoiceViewModel(SupportedGame.Generals, stringLocalizer, false),
            new LauncherGameChoiceViewModel(SupportedGame.ZeroHour, stringLocalizer, true)
        ];
        SelectGameCommand = new RelayCommand<object?>(SelectGame);
        ContinueCommand = new RelayCommand(Continue);
        CancelCommand = new RelayCommand(() => CancelRequested?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    ///     Gets one card per supported game, in the order they are offered.
    /// </summary>
    public IReadOnlyList<LauncherGameChoiceViewModel> Games { get; }

    public IRelayCommand<object?> SelectGameCommand { get; }

    public IRelayCommand ContinueCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public SupportedGame SelectedGame
    {
        get;
        private set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            OnPropertyChanged();
            foreach (LauncherGameChoiceViewModel choice in Games)
            {
                choice.IsSelected = choice.Game == value;
            }

            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    } = SupportedGame.ZeroHour;

    public event EventHandler? Completed;

    public event EventHandler? CancelRequested;

    public event EventHandler? SaveFailed;

    public event EventHandler? SelectionChanged;

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
        if (!LauncherPreferencesUpdate.TryApply(_preferencesService, updated))
        {
            SaveFailed?.Invoke(this, EventArgs.Empty);
            return;
        }

        Completed?.Invoke(this, EventArgs.Empty);
    }
}
