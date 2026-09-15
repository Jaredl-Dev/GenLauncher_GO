using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenLauncherGO.Features.Launcher;
using GenLauncherGO.Features.Settings;
using GenLauncherGO.Shared.Localization;

namespace GenLauncherGO.Features.Startup;

/// <summary>
///     Owns the editable installation-path state shared by first-run setup and launcher settings.
/// </summary>
internal sealed class LauncherInstallationsViewModel : ObservableObject
{
    private readonly ILauncherFilePicker _filePicker;
    private readonly ILauncherHostEnvironmentService _hostEnvironmentService;
    private readonly IGameInstallationService _installationService;
    private readonly LauncherStoragePaths _storagePaths;
    private readonly ILauncherStringLocalizer _stringLocalizer;
    private string _generalsPath;
    private LauncherInstallationsValidationResult _validation;
    private string _zeroHourPath;

    public LauncherInstallationsViewModel(
        LauncherInstallations installations,
        LauncherStoragePaths storagePaths,
        IGameInstallationService installationService,
        ILauncherHostEnvironmentService hostEnvironmentService,
        ILauncherFilePicker filePicker,
        ILauncherStringLocalizer stringLocalizer)
    {
        ArgumentNullException.ThrowIfNull(installations);
        _storagePaths = storagePaths ?? throw new ArgumentNullException(nameof(storagePaths));
        _installationService = installationService ?? throw new ArgumentNullException(nameof(installationService));
        _hostEnvironmentService = hostEnvironmentService ??
                                  throw new ArgumentNullException(nameof(hostEnvironmentService));
        _filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));

        _generalsPath = installations.Generals ?? string.Empty;
        _zeroHourPath = installations.ZeroHour ?? string.Empty;
        _validation = ValidateInstallations();

        BrowseGeneralsCommand = new AsyncRelayCommand<object?>(
            owner => BrowseAsync(SupportedGame.Generals, owner as Window),
            owner => owner is Window,
            AsyncRelayCommandOptions.AllowConcurrentExecutions);
        BrowseZeroHourCommand = new AsyncRelayCommand<object?>(
            owner => BrowseAsync(SupportedGame.ZeroHour, owner as Window),
            owner => owner is Window,
            AsyncRelayCommandOptions.AllowConcurrentExecutions);
        DetectGeneralsCommand = new RelayCommand(() => Detect(SupportedGame.Generals));
        DetectZeroHourCommand = new RelayCommand(() => Detect(SupportedGame.ZeroHour));
        DetectAllCommand = new RelayCommand(DetectAll);
        Games =
        [
            new LauncherGameInstallationViewModel(SupportedGame.Generals, this, stringLocalizer),
            new LauncherGameInstallationViewModel(SupportedGame.ZeroHour, this, stringLocalizer)
        ];
    }

    /// <summary>
    ///     Gets one installation row per supported game, in the order the setup screen presents them.
    /// </summary>
    public IReadOnlyList<LauncherGameInstallationViewModel> Games { get; }

    public IAsyncRelayCommand<object?> BrowseGeneralsCommand { get; }

    public IAsyncRelayCommand<object?> BrowseZeroHourCommand { get; }

    public IRelayCommand DetectGeneralsCommand { get; }

    public IRelayCommand DetectZeroHourCommand { get; }

    public IRelayCommand DetectAllCommand { get; }

    public string GeneralsPath
    {
        get => _generalsPath;
        set
        {
            string normalized = value ?? string.Empty;
            if (string.Equals(_generalsPath, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _generalsPath = normalized;
            _validation = ValidateInstallations();
            NotifyInstallationStateChanged(nameof(GeneralsPath));
        }
    }

    public string ZeroHourPath
    {
        get => _zeroHourPath;
        set
        {
            string normalized = value ?? string.Empty;
            if (string.Equals(_zeroHourPath, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _zeroHourPath = normalized;
            _validation = ValidateInstallations();
            NotifyInstallationStateChanged(nameof(ZeroHourPath));
        }
    }

    public bool IsGeneralsValid => IsValid(SupportedGame.Generals);

    public bool IsZeroHourValid => IsValid(SupportedGame.ZeroHour);

    public bool HasGeneralsValidationError => HasValidationError(SupportedGame.Generals);

    public bool HasZeroHourValidationError => HasValidationError(SupportedGame.ZeroHour);

    public string GeneralsStatusText => GetStatusText(SupportedGame.Generals);

    public string ZeroHourStatusText => GetStatusText(SupportedGame.ZeroHour);

    public bool ShowGeneralsProgramFilesWarning => ShowProgramFilesWarning(SupportedGame.Generals);

    public bool ShowZeroHourProgramFilesWarning => ShowProgramFilesWarning(SupportedGame.ZeroHour);

    public bool ShowGeneralsDifferentDriveRecommendation => ShowDifferentDriveRecommendation(SupportedGame.Generals);

    public bool ShowZeroHourDifferentDriveRecommendation => ShowDifferentDriveRecommendation(SupportedGame.ZeroHour);

    public bool HasOverlappingInstallationPaths => _validation.HasOverlappingPaths;

    /// <summary>
    ///     Gets whether every nonempty draft is valid and at least one supported installation remains.
    /// </summary>
    public bool CanContinue => _validation.IsValid;

    public event Action<SupportedGame>? RegistryDetectionFailed;

    public event Action<SupportedGame>? RegistryDetectionSucceeded;

    internal IAsyncRelayCommand<object?> GetBrowseCommand(SupportedGame game)
    {
        return PerGame.Select(game, BrowseGeneralsCommand, BrowseZeroHourCommand);
    }

    internal IRelayCommand GetDetectCommand(SupportedGame game)
    {
        return PerGame.Select(game, DetectGeneralsCommand, DetectZeroHourCommand);
    }

    internal string GetPath(SupportedGame game)
    {
        return PerGame.Select(game, GeneralsPath, ZeroHourPath);
    }

    internal void SetPath(SupportedGame game, string value)
    {
        switch (game)
        {
            case SupportedGame.Generals:
                GeneralsPath = value;
                break;
            case SupportedGame.ZeroHour:
                ZeroHourPath = value;
                break;
            default:
                throw PerGame.Unsupported(game, nameof(game));
        }
    }

    internal bool IsValid(SupportedGame game)
    {
        return GetValidation(game).IsValid;
    }

    internal bool HasValidationError(SupportedGame game)
    {
        return !IsValid(game) || HasOverlappingInstallationPaths;
    }

    internal string GetStatusText(SupportedGame game)
    {
        return CreateStatusText(game, GetValidation(game));
    }

    internal bool ShowProgramFilesWarning(SupportedGame game)
    {
        return IsInProgramFiles(GetValidation(game));
    }

    internal bool ShowDifferentDriveRecommendation(SupportedGame game)
    {
        return IsOnDifferentDrive(GetValidation(game));
    }

    /// <summary>
    ///     Runs first-run discovery while retaining every path that already validates.
    /// </summary>
    public void DetectAll()
    {
        LauncherInstallations detected = _installationService.DiscoverValidInstallations(
            CreateDraftInstallations(),
            _storagePaths.ExecutableDirectory);
        GeneralsPath = detected.Generals ?? GeneralsPath;
        ZeroHourPath = detected.ZeroHour ?? ZeroHourPath;
    }

    /// <summary>
    ///     Returns the canonical valid installation set represented by the current drafts.
    /// </summary>
    public LauncherInstallations CreateValidatedInstallations()
    {
        if (!CanContinue)
        {
            throw new InvalidOperationException(
                "At least one valid installation and no invalid nonempty path are required.");
        }

        return _validation.CanonicalInstallations;
    }

    private LauncherInstallations CreateDraftInstallations()
    {
        return new LauncherInstallations
        {
            Generals = NullIfWhiteSpace(GeneralsPath),
            ZeroHour = NullIfWhiteSpace(ZeroHourPath)
        };
    }

    private void Detect(SupportedGame game)
    {
        // Refresh the requested game while reserving the other configured deployment target.
        LauncherInstallations detected = _installationService.DiscoverValidInstallations(
            CreateDraftInstallations().WithPath(game, null),
            _storagePaths.ExecutableDirectory);
        string? detectedPath = detected.GetPath(game);
        if (!string.IsNullOrWhiteSpace(detectedPath))
        {
            SetPath(game, detectedPath);
            RegistryDetectionSucceeded?.Invoke(game);
            return;
        }

        RegistryDetectionFailed?.Invoke(game);
    }

    private async Task BrowseAsync(SupportedGame game, Window? owner)
    {
        if (owner == null)
        {
            return;
        }

        string currentPath = GetPath(game);
        string? selectedPath = await _filePicker.PickGameInstallationFolderAsync(
            owner,
            NullIfWhiteSpace(currentPath));
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        SetPath(game, selectedPath);
    }

    private LauncherInstallationsValidationResult ValidateInstallations()
    {
        return _installationService.ValidateInstallations(
            CreateDraftInstallations(),
            _storagePaths.ExecutableDirectory);
    }

    private string CreateStatusText(
        SupportedGame game,
        GameInstallationValidationResult validation)
    {
        if (HasOverlappingInstallationPaths)
        {
            return _stringLocalizer["OverlappingGameFolders"];
        }

        if (validation.IsValid)
        {
            return _stringLocalizer[PerGame.Select(
                game,
                "ValidGeneralsInstallation",
                "ValidZeroHourInstallation")];
        }

        return _stringLocalizer[validation.Failure == GameInstallationValidationFailure.PathMissing
            ? "ChooseDeploymentFolder"
            : "InstallationPathUnavailable"];
    }

    private bool IsInProgramFiles(GameInstallationValidationResult validation)
    {
        return validation is { IsValid: true, CanonicalPath: not null } &&
               _hostEnvironmentService.IsProtectedProgramFilesDirectory(validation.CanonicalPath);
    }

    private GameInstallationValidationResult GetValidation(SupportedGame game)
    {
        return PerGame.Select(game, _validation.GeneralsValidation, _validation.ZeroHourValidation);
    }

    private bool IsOnDifferentDrive(GameInstallationValidationResult validation)
    {
        if (validation is not { IsValid: true, CanonicalPath: not null })
        {
            return false;
        }

        try
        {
            string? executableRoot = Path.GetPathRoot(_storagePaths.ExecutableDirectory);
            string? gameRoot = Path.GetPathRoot(validation.CanonicalPath);
            return !string.IsNullOrWhiteSpace(executableRoot) &&
                   !string.IsNullOrWhiteSpace(gameRoot) &&
                   !string.Equals(executableRoot, gameRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private void NotifyInstallationStateChanged(string changedPathProperty)
    {
        OnPropertyChanged(changedPathProperty);
        OnPropertyChanged(nameof(IsGeneralsValid));
        OnPropertyChanged(nameof(IsZeroHourValid));
        OnPropertyChanged(nameof(HasGeneralsValidationError));
        OnPropertyChanged(nameof(HasZeroHourValidationError));
        OnPropertyChanged(nameof(GeneralsStatusText));
        OnPropertyChanged(nameof(ZeroHourStatusText));
        OnPropertyChanged(nameof(ShowGeneralsProgramFilesWarning));
        OnPropertyChanged(nameof(ShowZeroHourProgramFilesWarning));
        OnPropertyChanged(nameof(ShowGeneralsDifferentDriveRecommendation));
        OnPropertyChanged(nameof(ShowZeroHourDifferentDriveRecommendation));
        OnPropertyChanged(nameof(HasOverlappingInstallationPaths));
        OnPropertyChanged(nameof(CanContinue));

        foreach (LauncherGameInstallationViewModel game in Games)
        {
            game.NotifyStateChanged();
        }
    }

    private static string? NullIfWhiteSpace(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
