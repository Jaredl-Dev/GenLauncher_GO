using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Startup.Contracts;
using GenLauncherGO.Core.Startup.Models;
using GenLauncherGO.UI.Features.Launcher.Contracts;
using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.UI.Features.Startup.ViewModels;

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

    public bool IsGeneralsValid => _validation.GeneralsValidation.IsValid;

    public bool IsZeroHourValid => _validation.ZeroHourValidation.IsValid;

    public bool HasGeneralsValidationError =>
        !string.IsNullOrWhiteSpace(GeneralsPath) &&
        (!IsGeneralsValid || HasDuplicateInstallationPath);

    public bool HasZeroHourValidationError =>
        !string.IsNullOrWhiteSpace(ZeroHourPath) &&
        (!IsZeroHourValid || HasDuplicateInstallationPath);

    public string GeneralsStatusText => CreateStatusText(
        SupportedGame.Generals,
        GeneralsPath,
        _validation.GeneralsValidation);

    public string ZeroHourStatusText => CreateStatusText(
        SupportedGame.ZeroHour,
        ZeroHourPath,
        _validation.ZeroHourValidation);

    public bool ShowGeneralsProgramFilesWarning => IsInProgramFiles(_validation.GeneralsValidation);

    public bool ShowZeroHourProgramFilesWarning => IsInProgramFiles(_validation.ZeroHourValidation);

    public bool ShowGeneralsDifferentDriveRecommendation => IsOnDifferentDrive(_validation.GeneralsValidation);

    public bool ShowZeroHourDifferentDriveRecommendation => IsOnDifferentDrive(_validation.ZeroHourValidation);

    public bool HasDuplicateInstallationPath => _validation.HasDuplicatePath;

    /// <summary>
    ///     Gets whether every nonempty draft is valid and at least one supported installation remains.
    /// </summary>
    public bool CanContinue => _validation.IsValid;

    public event Action<SupportedGame>? RegistryDetectionFailed;

    public event Action<SupportedGame>? RegistryDetectionSucceeded;

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
        // A per-game detect is an independent refresh; draft values must not suppress either registry probe.
        LauncherInstallations detected = _installationService.DiscoverValidInstallations(
            new LauncherInstallations(),
            _storagePaths.ExecutableDirectory);
        if (game == SupportedGame.Generals)
        {
            if (!string.IsNullOrWhiteSpace(detected.Generals))
            {
                GeneralsPath = detected.Generals;
                RegistryDetectionSucceeded?.Invoke(SupportedGame.Generals);
                return;
            }

            RegistryDetectionFailed?.Invoke(SupportedGame.Generals);
            return;
        }

        if (!string.IsNullOrWhiteSpace(detected.ZeroHour))
        {
            ZeroHourPath = detected.ZeroHour;
            RegistryDetectionSucceeded?.Invoke(SupportedGame.ZeroHour);
            return;
        }

        RegistryDetectionFailed?.Invoke(SupportedGame.ZeroHour);
    }

    private async Task BrowseAsync(SupportedGame game, Window? owner)
    {
        if (owner == null)
        {
            return;
        }

        string currentPath = game == SupportedGame.Generals ? GeneralsPath : ZeroHourPath;
        string? selectedPath = await _filePicker.PickGameInstallationFolderAsync(
            owner,
            NullIfWhiteSpace(currentPath));
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        if (game == SupportedGame.Generals)
        {
            GeneralsPath = selectedPath;
        }
        else
        {
            ZeroHourPath = selectedPath;
        }
    }

    private LauncherInstallationsValidationResult ValidateInstallations()
    {
        return _installationService.ValidateInstallations(
            CreateDraftInstallations(),
            _storagePaths.ExecutableDirectory);
    }

    private string CreateStatusText(
        SupportedGame game,
        string path,
        GameInstallationValidationResult validation)
    {
        if (HasDuplicateInstallationPath)
        {
            return _stringLocalizer["DuplicateGameInstallation"];
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return _stringLocalizer["InstallationNotConfigured"];
        }

        if (validation.IsValid)
        {
            return _stringLocalizer[game == SupportedGame.Generals
                ? "ValidGeneralsInstallation"
                : "ValidZeroHourInstallation"];
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            _stringLocalizer["GameInstallationFilesNotFound"],
            string.Join(", ", LauncherFileSystemLayout.GetBuiltInGameExecutableNames(game)));
    }

    private bool IsInProgramFiles(GameInstallationValidationResult validation)
    {
        return validation is { IsValid: true, CanonicalPath: not null } &&
               _hostEnvironmentService.IsProtectedProgramFilesDirectory(validation.CanonicalPath);
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
        OnPropertyChanged(nameof(HasDuplicateInstallationPath));
        OnPropertyChanged(nameof(CanContinue));
    }

    private static string? NullIfWhiteSpace(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
