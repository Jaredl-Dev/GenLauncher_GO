using System;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Exceptions;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Shell.Contracts;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Features.Startup.ViewModels;
using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.UI.Features.Settings.ViewModels;

internal sealed class LauncherSettingsViewModel : ObservableObject
{
    private const string GeneralsOnlineDiscordUrl = "https://discord.playgenerals.online";

    private const string PredecessorRepositoryUrl = "https://github.com/x64-dev/GenLauncher_GO";

    private const string OriginalAuthorDonationUrl =
        "https://boosty.to/genlauncher/single-payment/donation/157147?share=target_link";

    private readonly ILauncherPreferencesService _preferencesService;

    private readonly ILauncherShellService _shellService;

    private readonly ILauncherStringLocalizer _stringLocalizer;

    private readonly string _logsDirectory;

    private readonly LauncherRuntimeContext _runtimeContext;

    private LauncherPreferences _preferences;

    public LauncherSettingsViewModel(
        ILauncherPreferencesService preferencesService,
        ILauncherShellService shellService,
        LauncherRuntimeContext runtimeContext,
        LauncherInstallationsViewModel installations,
        ILauncherStringLocalizer stringLocalizer)
    {
        _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        _shellService = shellService ?? throw new ArgumentNullException(nameof(shellService));
        ArgumentNullException.ThrowIfNull(runtimeContext);

        Installations = installations ?? throw new ArgumentNullException(nameof(installations));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _logsDirectory = runtimeContext.StoragePaths.LogsDirectory;
        _runtimeContext = runtimeContext;
        _preferences = _preferencesService.Current;

        CloseCommand = new RelayCommand(RequestClose);
        OpenGeneralsOnlineDiscordCommand = new RelayCommand(() => OpenUri(GeneralsOnlineDiscordUrl));
        OpenLogsDirectoryCommand = new RelayCommand(OpenLogsDirectory);
        OpenGitHubRepositoryCommand = new RelayCommand(() => OpenUri(PredecessorRepositoryUrl));
        OpenDonationCommand = new RelayCommand(() => OpenUri(OriginalAuthorDonationUrl));
    }

    public event EventHandler? CloseRequested;

    /// <summary>
    /// Occurs after a changed language preference has been persisted and requires a restart prompt.
    /// </summary>
    public event EventHandler? LanguagePreferenceChanged;

    /// <summary>
    /// Occurs when a user-requested preference change could not be persisted and was not applied.
    /// </summary>
    public event EventHandler? PreferencesSaveFailed;

    public LauncherInstallationsViewModel Installations { get; }

    public IRelayCommand CloseCommand { get; }

    public IRelayCommand OpenGeneralsOnlineDiscordCommand { get; }

    public IRelayCommand OpenLogsDirectoryCommand { get; }

    public IRelayCommand OpenGitHubRepositoryCommand { get; }

    public IRelayCommand OpenDonationCommand { get; }

    public string ActiveGameName => GetGameName(_runtimeContext.CurrentlyManagedGame);

    public SupportedGame SwitchTargetGame => _runtimeContext.CurrentlyManagedGame == SupportedGame.ZeroHour
        ? SupportedGame.Generals
        : SupportedGame.ZeroHour;

    public string SwitchGameButtonText => string.Format(
        _stringLocalizer["SwitchToGame"],
        GetGameName(SwitchTargetGame));

    public bool CanSwitchGame =>
        !string.IsNullOrWhiteSpace(_preferences.Installations.GetPath(SwitchTargetGame));

    /// <summary>
    /// Refreshes cached and displayed game-management state after paths or the live session change.
    /// </summary>
    public void RefreshAfterGameManagementChange()
    {
        _preferences = _preferencesService.Current;
        OnPropertyChanged(nameof(ActiveGameName));
        OnPropertyChanged(nameof(SwitchTargetGame));
        OnPropertyChanged(nameof(SwitchGameButtonText));
        OnPropertyChanged(nameof(CanSwitchGame));
    }

    public string GameArguments
    {
        get => ActiveGamePreferences.GameArguments;
        set => TryUpdateGamePreferences(ActiveGamePreferences with { GameArguments = value ?? string.Empty });
    }

    public string WorldBuilderArguments
    {
        get => ActiveGamePreferences.WorldBuilderArguments;
        set => TryUpdateGamePreferences(ActiveGamePreferences with
        {
            WorldBuilderArguments = value ?? string.Empty,
        });
    }

    public bool AutoDeleteOldVersions
    {
        get => _preferences.Shared.AutoDeleteOldVersions;
        set => TryUpdatePreferences(_preferences with
        {
            Shared = _preferences.Shared with { AutoDeleteOldVersions = value },
        });
    }

    public bool HideLauncherAfterGameStart
    {
        get => _preferences.Shared.HideLauncherAfterGameStart;
        set => TryUpdatePreferences(_preferences with
        {
            Shared = _preferences.Shared with { HideLauncherAfterGameStart = value },
        });
    }

    public bool UseEnglishLanguage
    {
        get => _preferences.Shared.UseEnglishLanguage;
        set
        {
            if (value == _preferences.Shared.UseEnglishLanguage)
            {
                return;
            }

            if (!TryUpdatePreferences(_preferences with
            {
                Shared = _preferences.Shared with { UseEnglishLanguage = value },
            }))
            {
                OnPropertyChanged(nameof(UseSystemLanguage));
                return;
            }

            OnPropertyChanged(nameof(UseSystemLanguage));
            LanguagePreferenceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool UseSystemLanguage
    {
        get => !_preferences.Shared.UseEnglishLanguage;
        set
        {
            if (value)
            {
                UseEnglishLanguage = false;
            }
        }
    }

    private bool TryUpdatePreferences(
        LauncherPreferences preferences,
        [CallerMemberName] string? propertyName = null)
    {
        if (preferences == _preferences)
        {
            return true;
        }

        try
        {
            _preferencesService.Update(preferences);
        }
        catch (LauncherPreferencesPersistenceException)
        {
            OnPropertyChanged(propertyName);
            PreferencesSaveFailed?.Invoke(this, EventArgs.Empty);
            return false;
        }

        _preferences = _preferencesService.Current;
        OnPropertyChanged(propertyName);
        return true;
    }

    private LauncherGamePreferences ActiveGamePreferences =>
        _preferences.Games.Get(_runtimeContext.CurrentlyManagedGame);

    private void TryUpdateGamePreferences(LauncherGamePreferences gamePreferences)
    {
        TryUpdatePreferences(_preferences with
        {
            Games = _preferences.Games.With(_runtimeContext.CurrentlyManagedGame, gamePreferences),
        });
    }

    private string GetGameName(SupportedGame game)
    {
        return _stringLocalizer[
            game == SupportedGame.ZeroHour ? "ZeroHourShortName" : "GeneralsShortName"];
    }

    private void RequestClose()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OpenUri(string uri)
    {
        _shellService.OpenUri(uri);
    }

    private void OpenLogsDirectory()
    {
        _shellService.OpenFolder(_logsDirectory, createIfMissing: true);
    }

}
