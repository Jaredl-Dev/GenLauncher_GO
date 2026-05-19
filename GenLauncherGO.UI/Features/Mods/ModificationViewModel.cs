using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Mods.ViewModels;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Shared.Localization;
using GenLauncherGO.UI.Shared.Themes;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.UI.Features.Mods;

/// <summary>
/// Represents bindable UI state for a launcher modification tile.
/// </summary>
internal sealed class ModificationViewModel : ObservableObject, ILaunchContentIntegrityProgressTarget
{
    // Tile construction can precede theme resource initialization during early startup and tests.
    private static readonly IBrush _fallbackActiveBrush = new SolidColorBrush(Color.FromRgb(186, 255, 12));

    private static readonly IBrush _fallbackBorderBrush = new SolidColorBrush(Color.FromRgb(0, 227, 255));

    private static readonly IBrush _fallbackDefaultTextBrush = Brushes.White;

    private static readonly IBrush _fallbackDownloadTextBrush = Brushes.Black;

    private static readonly IBrush _fallbackInactiveBrush = Brushes.DarkGray;

    private static readonly IBrush _fallbackProgressBackgroundBrush = Brushes.Black;

    private static readonly IBrush _fallbackActiveProgressBrush = new SolidColorBrush(Color.FromRgb(37, 52, 255));

    private readonly LauncherContent _containerModification;

    private readonly LauncherRuntimeContext _launcherContext;

    private readonly LauncherPackageActivityService _packageActivityService;

    private readonly ModificationTileImageProvider _imageProvider;

    private readonly ILauncherStringLocalizer _stringLocalizer;

    private LauncherContentVersion? _selectedVersion;

    private bool _readyToRun = true;

    private IImage? _imageSource;

    private IImage? _selectedImageSource;

    private bool _isSelected;

    private bool _isVersionSelectorVisible;

    private bool _isVersionActionVisible;

    private bool _isDragAndDropVisible;

    private bool _isUpdateButtonVisible = true;

    private bool _isSupportButtonVisible = true;

    private bool _isNetworkInfoVisible = true;

    private bool _isChangeLogVisible = true;

    private Thickness _imageBorderThickness = new(0);

    private IBrush _progressBackground = _fallbackProgressBackgroundBrush;

    private IBrush _progressForeground = _fallbackActiveProgressBrush;

    private IBrush _progressBorderBrush = _fallbackInactiveBrush;

    private IBrush _progressTextForeground = _fallbackDefaultTextBrush;

    private double _progressValue;

    private string _progressMessage = string.Empty;

    private string _updateButtonContent;

    private string _supportButtonContent;

    private string _changeLogButtonContent;

    private string _networkInfoButtonContent;

    private string _versionActionContent;

    private bool _updateButtonEnabled = true;

    private bool _updateButtonBlinking;

    private bool _supportButtonBlinking;

    private bool _integrityProgressActive;

    private bool _forwardedChildPackageActivityActive;

    private bool _isVersionSelectorEnabled = true;

    private ModificationVersionSelection? _selectedVersionOption;

    public ModificationViewModel(
        LauncherContent modification,
        ModificationImageSourceFactory imageSourceFactory,
        LauncherRuntimeContext launcherContext,
        IModificationImageFileService modificationImageFileService,
        ILauncherStringLocalizer stringLocalizer,
        LauncherPackageActivityService packageActivityService,
        ILogger<ModificationViewModel> logger)
    {
        _launcherContext = launcherContext ?? throw new ArgumentNullException(nameof(launcherContext));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _packageActivityService = packageActivityService ??
                                  throw new ArgumentNullException(nameof(packageActivityService));
        _imageProvider = new ModificationTileImageProvider(
            imageSourceFactory,
            launcherContext,
            modificationImageFileService,
            logger);
        _containerModification = modification ?? throw new ArgumentNullException(nameof(modification));
        RefreshSelectedVersion();

        _updateButtonContent = _stringLocalizer["Update"];
        _supportButtonContent = _stringLocalizer["Donate"];
        _changeLogButtonContent = _stringLocalizer["ChangelogOnly"];
        _networkInfoButtonContent = _stringLocalizer["PlayOnline"];
        _versionActionContent = _stringLocalizer["RemoveFromList"];

        InitializeVisualState();
    }

    /// <summary>
    /// Occurs when package download or repair activity state changes for this tile.
    /// </summary>
    public event EventHandler? PackageActivityChanged;

    public LauncherContent ContainerModification => _containerModification;

    public LauncherContentVersion LatestVersion => ContainerModification.LatestVersion;

    public LauncherContentVersion? SelectedVersion => _selectedVersion;

    public string NameInfo => ContainerModification.Name;

    public string LatestVersionInfo =>
        ContainerModification.ModificationType == ModificationType.Advertising
            ? LatestVersion.Version
            : String.Concat(_stringLocalizer["LatestVersion"], LatestVersion.Version);

    public bool ReadyToRun => _readyToRun;

    public bool CanSetImage =>
        ContainerModification.ModificationType != ModificationType.Advertising &&
        LatestVersion.EffectiveContentSourceKind == ContentSourceKind.Manual;

    public bool CanOpenModDb => !String.IsNullOrEmpty(ContainerModification.LatestVersion.ModDBLink);

    public bool CanOpenDiscord => !String.IsNullOrEmpty(ContainerModification.LatestVersion.DiscordLink);

    public bool LocalMod =>
        ContainerModification.ModificationType == ModificationType.Mod &&
        !ContainerModification.Versions.Any(version =>
            version.EffectiveContentSourceKind is
                ContentSourceKind.ManagedS3 or ContentSourceKind.ManagedSingleFile);

    public LauncherContentVersion? ActiveIntegrityVersion => SelectedVersion ?? LatestVersion;

    public bool CanReportIntegrityProgress => ActiveIntegrityVersion != null;

    /// <summary>
    /// Gets a value indicating whether package download, repair, or forwarded child activity is active.
    /// </summary>
    public bool HasActivePackageActivity =>
        _packageActivityService.GetActiveDownloadTask(this) is { IsCompleted: false } ||
        _integrityProgressActive ||
        _forwardedChildPackageActivityActive;

    public ObservableCollection<ModificationVersionSelection> VersionOptions { get; } = new();

    public ModificationVersionSelection? SelectedVersionOption
    {
        get => _selectedVersionOption;
        set => SetProperty(ref _selectedVersionOption, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(IsSelectedOrAdvertising));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether selection-gated actions should be shown for this tile.
    /// Advertising actions remain available without changing the selected game content.
    /// </summary>
    public bool IsSelectedOrAdvertising =>
        IsSelected || ContainerModification.ModificationType == ModificationType.Advertising;

    public IImage? ImageSource
    {
        get => _imageSource;
        private set
        {
            if (SetProperty(ref _imageSource, value))
            {
                OnPropertyChanged(nameof(HasImage));
            }
        }
    }

    public IImage? SelectedImageSource
    {
        get => _selectedImageSource;
        private set
        {
            if (SetProperty(ref _selectedImageSource, value))
            {
                OnPropertyChanged(nameof(HasImage));
            }
        }
    }

    public bool HasImage => ImageSource != null || SelectedImageSource != null;

    public bool IsVersionSelectorVisible
    {
        get => _isVersionSelectorVisible;
        private set => SetProperty(ref _isVersionSelectorVisible, value);
    }

    public bool IsVersionActionVisible
    {
        get => _isVersionActionVisible;
        private set => SetProperty(ref _isVersionActionVisible, value);
    }

    public bool IsDragAndDropVisible
    {
        get => _isDragAndDropVisible;
        private set => SetProperty(ref _isDragAndDropVisible, value);
    }

    public bool IsUpdateButtonVisible
    {
        get => _isUpdateButtonVisible;
        private set => SetProperty(ref _isUpdateButtonVisible, value);
    }

    public bool IsSupportButtonVisible
    {
        get => _isSupportButtonVisible;
        private set => SetProperty(ref _isSupportButtonVisible, value);
    }

    public bool IsNetworkInfoVisible
    {
        get => _isNetworkInfoVisible;
        private set => SetProperty(ref _isNetworkInfoVisible, value);
    }

    public bool IsChangeLogVisible
    {
        get => _isChangeLogVisible;
        private set => SetProperty(ref _isChangeLogVisible, value);
    }

    public Thickness ImageBorderThickness
    {
        get => _imageBorderThickness;
        private set => SetProperty(ref _imageBorderThickness, value);
    }

    public IBrush ProgressBackground
    {
        get => _progressBackground;
        private set => SetProperty(ref _progressBackground, value);
    }

    public IBrush ProgressForeground
    {
        get => _progressForeground;
        private set => SetProperty(ref _progressForeground, value);
    }

    public IBrush ProgressBorderBrush
    {
        get => _progressBorderBrush;
        private set => SetProperty(ref _progressBorderBrush, value);
    }

    public IBrush ProgressTextForeground
    {
        get => _progressTextForeground;
        private set => SetProperty(ref _progressTextForeground, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public string ProgressMessage
    {
        get => _progressMessage;
        private set => SetProperty(ref _progressMessage, value);
    }

    public string UpdateButtonContent
    {
        get => _updateButtonContent;
        private set => SetProperty(ref _updateButtonContent, value);
    }

    public string SupportButtonContent
    {
        get => _supportButtonContent;
        private set => SetProperty(ref _supportButtonContent, value);
    }

    public string ChangeLogButtonContent
    {
        get => _changeLogButtonContent;
        private set => SetProperty(ref _changeLogButtonContent, value);
    }

    public string NetworkInfoButtonContent
    {
        get => _networkInfoButtonContent;
        private set => SetProperty(ref _networkInfoButtonContent, value);
    }

    public bool UpdateButtonEnabled
    {
        get => _updateButtonEnabled;
        private set => SetProperty(ref _updateButtonEnabled, value);
    }

    public bool UpdateButtonBlinking
    {
        get => _updateButtonBlinking;
        private set => SetProperty(ref _updateButtonBlinking, value);
    }

    public bool SupportButtonBlinking
    {
        get => _supportButtonBlinking;
        private set => SetProperty(ref _supportButtonBlinking, value);
    }

    public bool IsVersionSelectorEnabled
    {
        get => _isVersionSelectorEnabled;
        private set => SetProperty(ref _isVersionSelectorEnabled, value);
    }

    public void RefreshFromModel()
    {
        RefreshSelectedVersion();
        OnStatePropertiesChanged();
    }

    public void SetDragAndDropMod()
    {
        IsDragAndDropVisible = true;
    }

    public void RemoveDragAndDropMod()
    {
        IsDragAndDropVisible = false;
    }

    public void RefreshPresentation()
    {
        ApplyPackageActivityVisualState(HasActivePackageActivity);
        RefreshImages();
    }

    private void ApplyPackageActivityVisualState(bool isActive)
    {
        if (!isActive)
        {
            ProgressBackground = ProgressBackgroundBrush;
            ProgressForeground = ActiveBrush;
            ProgressBorderBrush = InactiveBrush;
            ProgressTextForeground = DefaultTextBrush;
            return;
        }

        ProgressBackground = ActiveProgressBrush;
        ProgressForeground = ActiveBrush;
        ProgressBorderBrush = BorderBrush;
        ProgressTextForeground = DownloadTextBrush;
    }

    /// <summary>
    /// Updates bindable tile state from current modification and download state.
    /// </summary>
    public void RefreshFromModelAndPresentation()
    {
        if (_packageActivityService.GetActiveDownloadTask(this) is not { IsCompleted: false })
        {
            ResetDownloadVisuals();

            RefreshFromModel();

            if (ContainerModification.ModificationType != ModificationType.Advertising)
            {
                UpdateComboBox();
                SelectItemInComboBox();
            }
            else
            {
                HideVersionSelector();
            }
        }

        RefreshContentButtonAvailability();
        RefreshImages();
    }

    public void UpdateComboBox()
    {
        if (LatestVersion.Installation.Installed)
        {
            UpdateButtonContent = _stringLocalizer["UpToDate"];
            UpdateButtonEnabled = false;
            UpdateButtonBlinking = false;
        }
        else
        {
            UpdateButtonContent = _stringLocalizer["Update"];
            UpdateButtonEnabled = true;
            UpdateButtonBlinking = false;
        }

        VersionOptions.Clear();
        foreach (LauncherContentVersion version in ContainerModification.Versions
                     .Where(modificationVersion => modificationVersion.Installation.Installed)
                     .OrderBy(modificationVersion => modificationVersion))
        {
            VersionOptions.Add(new ModificationVersionSelection(
                version,
                version.Version,
                this));
        }
    }

    public void SelectItemInComboBox()
    {
        if (ContainerModification.Versions.Count == 0)
        {
            IsVersionSelectorEnabled = false;
            SelectedVersionOption = null;
            return;
        }

        if (ContainerModification.Versions.Count == 1 && !LatestVersion.Installation.Installed)
        {
            ApplyInstallAvailableState();
            SelectedVersionOption = null;
            return;
        }

        IsVersionSelectorEnabled = true;
        string versionString;
        if (ReadyToRun)
        {
            versionString = SelectedVersion?.Version ?? string.Empty;
        }
        else
        {
            LauncherContentVersion selectedVersion = SelectLatestInstalledVersion();
            OnStatePropertiesChanged();
            versionString = selectedVersion.Version;
        }

        SelectedVersionOption = VersionOptions.FirstOrDefault(selection =>
            String.Equals(selection.VersionName, versionString, StringComparison.Ordinal));
    }

    /// <summary>
    /// Projects the lifecycle owner's single terminal package result onto this tile.
    /// </summary>
    public void CompletePackageActivityPresentation(PackageDownloadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        try
        {
            ResetDownloadVisuals();
            RefreshFromModel();
            if (ContainerModification.ModificationType != ModificationType.Advertising)
            {
                UpdateComboBox();
                SelectItemInComboBox();
            }
            else
            {
                HideVersionSelector();
            }

            RefreshContentButtonAvailability();
            RefreshImages();
            ApplyTerminalDownloadResult(result);
        }
        finally
        {
            OnPackageActivityChanged();
        }
    }

    private void ApplyTerminalDownloadResult(PackageDownloadResult result)
    {
        switch (result.Status)
        {
            case PackageDownloadStatus.Succeeded:
                ApplyPackageActivityVisualState(isActive: false);
                break;
            case PackageDownloadStatus.Canceled:
                SetStatusMessage(_stringLocalizer["Canceled"]);
                ApplyPackageActivityVisualState(isActive: false);
                break;
            case PackageDownloadStatus.RecoverableFailure:
                ShowDownloadFailure(result.Message);
                break;
            case PackageDownloadStatus.UnexpectedFailure:
                ShowDownloadFailure(_stringLocalizer["UnexpectedErrorDetails"]);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(result),
                    result.Status,
                    "Unknown package download status.");
        }
    }

    private void ShowDownloadFailure(string message)
    {
        SetStatusMessage(String.Concat(_stringLocalizer["Error"], message));
        ApplyPackageActivityVisualState(isActive: false);
    }

    /// <summary>
    /// Prepares tile state for package download state.
    /// </summary>
    public void BeginPackageActivityPresentation()
    {
        UpdateButtonContent = _stringLocalizer["Pause"];
        UpdateButtonBlinking = false;
        IsVersionSelectorEnabled = false;
        _readyToRun = false;
        OnStatePropertiesChanged();

        RefreshContentButtonAvailability();
        ApplyPackageActivityVisualState(isActive: true);
        OnPackageActivityChanged();
    }

    public string VersionActionContent
    {
        get => _versionActionContent;
        private set => SetProperty(ref _versionActionContent, value);
    }

    /// <summary>
    /// Updates the active download action to reflect whether the transfer is paused.
    /// </summary>
    public void SetPackageDownloadPaused(bool isPaused)
    {
        UpdateButtonContent = _stringLocalizer[isPaused ? "Resume" : "Pause"];
    }

    /// <summary>
    /// Starts the one-time install notification for a newly added repository modification.
    /// </summary>
    public void NotifyInstallAvailable()
    {
        if (ContainerModification.ModificationType == ModificationType.Mod &&
            !LatestVersion.Installation.Installed)
        {
            UpdateButtonBlinking = true;
        }
    }

    public void SetStatusMessage(string message)
    {
        ProgressMessage = message;
    }

    public void SetUpdateButtonEnabled(bool isEnabled)
    {
        UpdateButtonEnabled = isEnabled;
    }

    public void SetSupportButtonBlinking(bool isBlinking)
    {
        SupportButtonBlinking = isBlinking;
    }

    public void ReportPackageProgress(string message, int percentage)
    {
        ProgressMessage = message;
        ProgressValue = percentage;
        if (HasActivePackageActivity)
        {
            OnPackageActivityChanged();
        }
    }

    private void ApplyInstallAvailableState()
    {
        IsVersionSelectorEnabled = false;
        UpdateButtonContent = _stringLocalizer["Install"];
        _readyToRun = false;
        OnStatePropertiesChanged();
    }

    public void BeginIntegrityProgress(string message)
    {
        _integrityProgressActive = true;
        ApplyPackageActivityVisualState(isActive: true);
        ReportPackageProgress(message, 0);
        OnPackageActivityChanged();
    }

    public void ReportIntegrityProgress(string message, int percentage)
    {
        ReportPackageProgress(message, percentage);
    }

    public void CompleteIntegrityProgress()
    {
        _integrityProgressActive = false;
        RefreshFromModelAndPresentation();
        ApplyPackageActivityVisualState(isActive: false);
        OnPackageActivityChanged();
    }

    /// <summary>
    /// Mirrors child-content package activity onto this parent tile.
    /// </summary>
    public void ReportForwardedChildPackageActivity(string message, int percentage)
    {
        if (_packageActivityService.GetActiveDownloadTask(this) is { IsCompleted: false } ||
            _integrityProgressActive)
        {
            return;
        }

        _forwardedChildPackageActivityActive = true;
        ApplyPackageActivityVisualState(isActive: true);
        ReportPackageProgress(message, percentage);
        OnPackageActivityChanged();
    }

    /// <summary>
    /// Clears mirrored child-content package activity from this parent tile.
    /// </summary>
    public void CompleteForwardedChildPackageActivity()
    {
        if (!_forwardedChildPackageActivityActive)
        {
            return;
        }

        _forwardedChildPackageActivityActive = false;
        RefreshFromModelAndPresentation();
        ApplyPackageActivityVisualState(isActive: false);
        OnPackageActivityChanged();
    }

    private IBrush ActiveBrush => ResolveBrush(_launcherContext.Colors.GenLauncherActiveColor, _fallbackActiveBrush);

    private IBrush BorderBrush => ResolveBrush(_launcherContext.Colors.GenLauncherBorderColor, _fallbackBorderBrush);

    private IBrush DefaultTextBrush =>
        ResolveBrush(_launcherContext.Colors.GenLauncherDefaultTextColor, _fallbackDefaultTextBrush);

    private IBrush DownloadTextBrush =>
        ResolveBrush(_launcherContext.Colors.GenLauncherDownloadTextColor, _fallbackDownloadTextBrush);

    private IBrush InactiveBrush =>
        ResolveBrush(_launcherContext.Colors.GenLauncherInactiveBorder, _fallbackInactiveBrush);

    private IBrush ProgressBackgroundBrush =>
        ResolveBrush(_launcherContext.Colors.GenLauncherDarkBackGround, _fallbackProgressBackgroundBrush);

    private IBrush ActiveProgressBrush => _launcherContext.Colors.GenLauncherButtonSelectionColor == default
        ? _fallbackActiveProgressBrush
        : new SolidColorBrush(_launcherContext.Colors.GenLauncherButtonSelectionColor);

    private void InitializeVisualState()
    {
        ResetDownloadVisuals();
        RefreshFromModelAndPresentation();
    }

    private void ResetDownloadVisuals()
    {
        ProgressValue = 0;
        ProgressMessage = string.Empty;
        IsUpdateButtonVisible = true;
        UpdateButtonContent = _stringLocalizer["Update"];
        SupportButtonContent = _stringLocalizer["Donate"];
        ChangeLogButtonContent = _stringLocalizer["ChangelogOnly"];
        NetworkInfoButtonContent = _stringLocalizer["PlayOnline"];
        ProgressTextForeground = DefaultTextBrush;

        if (ContainerModification.ModificationType != ModificationType.Advertising)
        {
            return;
        }

        UpdateButtonContent = _stringLocalizer["AdvertisingDonationAlerts"];
        if (String.IsNullOrEmpty(ContainerModification.LatestVersion.SimpleDownloadLink))
        {
            IsUpdateButtonVisible = false;
        }

        ChangeLogButtonContent = _stringLocalizer["AdvertisingBoostyLink"];
        NetworkInfoButtonContent = _stringLocalizer["AdvertisingYouTubeRuLink"];
    }

    private void HideVersionSelector()
    {
        IsVersionSelectorVisible = false;
    }

    private void RefreshContentButtonAvailability()
    {
        bool isAdvertising = ContainerModification.ModificationType == ModificationType.Advertising;
        bool hasActiveDownload =
            _packageActivityService.GetActiveDownloadTask(this) is { IsCompleted: false };
        IsVersionSelectorVisible = !isAdvertising &&
                                   ContainerModification.Installed &&
                                   !hasActiveDownload;
        IsVersionActionVisible = !isAdvertising &&
                                 (hasActiveDownload ||
                                  ContainerModification.ModificationType == ModificationType.Mod &&
                                  !ContainerModification.Installed);
        VersionActionContent = _stringLocalizer[
            hasActiveDownload
                ? "CancelDownloadAction"
                : "RemoveFromList"];
        IsChangeLogVisible = !String.IsNullOrEmpty(ContainerModification.LatestVersion.NewsLink);
        IsNetworkInfoVisible = !String.IsNullOrEmpty(ContainerModification.LatestVersion.NetworkInfo);
        IsSupportButtonVisible = !String.IsNullOrEmpty(ContainerModification.LatestVersion.SupportLink);
    }

    private void RefreshImages()
    {
        ImageSource = _imageProvider.LoadGrayscaleImage(
            ContainerModification,
            LatestVersion,
            LocalMod);
        SelectedImageSource = _imageProvider.LoadColorImage(
            ContainerModification,
            LatestVersion,
            LocalMod);
        ImageBorderThickness = ImageSource == null && SelectedImageSource == null
            ? new Thickness(0)
            : new Thickness(2);
    }

    private void RefreshSelectedVersion()
    {
        _selectedVersion = ContainerModification.GetSelectedVersion();
        if (_selectedVersion != null)
        {
            _selectedVersion.Installation.IsSelected = true;
        }
    }

    private LauncherContentVersion SelectLatestInstalledVersion()
    {
        if (_selectedVersion != null)
        {
            _selectedVersion.Installation.IsSelected = false;
        }

        _selectedVersion = ContainerModification.LatestInstalledVersion ??
                           throw new InvalidOperationException(
                               "An installed version is required before it can be selected.");
        _selectedVersion.Installation.IsSelected = true;
        _readyToRun = true;
        return _selectedVersion;
    }

    private void OnStatePropertiesChanged()
    {
        OnPropertyChanged(nameof(ContainerModification));
        OnPropertyChanged(nameof(LatestVersion));
        OnPropertyChanged(nameof(SelectedVersion));
        OnPropertyChanged(nameof(NameInfo));
        OnPropertyChanged(nameof(LatestVersionInfo));
        OnPropertyChanged(nameof(ReadyToRun));
        OnPropertyChanged(nameof(CanSetImage));
        OnPropertyChanged(nameof(CanOpenModDb));
        OnPropertyChanged(nameof(CanOpenDiscord));
        OnPropertyChanged(nameof(LocalMod));
        OnPropertyChanged(nameof(ActiveIntegrityVersion));
        OnPropertyChanged(nameof(CanReportIntegrityProgress));
    }

    private void OnPackageActivityChanged()
    {
        OnPropertyChanged(nameof(HasActivePackageActivity));
        PackageActivityChanged?.Invoke(this, EventArgs.Empty);
    }

    private static IBrush ResolveBrush(IBrush? brush, IBrush fallback)
    {
        return brush ?? fallback;
    }
}
