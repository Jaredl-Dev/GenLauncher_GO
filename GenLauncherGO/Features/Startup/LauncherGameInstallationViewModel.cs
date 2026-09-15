using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenLauncherGO.Shared.Localization;

namespace GenLauncherGO.Features.Startup;

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

    public LauncherGameInstallationViewModel(
        SupportedGame game,
        LauncherInstallationsViewModel installations,
        ILauncherStringLocalizer stringLocalizer)
    {
        ArgumentNullException.ThrowIfNull(stringLocalizer);

        Game = game;
        _installations = installations ?? throw new ArgumentNullException(nameof(installations));
        DisplayName = stringLocalizer[PerGame.Select(game, "GeneralsFullName", "ZeroHourFullName")];
        BrowseCommand = installations.GetBrowseCommand(game);
        DetectCommand = installations.GetDetectCommand(game);
    }

    public SupportedGame Game { get; }

    public string DisplayName { get; }

    public IAsyncRelayCommand<object?> BrowseCommand { get; }

    public IRelayCommand DetectCommand { get; }

    public string Path
    {
        get => _installations.GetPath(Game);
        set => _installations.SetPath(Game, value);
    }

    public string StatusText => _installations.GetStatusText(Game);

    public bool IsValid => _installations.IsValid(Game);

    public bool HasValidationError => _installations.HasValidationError(Game);

    public bool ShowProgramFilesWarning => _installations.ShowProgramFilesWarning(Game);

    public bool ShowDifferentDriveRecommendation => _installations.ShowDifferentDriveRecommendation(Game);

    internal void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(HasValidationError));
        OnPropertyChanged(nameof(ShowProgramFilesWarning));
        OnPropertyChanged(nameof(ShowDifferentDriveRecommendation));
    }
}
