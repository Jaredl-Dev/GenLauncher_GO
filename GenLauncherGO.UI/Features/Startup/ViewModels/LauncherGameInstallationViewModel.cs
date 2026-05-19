using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.UI.Features.Startup.ViewModels;

/// <summary>
///     Presents one game's installation row on the setup screen.
/// </summary>
/// <remarks>
///     This is a projection, not a copy: every value forwards to <see cref="LauncherInstallationsViewModel" />, which
///     stays the single authority for path validation and duplicate detection. Its only job is to let the setup screen
///     describe an installation row once instead of once per game.
/// </remarks>
internal sealed class LauncherGameInstallationViewModel : ObservableObject
{
    private readonly LauncherInstallationsViewModel _installations;

    private readonly bool _isGenerals;

    public LauncherGameInstallationViewModel(
        SupportedGame game,
        LauncherInstallationsViewModel installations,
        ILauncherStringLocalizer stringLocalizer)
    {
        ArgumentNullException.ThrowIfNull(stringLocalizer);

        Game = game;
        _installations = installations ?? throw new ArgumentNullException(nameof(installations));
        _isGenerals = game == SupportedGame.Generals;
        DisplayName = stringLocalizer[_isGenerals ? "GeneralsFullName" : "ZeroHourFullName"];
        BrowseCommand = _isGenerals ? installations.BrowseGeneralsCommand : installations.BrowseZeroHourCommand;
        DetectCommand = _isGenerals ? installations.DetectGeneralsCommand : installations.DetectZeroHourCommand;

        // Any path edit revalidates both games, so mirror every change rather than mapping property by property.
        _installations.PropertyChanged += OnInstallationsPropertyChanged;
    }

    public SupportedGame Game { get; }

    public string DisplayName { get; }

    public IAsyncRelayCommand<object?> BrowseCommand { get; }

    public IRelayCommand DetectCommand { get; }

    public string Path
    {
        get => _isGenerals ? _installations.GeneralsPath : _installations.ZeroHourPath;
        set
        {
            if (_isGenerals)
            {
                _installations.GeneralsPath = value;
            }
            else
            {
                _installations.ZeroHourPath = value;
            }
        }
    }

    public string StatusText =>
        _isGenerals ? _installations.GeneralsStatusText : _installations.ZeroHourStatusText;

    public bool IsValid => _isGenerals ? _installations.IsGeneralsValid : _installations.IsZeroHourValid;

    public bool HasValidationError =>
        _isGenerals ? _installations.HasGeneralsValidationError : _installations.HasZeroHourValidationError;

    public bool ShowProgramFilesWarning => _isGenerals
        ? _installations.ShowGeneralsProgramFilesWarning
        : _installations.ShowZeroHourProgramFilesWarning;

    public bool ShowDifferentDriveRecommendation => _isGenerals
        ? _installations.ShowGeneralsDifferentDriveRecommendation
        : _installations.ShowZeroHourDifferentDriveRecommendation;

    private void OnInstallationsPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(HasValidationError));
        OnPropertyChanged(nameof(ShowProgramFilesWarning));
        OnPropertyChanged(nameof(ShowDifferentDriveRecommendation));
    }
}
