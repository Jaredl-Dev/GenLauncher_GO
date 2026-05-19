using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using GenLauncherGO.Core.Launching;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Exceptions;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Integrity;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Mods;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Shared.Localization;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.UI.Features.Launcher.ViewModels;

/// <summary>
///     Provides bindable state and model-facing operations for the main launcher window.
/// </summary>
internal sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly ILauncherContentCatalog _catalog;

    private readonly LauncherExecutableSelectionService _executableSelectionService;

    private readonly LauncherLaunchCoordinator _launchCoordinator;
    private readonly ILauncherPreferencesService _launcherPreferencesService;

    private readonly ILogger<MainWindowViewModel> _logger;

    private readonly IModificationImageFileService _modificationImageFileService;

    private readonly ModificationImageSourceFactory _modificationImageSourceFactory;

    private readonly ILogger<ModificationViewModel> _modificationViewModelLogger;

    private readonly LauncherPackageActivityService _packageActivityService;

    private readonly LauncherRuntimeContext _runtimeContext;

    private readonly ILauncherStringLocalizer _stringLocalizer;

    private readonly HashSet<ModificationViewModel> _trackedChildActivityTiles = [];
    private bool _mainControlsEnabled = true;
    private ExecutableOption? _selectedGameClientOption;

    private ExecutableOption? _selectedWorldBuilderOption;

    private bool _startupAddModPromptEvaluated;

    public MainWindowViewModel(
        ILauncherPreferencesService launcherPreferencesService,
        LauncherExecutableSelectionService executableSelectionService,
        ILauncherContentCatalog catalog,
        LauncherRuntimeContext runtimeContext,
        ILauncherStringLocalizer stringLocalizer,
        ModificationImageSourceFactory modificationImageSourceFactory,
        IModificationImageFileService modificationImageFileService,
        LauncherPackageActivityService packageActivityService,
        ILogger<ModificationViewModel> modificationViewModelLogger,
        LauncherLaunchCoordinator launchCoordinator,
        ILogger<MainWindowViewModel> logger)
    {
        _launcherPreferencesService = launcherPreferencesService ??
                                      throw new ArgumentNullException(nameof(launcherPreferencesService));
        _executableSelectionService = executableSelectionService ??
                                      throw new ArgumentNullException(nameof(executableSelectionService));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _runtimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        _modificationImageSourceFactory = modificationImageSourceFactory ??
                                          throw new ArgumentNullException(nameof(modificationImageSourceFactory));
        _modificationImageFileService = modificationImageFileService ??
                                        throw new ArgumentNullException(nameof(modificationImageFileService));
        _packageActivityService = packageActivityService ??
                                  throw new ArgumentNullException(nameof(packageActivityService));
        _modificationViewModelLogger = modificationViewModelLogger ??
                                       throw new ArgumentNullException(nameof(modificationViewModelLogger));
        _launchCoordinator = launchCoordinator ?? throw new ArgumentNullException(nameof(launchCoordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _launcherPreferencesService.PreferencesChanged += LauncherPreferencesService_PreferencesChanged;
        _launchCoordinator.PropertyChanged += LaunchCoordinator_PropertyChanged;
    }

    public ObservableCollection<ModificationViewModel> ModsListSource
    {
        get;
        private set => SetProperty(ref field, value);
    } = [];

    public ObservableCollection<ModificationViewModel> PatchesListSource
    {
        get;
        private set => SetProperty(ref field, value);
    } = [];

    public ObservableCollection<ModificationViewModel> AddonsListSource
    {
        get;
        private set => SetProperty(ref field, value);
    } = [];

    public IReadOnlyList<ModificationViewModel> SelectedModifications =>
        ModsListSource.Where(modification => modification.IsSelected).ToList();

    public IReadOnlyList<ModificationViewModel> SelectedPatches =>
        PatchesListSource.Where(modification => modification.IsSelected).ToList();

    public IReadOnlyList<ModificationViewModel> SelectedAddons =>
        AddonsListSource.Where(modification => modification.IsSelected).ToList();

    /// <summary>
    ///     Gets selected content in deployment order: optional modification, optional patch, then all add-ons.
    /// </summary>
    public IReadOnlyList<ModificationViewModel> SelectedContent =>
        SelectedModifications
            .Concat(SelectedPatches)
            .Concat(SelectedAddons)
            .ToList();

    public ObservableCollection<ExecutableOption> SupportedGameClients { get; } = [];

    public ObservableCollection<ExecutableOption> SupportedWorldBuilders { get; } = [];

    public ExecutableOption? SelectedGameClientOption
    {
        get => _selectedGameClientOption;
        set
        {
            if (SetProperty(ref _selectedGameClientOption, value))
            {
                if (value != null)
                {
                    UpdateActiveGamePreferences(preferences =>
                        preferences with { SelectedGameClient = value.ExecutableName });
                }

                OnMainControlStateChanged();
            }
        }
    }

    public ExecutableOption? SelectedWorldBuilderOption
    {
        get => _selectedWorldBuilderOption;
        set
        {
            if (SetProperty(ref _selectedWorldBuilderOption, value))
            {
                if (value != null)
                {
                    UpdateActiveGamePreferences(preferences =>
                        preferences with { SelectedWorldBuilder = value.ExecutableName });
                }

                OnMainControlStateChanged();
            }
        }
    }

    public string WindowTitle
    {
        get;
        private set => SetProperty(ref field, value);
    } = "GenLauncherGO";

    public string CurrentLauncherVersionText
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    public string WindowedModeButtonText
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    public string QuickStartButtonText
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    public string PatchesTabText
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    public string AddonsTabText
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    public string ManualAddPatchText
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    public string ManualAddAddonText
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    public bool GameClientSelectorEnabled => MainControlsEnabled && SupportedGameClients.Count > 0;

    /// <summary>
    ///     Gets a value indicating whether the start-game button can accept a launch attempt.
    ///     Executable availability is rechecked by the launch workflow so a missing selection can present feedback.
    /// </summary>
    public bool StartGameButtonEnabled => MainControlsEnabled && SelectedGameClientOption != null;

    public bool WorldBuilderSelectorEnabled => MainControlsEnabled && SupportedWorldBuilders.Count > 0;

    /// <summary>
    ///     Gets a value indicating whether the World Builder button can accept a launch attempt.
    ///     Executable availability is rechecked by the launch workflow so a missing selection can present feedback.
    /// </summary>
    public bool WorldBuilderButtonEnabled =>
        MainControlsEnabled && SelectedWorldBuilderOption != null;

    public bool MainControlsEnabled =>
        _mainControlsEnabled &&
        !_launchCoordinator.IsLaunchInProgress;

    public bool IsLoadingIndicatorVisible => !MainControlsEnabled;

    public bool CanAddRepositoryModification => _runtimeContext.Connected;

    public bool IsAddModButtonVisible =>
        ActiveContentView == LauncherContentViewKind.Modifications &&
        CanAddRepositoryModification;

    public LauncherContentViewKind ActiveContentView
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsAddModButtonVisible));
            }
        }
    } = LauncherContentViewKind.Modifications;

    public bool IsPatchesButtonVisible
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool IsAddonsButtonVisible
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool IsPatchesTabDownloadIndicatorVisible
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool IsAddonsTabDownloadIndicatorVisible
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool AddModButtonBlinking
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool IsRunningProcessOverlayVisible =>
        _launchCoordinator.HasActiveProcess && !_launchCoordinator.ShouldHideLauncherWindow;

    public string RunningProcessStatusText
    {
        get
        {
            if (!IsRunningProcessOverlayVisible)
            {
                return string.Empty;
            }

            string processName = _launchCoordinator.ActiveProcessName ?? string.Empty;
            string displayName = string.IsNullOrWhiteSpace(processName)
                ? _stringLocalizer["RunningProcessUnknown"]
                : processName;
            return string.Format(CultureInfo.CurrentCulture, _stringLocalizer["RunningProcessStatus"], displayName);
        }
    }

    public bool ShouldHideLauncherWindow => _launchCoordinator.ShouldHideLauncherWindow;

    public double ModsListVerticalOffset => ModsListSource.Count == 0
        ? 0
        : GetActiveGamePreferences(_launcherPreferencesService.Current).ModsListVerticalOffset;

    public void Dispose()
    {
        _launcherPreferencesService.PreferencesChanged -= LauncherPreferencesService_PreferencesChanged;
        _launchCoordinator.PropertyChanged -= LaunchCoordinator_PropertyChanged;
    }

    /// <summary>
    ///     Initializes bindable launcher state.
    /// </summary>
    public void Initialize(bool countLauncherStart = true)
    {
        HashSet<LauncherContentKey> persistedSelection = GetPersistedSelectionKeys();

        UpdateWindowTitle();
        RefreshGameClientOptions();
        RefreshWorldBuilderOptions();
        if (countLauncherStart)
        {
            UpdateLaunchesCount();
        }

        ActiveContentView = LauncherContentViewKind.Modifications;
        RefreshModsList(persistedSelection);
        RestorePersistedChildSelection(persistedSelection);
        if (RefreshTabs())
        {
            RefreshPatchesList();
            RefreshAddonsList();
            UpdateAddonAndPatchTabLabels();
        }

        UpdateCurrentLauncherVersionText();
        UpdateGameArgumentButtons(_launcherPreferencesService.Current);
    }

    /// <summary>
    ///     Rebuilds all game-specific presentation state after a restartless session switch.
    /// </summary>
    public void ReloadForActiveGame()
    {
        foreach (ModificationViewModel tile in _trackedChildActivityTiles.ToList())
        {
            UntrackChildTile(tile);
        }

        _selectedGameClientOption = null;
        _selectedWorldBuilderOption = null;
        OnPropertyChanged(nameof(SelectedGameClientOption));
        OnPropertyChanged(nameof(SelectedWorldBuilderOption));
        OnPropertyChanged(nameof(CanAddRepositoryModification));
        OnPropertyChanged(nameof(IsAddModButtonVisible));
        ModsListSource = [];
        PatchesListSource = [];
        AddonsListSource = [];
        Initialize(false);
    }

    private ModificationViewModel CreateModificationViewModel(LauncherContent modification)
    {
        return new ModificationViewModel(
            modification,
            _modificationImageSourceFactory,
            _runtimeContext,
            _modificationImageFileService,
            _stringLocalizer,
            _packageActivityService,
            _modificationViewModelLogger);
    }

    public void RefreshGameClientOptions()
    {
        string selectedClientExecutable = SelectedGameClientOption?.ExecutableName
                                          ?? GetActiveGamePreferences(_launcherPreferencesService.Current)
                                              .SelectedGameClient;

        SelectedGameClientOption = RefreshExecutableOptions(
            SupportedGameClients,
            _executableSelectionService.GetOptions(GameLaunchTargetKind.GameClient),
            selectedClientExecutable);
        OnMainControlStateChanged();
    }

    public void RefreshWorldBuilderOptions()
    {
        string selectedWorldBuilderExecutable = SelectedWorldBuilderOption?.ExecutableName
                                                ?? GetActiveGamePreferences(_launcherPreferencesService.Current)
                                                    .SelectedWorldBuilder;

        SelectedWorldBuilderOption = RefreshExecutableOptions(
            SupportedWorldBuilders,
            _executableSelectionService.GetOptions(GameLaunchTargetKind.WorldBuilder),
            selectedWorldBuilderExecutable);
        OnMainControlStateChanged();
    }

    private static ExecutableOption? RefreshExecutableOptions(
        ObservableCollection<ExecutableOption> destination,
        IReadOnlyList<ExecutableOption> options,
        string? selectedExecutable)
    {
        destination.Clear();
        foreach (ExecutableOption option in options)
        {
            destination.Add(option);
        }

        return LauncherExecutableSelectionService.SelectPreferredOption(
            destination,
            selectedExecutable);
    }

    /// <summary>
    ///     Updates the quick-start and windowed-mode button labels from current game arguments.
    /// </summary>
    public void UpdateGameArgumentButtons(LauncherPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        LauncherGamePreferences gamePreferences = GetActiveGamePreferences(preferences);

        bool isWindowed = LauncherGameArgumentService.ContainsArgument(
            gamePreferences.GameArguments,
            LauncherGameArgumentService.WindowedArgument);
        bool isQuickStart = LauncherGameArgumentService.ContainsArgument(
            gamePreferences.GameArguments,
            LauncherGameArgumentService.QuickStartArgument);

        WindowedModeButtonText = _stringLocalizer[isWindowed ? "ChangeToFullScreen" : "ChangeToWindowed"];
        QuickStartButtonText = _stringLocalizer[isQuickStart ? "ChangeToNormalStart" : "ChangeToQuickStart"];
    }

    /// <summary>
    ///     Toggles one managed game executable argument in launcher preferences.
    /// </summary>
    public void ToggleGameArgument(string argument)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(argument);

        LauncherGamePreferences preferences = GetActiveGamePreferences(_launcherPreferencesService.Current);
        bool enabled = !LauncherGameArgumentService.ContainsArgument(preferences.GameArguments, argument);
        string updatedArguments = LauncherGameArgumentService.SetArgumentEnabled(
            preferences.GameArguments,
            argument,
            enabled);

        UpdateActiveGamePreferences(currentPreferences =>
            currentPreferences with { GameArguments = updatedArguments });
    }

    public void UpdateCurrentLauncherVersionText()
    {
        CurrentLauncherVersionText = _stringLocalizer["CurrentVersion"] + _runtimeContext.CurrentLauncherVersion;
    }

    public void SetMainControlsEnabled(bool isEnabled)
    {
        if (_mainControlsEnabled == isEnabled)
        {
            return;
        }

        _mainControlsEnabled = isEnabled;
        OnMainControlStateChanged();
    }

    public void RefreshModsList()
    {
        var selectedKeys = ModsListSource
            .Where(modification => modification.IsSelected)
            .Select(modification => modification.ContainerModification.ContentKey)
            .ToHashSet();

        RefreshModsList(selectedKeys);
    }

    private void RefreshModsList(IReadOnlySet<LauncherContentKey> selectedKeys)
    {
        IReadOnlyList<LauncherContent> mods = GetModificationsForDisplay();
        ModsListSource = new ObservableCollection<ModificationViewModel>(
            mods.Select(CreateModificationViewModel));
        RestoreSelection(ModsListSource, selectedKeys, false);
        AddModButtonBlinking = !_startupAddModPromptEvaluated && ModsListSource.Count == 0;
        _startupAddModPromptEvaluated = true;
        SetIndexNumbersForMods();
    }

    public void RefreshPatchesList()
    {
        IReadOnlyList<LauncherContent> patches = _catalog.Data.GetPatchesFor(
            SelectedModifications.FirstOrDefault()?.ContainerModification);
        PatchesListSource = CreateChildActivityViewModels(patches);
        NormalizeSelection(PatchesListSource, false);
        TrackChildActivityTiles(PatchesListSource);
        UpdateChildDownloadTabIndicators();
    }

    public void RefreshAddonsList()
    {
        IReadOnlyList<LauncherContent> addons = _catalog.Data.GetAddonsFor(
            SelectedModifications.FirstOrDefault()?.ContainerModification,
            SelectedPatches.FirstOrDefault()?.ContainerModification);
        AddonsListSource = CreateChildActivityViewModels(addons);
        TrackChildActivityTiles(AddonsListSource);
        UpdateChildDownloadTabIndicators();
    }

    public async Task AddModToListAsync(
        string modName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modName);

        LauncherContentVersion modVersion = await _catalog.AddRepositoryModificationAsync(
            modName,
            cancellationToken);
        await _catalog.ReadPatchesAndAddonsForModAsync(
            modVersion.ContentKey,
            cancellationToken);

        LauncherContent modification = _catalog.Data.FindContent(modVersion.ContentKey)
                                       ?? throw new InvalidOperationException(
                                           "Downloaded modification was not added to the launcher catalog.");

        ModificationViewModel tile = CreateModificationViewModel(modification);
        tile.NotifyInstallAvailable();
        AddModButtonBlinking = false;
        ModsListSource.Add(tile);
        MoveModInList(ModsListSource.Count - 1, 0);
    }

    public void MoveModInList(int sourceIndex, int targetIndex)
    {
        ModsListSource.Move(sourceIndex, targetIndex);
        SetIndexNumbersForMods();
    }

    private IReadOnlyList<LauncherContent> GetModificationsForDisplay()
    {
        var modifications = _catalog.Data.Modifications
            .OrderBy(modification => modification.NumberInList)
            .ToList();

        if (!_runtimeContext.Connected || _catalog.Advertising is not { } advertising)
        {
            return modifications;
        }

        if (modifications.Count < 3)
        {
            return modifications;
        }

        int advertisingPosition = Math.Clamp(
            GetActiveGamePreferences(_launcherPreferencesService.Current).AdvertisingPositionInList,
            0,
            modifications.Count);
        modifications.Insert(advertisingPosition, new LauncherContent(advertising));

        return modifications;
    }

    public void SetIndexNumbersForMods()
    {
        int advertisingPosition = -1;
        for (int index = 0; index < ModsListSource.Count; index++)
        {
            LauncherContent modification = ModsListSource[index].ContainerModification;
            modification.NumberInList = index;
            if (modification.ModificationType == ModificationType.Advertising)
            {
                advertisingPosition = index;
            }
        }

        SaveAdvertisingPositionInList(advertisingPosition);
    }

    /// <summary>
    ///     Persists where the advertising tile now sits, so it reopens in the row the user dragged it to.
    /// </summary>
    /// <remarks>
    ///     The tile is not part of the persisted catalog, so its row cannot ride along with the modification order
    ///     that <see cref="SetIndexNumbersForMods" /> writes and needs its own per-game preference.
    /// </remarks>
    /// <param name="advertisingPosition">
    ///     The tile's row, or a negative value when the list has no advertising tile. A session that never shows the
    ///     tile, because it is offline or has too few modifications, must leave the row the user chose alone.
    /// </param>
    private void SaveAdvertisingPositionInList(int advertisingPosition)
    {
        if (advertisingPosition < 0)
        {
            return;
        }

        UpdateNonCriticalActiveGamePreferences(
            preferences => preferences with { AdvertisingPositionInList = advertisingPosition },
            "advertising tile position");
    }

    public async Task UpdateAddonsAndPatchesAsync(LauncherContent mod)
    {
        if (mod != null)
        {
            await _catalog.ReadPatchesAndAddonsForModAsync(mod.ContentKey, CancellationToken.None);
        }
    }

    /// <summary>
    ///     Loads any required original-game content and shows the requested content view.
    /// </summary>
    public async Task ShowContentViewAsync(
        LauncherContentViewKind viewKind,
        CancellationToken cancellationToken = default)
    {
        if (viewKind == LauncherContentViewKind.Hidden)
        {
            throw new ArgumentOutOfRangeException(nameof(viewKind), viewKind, "Hidden is a transient launcher view.");
        }

        LauncherContentViewKind previousView = ActiveContentView;
        ActiveContentView = LauncherContentViewKind.Hidden;
        try
        {
            bool requiresOriginalGameContent =
                viewKind is LauncherContentViewKind.Patches or LauncherContentViewKind.Addons &&
                SelectedModifications.Count == 0;
            if (requiresOriginalGameContent)
            {
                SetMainControlsEnabled(false);
                try
                {
                    await _catalog.ReadOriginalGameAddonsAndPatchesAsync(cancellationToken);
                }
                finally
                {
                    SetMainControlsEnabled(true);
                }
            }

            ActiveContentView = viewKind;
        }
        catch
        {
            ActiveContentView = previousView;
            throw;
        }
    }

    public void UpdateAddonAndPatchTabLabels()
    {
        RefreshTabs();
        UpdateChildDownloadTabIndicators();
    }

    /// <summary>
    ///     Returns every current or activity-retained tile used to preserve semantic selection during list refresh.
    /// </summary>
    internal IReadOnlyList<ModificationViewModel> GetKnownContentTiles()
    {
        return ModsListSource
            .Concat(PatchesListSource)
            .Concat(AddonsListSource)
            .Concat(_trackedChildActivityTiles)
            .Distinct()
            .ToList();
    }

    /// <summary>
    ///     Selects the requested content tile in its semantic content collection.
    /// </summary>
    public void SelectContent(ModificationViewModel modification)
    {
        ArgumentNullException.ThrowIfNull(modification);

        IReadOnlyList<ModificationViewModel> source = modification.ContainerModification.ModificationType switch
        {
            ModificationType.Mod or ModificationType.Advertising => ModsListSource,
            ModificationType.Patch => PatchesListSource,
            ModificationType.Addon => AddonsListSource,
            _ => Array.Empty<ModificationViewModel>()
        };

        if (modification.ContainerModification.ModificationType == ModificationType.Addon)
        {
            modification.IsSelected = true;
            return;
        }

        foreach (ModificationViewModel candidate in source)
        {
            candidate.IsSelected = ReferenceEquals(candidate, modification);
        }
    }

    /// <summary>
    ///     Projects semantic UI selection onto the persistence model.
    /// </summary>
    public void ApplySelectionToPersistenceModel()
    {
        var selectedKeys = GetKnownContentTiles()
            .Where(modification => modification.IsSelected)
            .Select(modification => modification.ContainerModification.ContentKey)
            .ToHashSet();

        foreach (LauncherContent modification in GetCatalogContent())
        {
            modification.IsSelected = selectedKeys.Contains(modification.ContentKey);
        }
    }

    /// <summary>
    ///     Saves launcher data through the UI selection persistence boundary.
    /// </summary>
    public void SaveLauncherData()
    {
        ApplySelectionToPersistenceModel();
        _catalog.SaveLauncherData();
    }

    /// <summary>
    ///     Persists the active game's main modification-list position without letting optional UI state block shutdown.
    /// </summary>
    public void SaveModsListVerticalOffset(double verticalOffset)
    {
        double normalizedOffset = ModsListSource.Count == 0 || !double.IsFinite(verticalOffset)
            ? 0
            : Math.Max(0, verticalOffset);

        UpdateNonCriticalActiveGamePreferences(
            preferences => preferences with { ModsListVerticalOffset = normalizedOffset },
            "modification-list scroll position");
    }

    /// <summary>
    ///     Gets the selected versions that integrity should verify before a launch.
    /// </summary>
    /// <remarks>
    ///     A version with a suspended download is deliberately half-written, so it is excluded: verifying it would
    ///     report the user's own paused transfer as damaged content and offer to repair it away.
    /// </remarks>
    public IReadOnlyList<LauncherContentVersion> GetSelectedVersionsOfAllSelectedModifications()
    {
        return SelectedContent
            .Select(modification => modification.SelectedVersion)
            .OfType<LauncherContentVersion>()
            .Where(version => !version.Installation.DownloadSuspended)
            .ToList();
    }

    public IReadOnlyList<string> GetNotAddedRepositoryModificationNames()
    {
        var addedModificationKeys = _catalog.Data.Modifications
            .Select(modification => LauncherContentKey.ForModificationName(modification.Name))
            .ToHashSet();
        IReadOnlyList<string> repositoryModificationNames =
            _catalog.RepositoryModificationNames ?? Array.Empty<string>();

        return repositoryModificationNames
            .Where(name => !addedModificationKeys.Contains(LauncherContentKey.ForModificationName(name)))
            .ToList();
    }

    public string? GetSelectedModificationName()
    {
        return SelectedModifications.FirstOrDefault()?.ContainerModification.Name;
    }

    public void AddImportedContentToList(LauncherContent importedContent)
    {
        ArgumentNullException.ThrowIfNull(importedContent);

        ModificationViewModel tile = CreateModificationViewModel(importedContent);

        switch (importedContent.ModificationType)
        {
            case ModificationType.Mod:
                AddModButtonBlinking = false;
                if (!ReplaceMatchingContent(ModsListSource, tile))
                {
                    ModsListSource.Add(tile);
                }

                MoveModInList(ModsListSource.IndexOf(tile), 0);
                break;
            case ModificationType.Patch:
                AddImportedChildContent(PatchesListSource, tile);
                break;
            case ModificationType.Addon:
                AddImportedChildContent(AddonsListSource, tile);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(importedContent),
                    importedContent.ModificationType,
                    "Unknown manual import kind.");
        }
    }

    public void RemoveContentFromList(ModificationViewModel modification)
    {
        ArgumentNullException.ThrowIfNull(modification);

        modification.IsSelected = false;
        switch (modification.ContainerModification.ModificationType)
        {
            case ModificationType.Mod:
                ModsListSource.Remove(modification);
                SetIndexNumbersForMods();
                break;
            case ModificationType.Patch:
                RemoveChildContent(PatchesListSource, modification);
                break;
            case ModificationType.Addon:
                RemoveChildContent(AddonsListSource, modification);
                break;
        }
    }

    public void RefreshModificationContainerData()
    {
        foreach (ModificationViewModel tile in ModsListSource)
        {
            tile.RefreshFromModel();
        }
    }

    private void UpdateLaunchesCount()
    {
        LauncherPreferences preferences = _launcherPreferencesService.Current;
        LauncherGamePreferences gamePreferences = GetActiveGamePreferences(preferences);
        int launchesCount = gamePreferences.LaunchesCount;

        if (launchesCount < 0)
        {
            launchesCount = LauncherApplicationDefaults.LaunchesCountForUpdateAdvertising;
        }

        if (launchesCount > LauncherApplicationDefaults.LaunchesCountForUpdateAdvertising)
        {
            launchesCount = 0;
        }

        try
        {
            LauncherGamePreferences updatedGamePreferences =
                gamePreferences with { LaunchesCount = launchesCount + 1 };
            _launcherPreferencesService.Update(preferences with
            {
                Games = preferences.Games.With(
                    _runtimeContext.CurrentlyManagedGame,
                    updatedGamePreferences)
            });
        }
        catch (LauncherPreferencesPersistenceException exception)
        {
            _logger.LogWarning(
                exception,
                "The non-critical launcher count could not be persisted; startup will continue with the previous value.");
        }
    }

    private void UpdateLauncherPreferences(Func<LauncherPreferences, LauncherPreferences> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        _launcherPreferencesService.Update(update(_launcherPreferencesService.Current));
    }

    private void UpdateActiveGamePreferences(
        Func<LauncherGamePreferences, LauncherGamePreferences> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        UpdateLauncherPreferences(preferences =>
        {
            SupportedGame game = _runtimeContext.CurrentlyManagedGame;
            LauncherGamePreferences updated = update(preferences.Games.Get(game));
            return preferences with { Games = preferences.Games.With(game, updated) };
        });
    }

    /// <summary>
    ///     Persists optional UI state without allowing a settings write failure to block the active workflow.
    /// </summary>
    private void UpdateNonCriticalActiveGamePreferences(
        Func<LauncherGamePreferences, LauncherGamePreferences> update,
        string preferenceName)
    {
        try
        {
            UpdateActiveGamePreferences(update);
        }
        catch (LauncherPreferencesPersistenceException exception)
        {
            _logger.LogWarning(
                exception,
                "The non-critical {PreferenceName} could not be persisted.",
                preferenceName);
        }
    }

    private LauncherGamePreferences GetActiveGamePreferences(LauncherPreferences preferences)
    {
        return preferences.Games.Get(_runtimeContext.CurrentlyManagedGame);
    }

    private void LauncherPreferencesService_PreferencesChanged(
        object? sender,
        LauncherPreferences preferences)
    {
        UpdateGameArgumentButtons(preferences);
    }

    private void LaunchCoordinator_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LauncherLaunchCoordinator.IsLaunchInProgress))
        {
            OnMainControlStateChanged();
            return;
        }

        if (e.PropertyName is (nameof(LauncherLaunchCoordinator.HasActiveProcess)) or
            (nameof(LauncherLaunchCoordinator.ActiveProcessName)) or
            (nameof(LauncherLaunchCoordinator.ShouldHideLauncherWindow)))
        {
            OnPropertyChanged(nameof(IsRunningProcessOverlayVisible));
            OnPropertyChanged(nameof(RunningProcessStatusText));
        }

        if (e.PropertyName == nameof(LauncherLaunchCoordinator.ShouldHideLauncherWindow))
        {
            OnPropertyChanged(nameof(ShouldHideLauncherWindow));
        }
    }

    private void UpdateWindowTitle()
    {
        string gameName = _stringLocalizer[
            _runtimeContext.CurrentlyManagedGame == SupportedGame.ZeroHour
                ? "ZeroHourShortName"
                : "GeneralsShortName"];
        WindowTitle = $"GenLauncherGO - {gameName}";
    }

    /// <summary>
    ///     Refreshes launcher tab labels and visibility.
    /// </summary>
    /// <returns><see langword="true" /> when child content lists should be refreshed.</returns>
    public bool RefreshTabs()
    {
        LauncherContent? currentModification =
            SelectedModifications.FirstOrDefault()?.ContainerModification;
        if (currentModification?.ModificationType == ModificationType.Advertising)
        {
            IsPatchesButtonVisible = false;
            IsAddonsButtonVisible = false;
            IsPatchesTabDownloadIndicatorVisible = false;
            IsAddonsTabDownloadIndicatorVisible = false;
            return false;
        }

        string targetName = currentModification?.Name ?? _stringLocalizer[
            _runtimeContext.CurrentlyManagedGame == SupportedGame.Generals
                ? "GeneralsShortName"
                : "ZeroHourShortName"];
        int activePatchCount = SelectedPatches.FirstOrDefault()?.SelectedVersion?.Installation.Installed == true
            ? 1
            : 0;
        int activeAddonCount = SelectedAddons.Count(addon =>
            addon.SelectedVersion?.Installation.Installed == true);

        PatchesTabText = CreateCountedTabLabel(
            _stringLocalizer["Patches"],
            targetName,
            activePatchCount);
        AddonsTabText = CreateCountedTabLabel(
            _stringLocalizer["Addons"],
            targetName,
            activeAddonCount);
        ManualAddPatchText = string.Format(CultureInfo.CurrentCulture, _stringLocalizer["AddPatchFromFiles"], targetName);
        ManualAddAddonText = string.Format(CultureInfo.CurrentCulture, _stringLocalizer["AddAddonFromFiles"], targetName);
        IsPatchesButtonVisible = true;
        IsAddonsButtonVisible = true;
        return true;
    }

    private static string CreateCountedTabLabel(string prefix, string targetName, int activeCount)
    {
        string label = prefix + targetName;
        return activeCount <= 0
            ? label
            : $"{label} ({activeCount})";
    }

    private void UpdateChildDownloadTabIndicators()
    {
        IsPatchesTabDownloadIndicatorVisible =
            PatchesListSource.Any(modification => modification.HasActivePackageActivity);
        IsAddonsTabDownloadIndicatorVisible =
            AddonsListSource.Any(modification => modification.HasActivePackageActivity);
    }

    /// <summary>
    ///     Creates child-content tiles while preserving active package activity tiles across list rebuilds.
    /// </summary>
    private ObservableCollection<ModificationViewModel> CreateChildActivityViewModels(
        IEnumerable<LauncherContent> modifications)
    {
        ArgumentNullException.ThrowIfNull(modifications);

        var viewModels = new ObservableCollection<ModificationViewModel>();
        foreach (LauncherContent modification in modifications)
        {
            viewModels.Add(FindTrackedChildTile(modification) ?? CreateModificationViewModel(modification));
        }

        return viewModels;
    }

    private ModificationViewModel? FindTrackedChildTile(LauncherContent modification)
    {
        return _trackedChildActivityTiles.FirstOrDefault(tile =>
            ChildContentMatches(modification, tile.ContainerModification));
    }

    private void TrackChildActivityTiles(IEnumerable<ModificationViewModel> modifications)
    {
        foreach (ModificationViewModel modification in modifications)
        {
            if (_trackedChildActivityTiles.Add(modification))
            {
                modification.PackageActivityChanged += ChildPackageActivityChanged;
            }
        }

        PruneInactiveChildActivityTiles();
    }

    private void ChildPackageActivityChanged(object? sender, EventArgs e)
    {
        if (sender is not ModificationViewModel childTile)
        {
            return;
        }

        UpdateForwardedChildPackageActivity(childTile);
        UpdateChildDownloadTabIndicators();
        PruneInactiveChildActivityTiles();
    }

    private void UpdateForwardedChildPackageActivity(ModificationViewModel childTile)
    {
        ModificationViewModel? parentTile = FindParentModificationTile(childTile);
        if (parentTile == null)
        {
            return;
        }

        IReadOnlyList<ModificationViewModel> activeChildren = _trackedChildActivityTiles
            .Where(tile => IsChildOfParent(tile, parentTile.ContainerModification.Name))
            .Where(tile => tile.HasActivePackageActivity)
            .ToList();

        if (activeChildren.Count == 0)
        {
            parentTile.CompleteForwardedChildPackageActivity();
            return;
        }

        string message = activeChildren
            .Select(tile => tile.ProgressMessage)
            .LastOrDefault(message => !string.IsNullOrWhiteSpace(message)) ?? _stringLocalizer["Preparing"];
        int percentage = Convert.ToInt32(activeChildren.Average(tile => tile.ProgressValue));
        parentTile.ReportForwardedChildPackageActivity(message, percentage);
    }

    /// <summary>
    ///     Removes inactive child activity subscriptions that are no longer shown in current child lists.
    /// </summary>
    private void PruneInactiveChildActivityTiles()
    {
        foreach (ModificationViewModel tile in _trackedChildActivityTiles
                     .Where(tile =>
                         !tile.HasActivePackageActivity &&
                         (!CatalogContains(tile) || (!tile.IsSelected && !IsCurrentChildTile(tile))))
                     .ToList())
        {
            UntrackChildTile(tile);
        }
    }

    private void UntrackChildTile(ModificationViewModel tile)
    {
        if (_trackedChildActivityTiles.Remove(tile))
        {
            tile.PackageActivityChanged -= ChildPackageActivityChanged;
        }
    }

    private ModificationViewModel? FindParentModificationTile(ModificationViewModel childTile)
    {
        return ModsListSource.FirstOrDefault(tile =>
            IsChildOfParent(childTile, tile.ContainerModification.Name));
    }

    private bool IsCurrentChildTile(ModificationViewModel tile)
    {
        return PatchesListSource.Contains(tile) || AddonsListSource.Contains(tile);
    }

    private bool CatalogContains(ModificationViewModel tile)
    {
        LauncherContentKey contentKey = tile.ContainerModification.ContentKey;
        return GetCatalogContent().Any(content => content.ContentKey == contentKey);
    }

    private static bool IsChildOfParent(ModificationViewModel childTile, string parentName)
    {
        return !string.IsNullOrWhiteSpace(parentName) &&
               childTile.ContainerModification.ContentKey.IsChildOf(
                   LauncherContentKey.ForModificationName(parentName));
    }

    private static bool ChildContentMatches(LauncherContent left, LauncherContent right)
    {
        return left.ContentKey == right.ContentKey;
    }

    private HashSet<LauncherContentKey> GetPersistedSelectionKeys()
    {
        return GetCatalogContent()
            .Where(modification => modification.IsSelected)
            .Select(modification => modification.ContentKey)
            .ToHashSet();
    }

    private void RestorePersistedChildSelection(IReadOnlySet<LauncherContentKey> selectedKeys)
    {
        var restoredPatches = new ObservableCollection<ModificationViewModel>(
            _catalog.Data.Patches.Select(CreateModificationViewModel));
        var restoredAddons = new ObservableCollection<ModificationViewModel>(
            _catalog.Data.Addons.Select(CreateModificationViewModel));

        foreach (IGrouping<string, ModificationViewModel> patchesForParent in restoredPatches.GroupBy(
                     patch => patch.ContainerModification.ContentKey.ParentIdentity,
                     StringComparer.OrdinalIgnoreCase))
        {
            RestoreSelection(patchesForParent, selectedKeys, false);
        }

        RestoreSelection(restoredAddons, selectedKeys, true);
        TrackChildActivityTiles(restoredPatches);
        TrackChildActivityTiles(restoredAddons);
    }

    private IEnumerable<LauncherContent> GetCatalogContent()
    {
        return _catalog.Data.Modifications
            .Concat(_catalog.Data.Patches)
            .Concat(_catalog.Data.Addons);
    }

    private static void RestoreSelection(
        IEnumerable<ModificationViewModel> modifications,
        IReadOnlySet<LauncherContentKey> selectedKeys,
        bool allowsMultiple)
    {
        bool selectionRestored = false;
        foreach (ModificationViewModel modification in modifications)
        {
            bool isSelected = selectedKeys.Contains(modification.ContainerModification.ContentKey) &&
                              (allowsMultiple || !selectionRestored);
            modification.IsSelected = isSelected;
            selectionRestored |= isSelected;
        }
    }

    private static void NormalizeSelection(
        IEnumerable<ModificationViewModel> modifications,
        bool allowsMultiple)
    {
        IReadOnlyList<ModificationViewModel> materialized = modifications.ToList();
        var selectedKeys = materialized
            .Where(modification => modification.IsSelected)
            .Select(modification => modification.ContainerModification.ContentKey)
            .ToHashSet();
        RestoreSelection(materialized, selectedKeys, allowsMultiple);
    }

    private void AddImportedChildContent(
        ObservableCollection<ModificationViewModel> source,
        ModificationViewModel tile)
    {
        if (!ReplaceMatchingContent(source, tile))
        {
            source.Add(tile);
        }

        TrackChildActivityTiles([tile]);
        source.Move(source.IndexOf(tile), 0);
    }

    private void RemoveChildContent(
        ObservableCollection<ModificationViewModel> source,
        ModificationViewModel tile)
    {
        source.Remove(tile);
        UntrackChildTile(tile);
        UpdateChildDownloadTabIndicators();
        PruneInactiveChildActivityTiles();
    }

    private bool ReplaceMatchingContent(
        ObservableCollection<ModificationViewModel> source,
        ModificationViewModel replacement)
    {
        ModificationViewModel? existing = source.FirstOrDefault(modification =>
            modification.ContainerModification.ContentKey ==
            replacement.ContainerModification.ContentKey);
        if (existing == null)
        {
            return false;
        }

        int existingIndex = source.IndexOf(existing);
        replacement.IsSelected = existing.IsSelected;
        source[existingIndex] = replacement;
        UntrackChildTile(existing);
        return true;
    }

    private void OnMainControlStateChanged()
    {
        OnPropertyChanged(nameof(GameClientSelectorEnabled));
        OnPropertyChanged(nameof(StartGameButtonEnabled));
        OnPropertyChanged(nameof(WorldBuilderSelectorEnabled));
        OnPropertyChanged(nameof(WorldBuilderButtonEnabled));
        OnPropertyChanged(nameof(MainControlsEnabled));
        OnPropertyChanged(nameof(IsLoadingIndicatorVisible));
    }
}
