using System;
using CommunityToolkit.Mvvm.Input;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Settings;

namespace GenLauncherGO.UI.Features.Startup.ViewModels;

/// <summary>
///     Persists a valid first-run or repair installation selection.
/// </summary>
internal sealed class LauncherSetupViewModel
{
    private readonly ILauncherPreferencesService _preferencesService;

    public LauncherSetupViewModel(
        ILauncherPreferencesService preferencesService,
        LauncherInstallationsViewModel installations,
        bool detectInstallationsOnOpen = true)
    {
        _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        Installations = installations ?? throw new ArgumentNullException(nameof(installations));
        ContinueCommand = new RelayCommand(Continue, () => Installations.CanContinue);
        CancelCommand = new RelayCommand(() => CancelRequested?.Invoke(this, EventArgs.Empty));
        Installations.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(LauncherInstallationsViewModel.CanContinue))
            {
                ContinueCommand.NotifyCanExecuteChanged();
            }
        };

        if (detectInstallationsOnOpen)
        {
            Installations.DetectAll();
        }
    }

    public LauncherInstallationsViewModel Installations { get; }

    public IRelayCommand ContinueCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public event EventHandler? Completed;

    public event EventHandler? CancelRequested;

    public event EventHandler? SaveFailed;

    private void Continue()
    {
        LauncherInstallations installations = Installations.CreateValidatedInstallations();
        SupportedGame? selectedGame = installations.ResolvePreferredGame(
            _preferencesService.Current.LastSelectedGame);
        LauncherPreferences updated = _preferencesService.Current with
        {
            Installations = installations,
            LastSelectedGame = selectedGame
        };

        if (!LauncherPreferencesUpdate.TryApply(_preferencesService, updated))
        {
            SaveFailed?.Invoke(this, EventArgs.Empty);
            return;
        }

        Completed?.Invoke(this, EventArgs.Empty);
    }
}
