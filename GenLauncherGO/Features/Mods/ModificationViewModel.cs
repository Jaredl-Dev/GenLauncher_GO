using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using GenLauncherGO.Features.Integrity;
using GenLauncherGO.Features.Startup;
using GenLauncherGO.Features.Updating;
using GenLauncherGO.Shared.Localization;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Features.Mods;

/// <summary>
///     Represents bindable UI state for a launcher modification tile.
/// </summary>
internal sealed class ModificationViewModel : ObservableObject, ILaunchContentIntegrityProgressTarget
{
    private readonly ModificationTileImageProvider _imageProvider;

    private readonly LauncherRuntimeContext _launcherContext;

    private readonly LauncherPackageActivityService _packageActivityService;

    private readonly ILauncherStringLocalizer _stringLocalizer;

    private bool _forwardedChildPackageActivityActive;
    private bool _integrityProgressActive;
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
        ContainerModification = modification ?? throw new ArgumentNullException(nameof(modification));
        ProgressBackground = ProgressBackgroundBrush;
        ProgressForeground = ActiveProgressBrush;
        ProgressBorderBrush = InactiveBrush;
        ProgressTextForeground = DefaultTextBrush;
        RefreshSelectedVersion();

        UpdateButtonContent = _stringLocalizer["Update"];
        SupportButtonContent = _stringLocalizer["Donate"];
        ChangeLogButtonContent = _stringLocalizer["ChangelogOnly"];
        NetworkInfoButtonContent = _stringLocalizer["PlayOnline"];
        VersionActionContent = _stringLocalizer["RemoveFromList"];

        InitializeVisualState();
    }

    public LauncherContent ContainerModification { get; }

    /// <summary>
    ///     Gets whether this card uses the compact patch and add-on presentation instead of the artwork presentation.
    /// </summary>
    public bool UsesCompactTileLayout =>
        ContainerModification.ModificationType is ModificationType.Addon or ModificationType.Patch;

    public LauncherContentVersion LatestVersion => ContainerModification.LatestVersion;

    public LauncherContentVersion? SelectedVersion { get; private set; }

    public string NameInfo => ContainerModification.Name;

    public string LatestVersionInfo =>
        ContainerModification.ModificationType == ModificationType.Advertising
            ? LatestVersion.Version
            : string.Concat(_stringLocalizer["LatestVersion"], LatestVersion.Version);

    public bool ReadyToRun { get; private set; } = true;

    /// <summary>
    ///     Gets the active version for this tile, falling back from the selected version to the latest version.
    /// </summary>
    public LauncherContentVersion ActiveVersion => SelectedVersion ?? LatestVersion;

    public bool CanSetImage =>
        ContainerModification.ModificationType == ModificationType.Mod &&
        LatestVersion.EffectiveContentSourceKind == ContentSourceKind.Manual;

    public bool CanOpenFolder =>
        ContainerModification.ModificationType != ModificationType.Advertising &&
        ActiveVersion.EffectiveContentSourceKind == ContentSourceKind.Manual;

    public bool CanOpenModDb => !string.IsNullOrEmpty(LatestVersion.ModDBLink);

    public bool CanOpenDiscord => !string.IsNullOrEmpty(LatestVersion.DiscordLink);

    private bool HasActionMenuItems => CanSetImage || CanOpenFolder;

    private bool HasLinkMenuItems => CanOpenModDb || CanOpenDiscord;

    public bool HasContextMenuSeparator => HasActionMenuItems && HasLinkMenuItems;

    public bool HasContextMenu => HasActionMenuItems || HasLinkMenuItems;

    public bool LocalMod =>
        ContainerModification.ModificationType == ModificationType.Mod &&
        !ContainerModification.Versions.Any(version =>
            version.EffectiveContentSourceKind.IsManagedRemote());

    /// <summary>
    ///     Gets the palette this modification publishes for the launcher shell, or <see langword="null" /> for none.
    /// </summary>
    public LauncherContentTheme? PublishedTheme => ActiveVersion.Theme;

    /// <summary>
    ///     Gets a value indicating whether package download, repair, or forwarded child activity is active.
    /// </summary>
    public bool HasActivePackageActivity =>
        _packageActivityService.GetActiveDownloadTask(this) is { IsCompleted: false } ||
        _integrityProgressActive ||
        _forwardedChildPackageActivityActive;

    /// <summary>
    ///     Gets whether any version of this modification has a suspended download.
    /// </summary>
    internal bool HasSuspendedDownload =>
        ContainerModification.Versions.Any(version => version.Installation.DownloadSuspended);

    public ObservableCollection<ModificationVersionSelection> VersionOptions { get; } = [];

    public ModificationVersionSelection? SelectedVersionOption
    {
        get => _selectedVersionOption;
        set
        {
            if (SetProperty(ref _selectedVersionOption, value))
            {
                if (value != null)
                {
                    SelectVersion(value.SelectedVersion);
                }
                else
                {
                    OnStatePropertiesChanged();
                }
            }
        }
    }

    public bool IsSelected
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsSelectedOrAdvertising));
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether selection-gated actions should be shown for this tile.
    ///     Advertising actions remain available without changing the selected game content.
    /// </summary>
    public bool IsSelectedOrAdvertising =>
        IsSelected || ContainerModification.ModificationType == ModificationType.Advertising;

    public IImage? ImageSource
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasImage));
            }
        }
    }

    public IImage? SelectedImageSource
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasImage));
            }
        }
    }

    public bool HasImage => ImageSource != null || SelectedImageSource != null;

    public bool IsVersionSelectorVisible
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool IsVersionActionVisible
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool IsDragAndDropVisible
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool IsUpdateButtonVisible
    {
        get;
        private set => SetProperty(ref field, value);
    } = true;

    public bool IsSupportButtonVisible
    {
        get;
        private set => SetProperty(ref field, value);
    } = true;

    public bool IsNetworkInfoVisible
    {
        get;
        private set => SetProperty(ref field, value);
    } = true;

    public bool IsChangeLogVisible
    {
        get;
        private set => SetProperty(ref field, value);
    } = true;

    public Thickness ImageBorderThickness
    {
        get;
        private set => SetProperty(ref field, value);
    } = new(0);

    public IBrush ProgressBackground
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public IBrush ProgressForeground
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public IBrush ProgressBorderBrush
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public IBrush ProgressTextForeground
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public double ProgressValue
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public string ProgressMessage
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    public string UpdateButtonContent
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public string SupportButtonContent
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public string ChangeLogButtonContent
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public string NetworkInfoButtonContent
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool UpdateButtonEnabled
    {
        get;
        private set => SetProperty(ref field, value);
    } = true;

    public bool UpdateButtonBlinking
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool SupportButtonBlinking
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool IsVersionSelectorEnabled
    {
        get;
        private set => SetProperty(ref field, value);
    } = true;

    public string VersionActionContent
    {
        get;
        private set => SetProperty(ref field, value);
    }

    // Tiles push brushes into bindable properties instead of resolving DynamicResource, so they read the theme
    // directly rather than through application resources.
    private IBrush ActiveBrush => _launcherContext.Colors.GenLauncherActiveColor;

    private IBrush BorderBrush => _launcherContext.Colors.GenLauncherBorderColor;

    private IBrush DefaultTextBrush => _launcherContext.Colors.GenLauncherDefaultTextColor;

    private IBrush DownloadTextBrush => _launcherContext.Colors.GenLauncherDownloadTextColor;

    private IBrush InactiveBrush => _launcherContext.Colors.GenLauncherInactiveBorder;

    private IBrush ProgressBackgroundBrush => _launcherContext.Colors.GenLauncherDarkBackGround;

    private IBrush ActiveProgressBrush =>
        new ImmutableSolidColorBrush(_launcherContext.Colors.GenLauncherButtonSelectionColor);

    public LauncherContentVersion ActiveIntegrityVersion => ActiveVersion;

    public void BeginIntegrityProgress(string message)
    {
        _integrityProgressActive = true;
        ApplyPackageActivityVisualState(true);
        ReportPackageProgress(message, 0);
    }

    public void ReportIntegrityProgress(string message, int percentage)
    {
        ReportPackageProgress(message, percentage);
    }

    public void CompleteIntegrityProgress()
    {
        _integrityProgressActive = false;
        RefreshFromModelAndPresentation();
        ApplyPackageActivityVisualState(false);
        OnPackageActivityChanged();
    }

    /// <summary>
    ///     Occurs when package download or repair activity state changes for this tile.
    /// </summary>
    public event EventHandler? PackageActivityChanged;

    /// <summary>
    ///     Loads the cached shell artwork published alongside <see cref="PublishedTheme" />.
    /// </summary>
    public IImageBrush? LoadPublishedThemeBackground()
    {
        return _imageProvider.LoadThemeBackground(
            ContainerModification,
            ActiveVersion);
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
    ///     Updates bindable tile state from current modification and download state.
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

            RestoreSuspendedDownload();
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
        if (!ReadyToRun)
        {
            LauncherContentVersion latestInstalled = ContainerModification.LatestInstalledVersion ??
                throw new InvalidOperationException(
                    "An installed version is required before it can be selected.");
            SelectVersion(latestInstalled);
            return;
        }

        SelectedVersionOption = VersionOptions.FirstOrDefault(selection =>
            string.Equals(selection.VersionName, SelectedVersion?.Version ?? string.Empty, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Projects the lifecycle owner's single terminal package result onto this tile.
    /// </summary>
    public void CompletePackageActivityPresentation(PackageDownloadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        // Captured before the reset below clears it, because a suspended download restores this exact position.
        double progressAtCompletion = ProgressValue;
        try
        {
            if (result.Status is PackageDownloadStatus.Succeeded or PackageDownloadStatus.Canceled)
            {
                ClearSuspendedDownload();
            }

            RefreshFromModelAndPresentation();
            ApplyTerminalDownloadResult(result, progressAtCompletion);
        }
        finally
        {
            OnPackageActivityChanged();
        }
    }

    private void ApplyTerminalDownloadResult(PackageDownloadResult result, double progressAtCompletion)
    {
        switch (result.Status)
        {
            case PackageDownloadStatus.Succeeded:
                ApplyPackageActivityVisualState(false);
                break;
            case PackageDownloadStatus.Canceled:
                SetStatusMessage(_stringLocalizer["Canceled"]);
                ApplyPackageActivityVisualState(false);
                break;
            case PackageDownloadStatus.Suspended:
                RecordSuspendedDownload(progressAtCompletion);
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

    /// <summary>
    ///     Marks the version's partial content as kept and leaves the tile showing where the transfer stopped.
    /// </summary>
    private void RecordSuspendedDownload(double progressAtCompletion)
    {
        LauncherContentVersion version = LatestVersion;
        version.Installation.DownloadSuspended = true;
        version.Installation.SuspendedProgressPercentage = progressAtCompletion;

        ShowSuspendedDownload(progressAtCompletion);
        RefreshContentButtonAvailability();
    }

    /// <summary>
    ///     Restores the paused progress a previous session left behind, so the tile reopens where it stopped.
    /// </summary>
    private void RestoreSuspendedDownload()
    {
        LauncherContentVersion? version = ContainerModification.Versions
            .FirstOrDefault(version => version.Installation.DownloadSuspended);
        if (version != null)
        {
            ShowSuspendedDownload(version.Installation.SuspendedProgressPercentage);
        }
    }

    private void ShowSuspendedDownload(double progressPercentage)
    {
        ProgressValue = progressPercentage;
        SetStatusMessage(_stringLocalizer["Paused"]);
        UpdateButtonContent = _stringLocalizer["Resume"];
        ApplyPackageActivityVisualState(false);
        IsUpdateButtonVisible = true;
        SetUpdateButtonEnabled(true);
    }

    /// <summary>
    ///     Drops the suspended marker once the version is no longer waiting to be resumed.
    /// </summary>
    private void ClearSuspendedDownload()
    {
        foreach (LauncherContentVersion version in ContainerModification.Versions)
        {
            version.Installation.DownloadSuspended = false;
            version.Installation.SuspendedProgressPercentage = 0;
        }
    }

    private void ShowDownloadFailure(string message)
    {
        SetStatusMessage(string.Concat(_stringLocalizer["Error"], message));
        ApplyPackageActivityVisualState(false);
    }

    /// <summary>
    ///     Prepares tile state for package download state.
    /// </summary>
    public void BeginPackageActivityPresentation()
    {
        // Starting a transfer settles whatever a previous session suspended, whether it resumes or restarts it.
        ClearSuspendedDownload();
        UpdateButtonContent = _stringLocalizer["Pause"];
        UpdateButtonBlinking = false;
        IsVersionSelectorEnabled = false;
        ReadyToRun = false;
        OnStatePropertiesChanged();

        RefreshContentButtonAvailability();
        ApplyPackageActivityVisualState(true);
        OnPackageActivityChanged();
    }

    /// <summary>
    ///     Updates the active download action to reflect whether the transfer is paused.
    /// </summary>
    public void SetPackageDownloadPaused(bool isPaused)
    {
        UpdateButtonContent = _stringLocalizer[isPaused ? "Resume" : "Pause"];
    }

    /// <summary>
    ///     Starts the one-time install notification for a newly added repository modification.
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
        ReadyToRun = false;
        OnStatePropertiesChanged();
    }

    /// <summary>
    ///     Mirrors child-content package activity onto this parent tile.
    /// </summary>
    public void ReportForwardedChildPackageActivity(string message, int percentage)
    {
        if (_packageActivityService.GetActiveDownloadTask(this) is { IsCompleted: false } ||
            _integrityProgressActive)
        {
            return;
        }

        _forwardedChildPackageActivityActive = true;
        ApplyPackageActivityVisualState(true);
        ReportPackageProgress(message, percentage);
    }

    /// <summary>
    ///     Clears mirrored child-content package activity from this parent tile.
    /// </summary>
    public void CompleteForwardedChildPackageActivity()
    {
        if (!_forwardedChildPackageActivityActive)
        {
            return;
        }

        _forwardedChildPackageActivityActive = false;
        RefreshFromModelAndPresentation();
        ApplyPackageActivityVisualState(false);
        OnPackageActivityChanged();
    }

    private void InitializeVisualState()
    {
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
        if (string.IsNullOrEmpty(ContainerModification.LatestVersion.SimpleDownloadLink))
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
        bool isDownloadingOrSuspended = hasActiveDownload || HasSuspendedDownload;
        IsVersionSelectorVisible = !isAdvertising &&
                                   ContainerModification.Installed &&
                                   !isDownloadingOrSuspended;
        IsVersionActionVisible = !isAdvertising &&
                                 (isDownloadingOrSuspended ||
                                  (ContainerModification.ModificationType == ModificationType.Mod &&
                                   !ContainerModification.Installed));
        VersionActionContent = _stringLocalizer[
            isDownloadingOrSuspended
                ? "CancelDownloadAction"
                : "RemoveFromList"];
        IsChangeLogVisible = !string.IsNullOrEmpty(ContainerModification.LatestVersion.NewsLink);
        IsNetworkInfoVisible = !string.IsNullOrEmpty(ContainerModification.LatestVersion.NetworkInfo);
        IsSupportButtonVisible = !string.IsNullOrEmpty(ContainerModification.LatestVersion.SupportLink);
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
        SelectedVersion = ContainerModification.GetSelectedVersion();
        SelectedVersion?.Installation.IsSelected = true;
    }

    public void SelectVersion(LauncherContentVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (SelectedVersion?.ContentKey == version.ContentKey && ReadyToRun)
        {
            return;
        }

        ReadyToRun = true;
        SelectedVersion = version;
        foreach (LauncherContentVersion candidate in ContainerModification.Versions)
        {
            candidate.Installation.IsSelected = candidate.ContentKey == version.ContentKey;
        }

        ModificationVersionSelection? matchingOption = VersionOptions.FirstOrDefault(selection =>
            selection.SelectedVersion.ContentKey == version.ContentKey);
        if (_selectedVersionOption != matchingOption)
        {
            SetProperty(ref _selectedVersionOption, matchingOption, nameof(SelectedVersionOption));
        }

        OnStatePropertiesChanged();
    }

    private void OnStatePropertiesChanged()
    {
        OnPropertyChanged(nameof(ContainerModification));
        OnPropertyChanged(nameof(LatestVersion));
        OnPropertyChanged(nameof(SelectedVersion));
        OnPropertyChanged(nameof(ActiveVersion));
        OnPropertyChanged(nameof(NameInfo));
        OnPropertyChanged(nameof(LatestVersionInfo));
        OnPropertyChanged(nameof(ReadyToRun));
        OnPropertyChanged(nameof(CanSetImage));
        OnPropertyChanged(nameof(CanOpenFolder));
        OnPropertyChanged(nameof(CanOpenModDb));
        OnPropertyChanged(nameof(CanOpenDiscord));
        OnPropertyChanged(nameof(HasContextMenuSeparator));
        OnPropertyChanged(nameof(HasContextMenu));
        OnPropertyChanged(nameof(LocalMod));
        OnPropertyChanged(nameof(ActiveIntegrityVersion));
    }

    private void OnPackageActivityChanged()
    {
        OnPropertyChanged(nameof(HasActivePackageActivity));
        PackageActivityChanged?.Invoke(this, EventArgs.Empty);
    }
}
