using System;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.UI.Features.Startup.ViewModels;

/// <summary>
///     Presents one selectable game on the initial game-choice screen.
/// </summary>
/// <remarks>
///     The choice screen shows one of these per supported game, so the card markup exists once regardless of how many
///     games the launcher manages.
/// </remarks>
internal sealed class LauncherGameChoiceViewModel : ObservableObject
{
    private readonly Lazy<Bitmap> _coverImage;

    private bool _isSelected;

    public LauncherGameChoiceViewModel(
        SupportedGame game,
        ILauncherStringLocalizer stringLocalizer,
        bool isSelected)
    {
        ArgumentNullException.ThrowIfNull(stringLocalizer);

        Game = game;
        _isSelected = isSelected;
        DisplayName = stringLocalizer[game == SupportedGame.Generals ? "GeneralsFullName" : "ZeroHourFullName"];
        SelectedText = stringLocalizer["Selected"];
        NotSelectedText = stringLocalizer["NotSelected"];
        // Deferred so the choice list can be built and asserted on without an initialized Avalonia application.
        _coverImage = new Lazy<Bitmap>(() => LoadCover(game));
    }

    public SupportedGame Game { get; }

    public string DisplayName { get; }

    public string SelectedText { get; }

    public string NotSelectedText { get; }

    public Bitmap CoverImage => _coverImage.Value;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private static Bitmap LoadCover(SupportedGame game)
    {
        Uri uri = new(game == SupportedGame.Generals
            ? "avares://GenLauncherGO/Shared/Resources/Images/OfficialCoverGenerals.png"
            : "avares://GenLauncherGO/Shared/Resources/Images/OfficialCoverZeroHour.jpg");
        return new Bitmap(AssetLoader.Open(uri));
    }
}
