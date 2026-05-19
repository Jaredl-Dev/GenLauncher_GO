using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using GenLauncherGO.Core.Launching;
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
/// Provides bindable state and model-facing operations for the main launcher window.
/// </summary>
internal sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly ILauncherPreferencesService _launcherPreferencesService;

    private readonly LauncherExecutableSelectionService _executableSelectionService;

    private readonly ILauncherContentCatalog _catalog;

    private readonly LauncherRuntimeContext _runtimeContext;

    private readonly ILauncherStringLocalizer _stringLocalizer;

    private readonly ModificationImageSourceFactory _modificationImageSourceFactory;

    private readonly IModificationImageFileService _modificationImageFileService;

    private readonly LauncherPackageActivityService _packageActivityService;

    private readonly ILogger<ModificationViewModel> _modificationViewModelLogger;

    private readonly LauncherLaunchCoordinator _launchCoordinator;

    private readonly ILogger<MainWindowViewModel> _logger;

    private ObservableCollection<ModificationViewModel> _modsListSource = new();

    private ObservableCollection<ModificationViewModel> _patchesListSource = new();

    private ObservableCollection<ModificationViewModel> _addonsListSource = new();

    private readonly HashSet<ModificationViewModel> _trackedChildActivityTiles = new();

    private ExecutableOption? _selectedGameClientOption;

    private ExecutableOption? _selectedWorldBuilderOption;

    private bool _mainControlsEnabled = true;

    private string _windowTitle = "GenLauncherGO";

    private string _currentLauncherVersionText = string.Empty;

    private string _windowedModeButtonText = string.Empty;

    private string _quickStartButtonText = string.Empty;

    private string _patchesTabText = string.Empty;

    private string _addonsTabText = string.Empty;

    private string _manualAddPatchText = string.Empty;

    private string _manualAddAddonText = string.Empty;

    private LauncherContentViewKind _activeContentView = LauncherContentViewKind.Modifications;

    private bool _isPatchesButtonVisible;

    private bool _isAddonsButtonVisible;

    private bool _isPatchesTabDownloadIndicatorVisible;

    private bool _isAddonsTabDownloadIndicatorVisible;

    private double _patchesTabDownloadProgressValue;

    private double _addonsTabDownloadProgressValue;

    private LauncherTaskbarProgressState _taskbarProgressState = LauncherTaskbarProgressState.None;

    private double _taskbarProgressValue;

    private bool _addModButtonBlinking;

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
        _packageActivityService.ActivityChanged += PackageActivityService_ActivityChanged;
        _launchCoordinator.PropertyChanged += LaunchCoordinator_PropertyChanged;
    }

    public ObservableCollection<ModificationViewModel> ModsListSource
    {
        get => _modsListSource;
        private set => SetProperty(ref _modsListSource, value);
    }

    public ObservableCollection<ModificationViewModel> PatchesListSource
    {
        get => _patchesListSource;
        private set => SetProperty(ref _patchesListSource, value);
    }

    public ObservableCollection<ModificationViewModel> AddonsListSource
    {
        get => _addonsListSource;
        private set => SetProperty(ref _addonsListSource, value);
    }

    public IReadOnlyList<ModificationViewModel> SelectedModifications =>
        ModsListSource.Where(modification => modification.IsSelected).ToList();

    public IReadOnlyList<ModificationViewModel> SelectedPatches =>
        PatchesListSource.Where(modification => modification.IsSelected).ToList();

    public IReadOnlyList<ModificationViewModel> SelectedAddons =>
        AddonsListSource.Where(modification => modification.IsSelected).ToList();

    public ObservableCollection<ExecutableOption> SupportedGameClients { get; } = new();

    public ObservableCollection<ExecutableOption> SupportedWorldBuilders { get; } = new();

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

                UpdateWindowTitle();
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
        get => _windowTitle;
        private set => SetProperty(ref _windowTitle, value);
    }

    public string CurrentLauncherVersionText
    {
        get => _currentLauncherVersionText;
        private set => SetProperty(ref _currentLauncherVersionText, value);
    }

    public string WindowedModeButtonText
    {
        get => _windowedModeButtonText;
        private set => SetProperty(ref _windowedModeButtonText, value);
    }

    public string QuickStartButtonText
    {
        get => _quickStartButtonText;
        private set => SetProperty(ref _quickStartButtonText, value);
    }

    public string PatchesTabText
    {
        get => _patchesTabText;
        private set => SetProperty(ref _patchesTabText, value);
    }

    public string AddonsTabText
    {
        get => _addonsTabText;
        private set => SetProperty(ref _addonsTabText, value);
    }

    public string ManualAddPatchText
    {
        get => _manualAddPatchText;
        private set => SetProperty(ref _manualAddPatchText, value);
    }

    public string ManualAddAddonText
    {
        get => _manualAddAddonText;
        private set => SetProperty(ref _manualAddAddonText, value);
    }

    public bool GameClientSelectorEnabled => MainControlsEnabled && SupportedGameClients.Count > 0;

    /// <summary>
    /// Gets a value indicating whether the start-game button can accept a launch attempt.
    /// Executable availability is rechecked by the launch workflow so a missing selection can present feedback.
    /// </summary>
    public bool StartGameButtonEnabled => MainControlsEnabled && SelectedGameClientOption != null;

    public bool WorldBuilderSelectorEnabled => MainControlsEnabled && SupportedWorldBuilders.Count > 0;

    /// <summary>
    /// Gets a value indicating whether the World Builder button can accept a launch attempt.
    /// Executable availability is rechecked by the launch workflow so a missing selection can present feedback.
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
        get => _activeContentView;
        private set
        {
            if (SetProperty(ref _activeContentView, value))
            {
                OnPropertyChanged(nameof(IsAddModButtonVisible));
            }
        }
    }

    public bool IsPatchesButtonVisible
    {
        get => _isPatchesButtonVisible;
        private set => SetProperty(ref _isPatchesButtonVisible, value);
    }

    public bool IsAddonsButtonVisible
    {
        get => _isAddonsButtonVisible;
        private set => SetProperty(ref _isAddonsButtonVisible, value);
    }

    public bool IsPatchesTabDownloadIndicatorVisible
    {
        get => _isPatchesTabDownloadIndicatorVisible;
        private set => SetProperty(ref _isPatchesTabDownloadIndicatorVisible, value);
    }

    public bool IsAddonsTabDownloadIndicatorVisible
    {
        get => _isAddonsTabDownloadIndicatorVisible;
        private set => SetProperty(ref _isAddonsTabDownloadIndicatorVisible, value);
    }

    public double PatchesTabDownloadProgressValue
    {
        get => _patchesTabDownloadProgressValue;
        private set => SetProperty(ref _patchesTabDownloadProgressValue, value);
    }

    public double AddonsTabDownloadProgressValue
    {
        get => _addonsTabDownloadProgressValue;
        private set => SetProperty(ref _addonsTabDownloadProgressValue, value);
    }

    public LauncherTaskbarProgressState TaskbarProgressState
    {
        get => _taskbarProgressState;
        private set => SetProperty(ref _taskbarProgressState, value);
    }

    public double TaskbarProgressValue
    {
        get => _taskbarProgressValue;
        private set => SetProperty(ref _taskbarProgressValue, value);
    }

    public bool AddModButtonBlinking
    {
        get => _addModButtonBlinking;
        private set => SetProperty(ref _addModButtonBlinking, value);
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
            string displayName = String.IsNullOrWhiteSpace(processName)
                ? _stringLocalizer["RunningProcessUnknown"]
                : processName;
            return String.Format(_stringLocalizer["RunningProcessStatus"], displayName);
        }
    }

    public bool ShouldHideLauncherWindow => _launchCoordinator.ShouldHideLauncherWindow;

    /// <summary>
    /// Initializes bindable launcher state.
    /// </summary>
    public void Initialize(bool countLauncherStart = true)
    {
        HashSet<LauncherContentKey> persistedSelection = GetPersistedSelectionKeys();

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
    /// Rebuilds all game-specific presentation state after a restartless session switch.
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
        ModsListSource = new ObservableCollection<ModificationViewModel>();
        PatchesListSource = new ObservableCollection<ModificationViewModel>();
        AddonsListSource = new ObservableCollection<ModificationViewModel>();
        Initialize(countLauncherStart: false);
    }

    public void Dispose()
    {
        _launcherPreferencesService.PreferencesChanged -= LauncherPreferencesService_PreferencesChanged;
        _packageActivityService.ActivityChanged -= PackageActivityService_ActivityChanged;
        _launchCoordinator.PropertyChanged -= LaunchCoordinator_PropertyChanged;
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

        SupportedGameClients.Clear();
        foreach (ExecutableOption gameClient in _executableSelectionService.GetGameClientOptions())
        {
            SupportedGameClients.Add(gameClient);
        }

        SelectedGameClientOption = _executableSelectionService.SelectGameClientOption(
            SupportedGameClients,
            selectedClientExecutable);
        OnMainControlStateChanged();
    }

    public void RefreshWorldBuilderOptions()
    {
        string selectedWorldBuilderExecutable = SelectedWorldBuilderOption?.ExecutableName
                                                ?? GetActiveGamePreferences(_launcherPreferencesService.Current)
                                                    .SelectedWorldBuilder;

        SupportedWorldBuilders.Clear();
        foreach (ExecutableOption worldBuilder in _executableSelectionService.GetWorldBuilderOptions())
        {
            SupportedWorldBuilders.Add(worldBuilder);
        }

        SelectedWorldBuilderOption = _executableSelectionService.SelectWorldBuilderOption(
            SupportedWorldBuilders,
            selectedWorldBuilderExecutable);
        OnMainControlStateChanged();
    }

    /// <summary>
    /// Updates the quick-start and windowed-mode button labels from current game arguments.
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
    /// Toggles one managed game executable argument in launcher preferences.
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
        RestoreSelection(ModsListSource, selectedKeys);
        AddModButtonBlinking = !_startupAddModPromptEvaluated && ModsListSource.Count == 0;
        _startupAddModPromptEvaluated = true;
        SetIndexNumbersForMods();
    }

    public void RefreshPatchesList()
    {
        IReadOnlyList<LauncherContent> patches = _catalog.Data.GetPatchesFor(
            SelectedModifications.FirstOrDefault()?.ContainerModification);
        PatchesListSource = CreateChildActivityViewModels(patches);
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

        LauncherContent mod = _catalog.Data.FindContent(modVersion.ContentKey)
                               ?? throw new InvalidOperationException(
                                   "Downloaded modification was not added to the launcher catalog.");

        ModificationViewModel tempModBox = CreateModificationViewModel(mod);
        tempModBox.NotifyInstallAvailable();
        AddModButtonBlinking = false;
        ModsListSource.Add(tempModBox);
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

        modifications.Insert(0, new LauncherContent(advertising));

        return modifications;
    }

    public void SetIndexNumbersForMods()
    {
        for (int index = 0; index < ModsListSource.Count; index++)
        {
            ModsListSource[index].ContainerModification.NumberInList = index;
        }
    }

    public async Task UpdateAddonsAndPatchesAsync(LauncherContent mod)
    {
        if (mod != null)
        {
            await _catalog.ReadPatchesAndAddonsForModAsync(mod.ContentKey, CancellationToken.None);
        }
    }

    /// <summary>
    /// Loads any required original-game content and shows the requested content view.
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

    /// <summary>
    /// Refreshes launcher tab labels and visibility.
    /// </summary>
    /// <returns><see langword="true"/> when child content lists should be refreshed.</returns>
    public bool RefreshTabs()
    {
        return RefreshTabState();
    }

    public void UpdateAddonAndPatchTabLabels()
    {
        RefreshTabState();
        UpdateChildDownloadTabIndicators();
    }

    /// <summary>
    /// Returns every current or activity-retained tile used to preserve semantic selection during list refresh.
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
    /// Selects the requested content tile in its semantic content collection.
    /// </summary>
    public void EnsureContentSelected(ModificationViewModel modification)
    {
        ArgumentNullException.ThrowIfNull(modification);

        IReadOnlyList<ModificationViewModel> source = modification.ContainerModification.ModificationType switch
        {
            ModificationType.Mod => ModsListSource,
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
    /// Clears an already-selected single-choice modification or patch.
    /// </summary>
    /// <returns><see langword="true"/> when the selection was cleared.</returns>
    public bool TryClearContentSelection(ModificationViewModel modification)
    {
        ArgumentNullException.ThrowIfNull(modification);

        if (!modification.IsSelected ||
            modification.ContainerModification.ModificationType is not
                (ModificationType.Mod or ModificationType.Patch))
        {
            return false;
        }

        modification.IsSelected = false;
        return true;
    }

    /// <summary>
    /// Projects semantic UI selection onto the persistence model.
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
    /// Saves launcher data through the UI selection persistence boundary.
    /// </summary>
    public void SaveLauncherData()
    {
        ApplySelectionToPersistenceModel();
        _catalog.SaveLauncherData();
    }

    public IReadOnlyList<LauncherContentVersion> GetSelectedVersionsOfAllSelectedModifications()
    {
        return SelectedModifications.Take(1)
            .Concat(SelectedPatches.Take(1))
            .Concat(SelectedAddons)
            .Select(modification => modification.SelectedVersion)
            .OfType<LauncherContentVersion>()
            .ToList();
    }

    public IReadOnlyList<string> GetNotAddedRepositoryModificationNames()
    {
        var addedModificationKeys = _catalog.Data.Modifications
            .Select(modification => LauncherContentKey.ForModificationName(modification.Name))
            .ToHashSet();
        IReadOnlyList<string> reposMods = _catalog.RepositoryModificationNames ?? Array.Empty<string>();

        return reposMods
            .Where(name => !addedModificationKeys.Contains(LauncherContentKey.ForModificationName(name)))
            .ToList();
    }

    public string? GetSelectedModificationName()
    {
        return SelectedModifications.FirstOrDefault()?.ContainerModification.Name;
    }

    public void AddImportedContentToList(LauncherManualImportResult importResult)
    {
        ArgumentNullException.ThrowIfNull(importResult);

        ModificationViewModel modData = CreateModificationViewModel(importResult.Modification);

        switch (importResult.Kind)
        {
            case ModificationType.Mod:
                AddModButtonBlinking = false;
                if (!ReplaceMatchingContent(ModsListSource, modData))
                {
                    ModsListSource.Add(modData);
                }

                MoveModInList(ModsListSource.IndexOf(modData), 0);
                break;
            case ModificationType.Patch:
                if (!ReplaceMatchingContent(PatchesListSource, modData))
                {
                    PatchesListSource.Add(modData);
                }

                TrackChildActivityTiles(new[] { modData });
                PatchesListSource.Move(PatchesListSource.IndexOf(modData), 0);
                break;
            case ModificationType.Addon:
                if (!ReplaceMatchingContent(AddonsListSource, modData))
                {
                    AddonsListSource.Add(modData);
                }

                TrackChildActivityTiles(new[] { modData });
                AddonsListSource.Move(AddonsListSource.IndexOf(modData), 0);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(importResult),
                    importResult.Kind,
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
                PatchesListSource.Remove(modification);
                UntrackChildTile(modification);
                UpdateChildDownloadTabIndicators();
                PruneInactiveChildActivityTiles();
                break;
            case ModificationType.Addon:
                AddonsListSource.Remove(modification);
                UntrackChildTile(modification);
                UpdateChildDownloadTabIndicators();
                PruneInactiveChildActivityTiles();
                break;
        }
    }

    public void RefreshModificationContainerData()
    {
        foreach (ModificationViewModel modData in ModsListSource)
        {
            modData.RefreshFromModel();
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
                    updatedGamePreferences),
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

    private void PackageActivityService_ActivityChanged(object? sender, EventArgs e)
    {
        UpdateTaskbarProgress();
    }

    private void LaunchCoordinator_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LauncherLaunchCoordinator.IsLaunchInProgress))
        {
            OnMainControlStateChanged();
            return;
        }

        if (e.PropertyName == nameof(LauncherLaunchCoordinator.HasActiveProcess) ||
            e.PropertyName == nameof(LauncherLaunchCoordinator.ActiveProcessName) ||
            e.PropertyName == nameof(LauncherLaunchCoordinator.ShouldHideLauncherWindow))
        {
            OnPropertyChanged(nameof(IsRunningProcessOverlayVisible));
            OnPropertyChanged(nameof(RunningProcessStatusText));
        }

        if (e.PropertyName == nameof(LauncherLaunchCoordinator.ShouldHideLauncherWindow))
        {
            OnPropertyChanged(nameof(ShouldHideLauncherWindow));
        }
    }

    private void UpdateTaskbarProgress()
    {
        if (!_packageActivityService.IsActive)
        {
            TaskbarProgressValue = 0D;
            TaskbarProgressState = LauncherTaskbarProgressState.None;
            return;
        }

        double? progressPercentage = _packageActivityService.ProgressPercentage;
        if (!progressPercentage.HasValue)
        {
            TaskbarProgressValue = 0D;
            TaskbarProgressState = LauncherTaskbarProgressState.Indeterminate;
            return;
        }

        TaskbarProgressValue = Math.Clamp(progressPercentage.Value / 100D, 0D, 1D);
        TaskbarProgressState = LauncherTaskbarProgressState.Normal;
    }

    private void UpdateWindowTitle()
    {
        string gameName = _stringLocalizer[
            _runtimeContext.CurrentlyManagedGame == SupportedGame.ZeroHour
                ? "ZeroHourShortName"
                : "GeneralsShortName"];
        string gameClientName = SelectedGameClientOption?.DisplayName ?? _stringLocalizer["NoSupportedClient"];

        WindowTitle = $"GenLauncherGO - {gameName} - {gameClientName}";
    }

    private bool RefreshTabState()
    {
        LauncherContent? currentModification =
            SelectedModifications.FirstOrDefault()?.ContainerModification;
        if (currentModification?.ModificationType == ModificationType.Advertising)
        {
            IsPatchesButtonVisible = false;
            IsAddonsButtonVisible = false;
            IsPatchesTabDownloadIndicatorVisible = false;
            IsAddonsTabDownloadIndicatorVisible = false;
            PatchesTabDownloadProgressValue = 0D;
            AddonsTabDownloadProgressValue = 0D;
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
        ManualAddPatchText = String.Format(_stringLocalizer["AddPatchFromFiles"], targetName);
        ManualAddAddonText = String.Format(_stringLocalizer["AddAddonFromFiles"], targetName);
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
        (int patchDownloadCount, double patchDownloadProgress) = GetDownloadIndicatorState(PatchesListSource);
        IsPatchesTabDownloadIndicatorVisible = patchDownloadCount > 0;
        PatchesTabDownloadProgressValue = patchDownloadCount > 0 ? patchDownloadProgress : 0D;

        (int addonDownloadCount, double addonDownloadProgress) = GetDownloadIndicatorState(AddonsListSource);
        IsAddonsTabDownloadIndicatorVisible = addonDownloadCount > 0;
        AddonsTabDownloadProgressValue = addonDownloadCount > 0 ? addonDownloadProgress : 0D;
    }

    private static (int DownloadCount, double ProgressValue) GetDownloadIndicatorState(
        IEnumerable<ModificationViewModel> modifications)
    {
        var activeDownloads = modifications
            .Where(modification => modification.HasActivePackageActivity)
            .ToList();

        if (activeDownloads.Count == 0)
        {
            return (0, 0D);
        }

        return (activeDownloads.Count, activeDownloads.Average(modification => modification.ProgressValue));
    }

    /// <summary>
    /// Creates child-content tiles while preserving active package activity tiles across list rebuilds.
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
            .LastOrDefault(message => !String.IsNullOrWhiteSpace(message)) ?? _stringLocalizer["Preparing"];
        int percentage = Convert.ToInt32(activeChildren.Average(tile => tile.ProgressValue));
        parentTile.ReportForwardedChildPackageActivity(message, percentage);
    }

    /// <summary>
    /// Removes inactive child activity subscriptions that are no longer shown in current child lists.
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
        return !String.IsNullOrWhiteSpace(parentName) &&
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

        RestoreSelection(restoredPatches, selectedKeys);
        RestoreSelection(restoredAddons, selectedKeys);
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
        IReadOnlySet<LauncherContentKey> selectedKeys)
    {
        foreach (ModificationViewModel modification in modifications)
        {
            modification.IsSelected = selectedKeys.Contains(modification.ContainerModification.ContentKey);
        }
    }

    private bool ReplaceMatchingContent(
        ObservableCollection<ModificationViewModel> source,
        ModificationViewModel replacement)
    {
        int existingIndex = source
            .Select((modification, index) => (modification, index))
            .Where(entry =>
                entry.modification.ContainerModification.ContentKey ==
                replacement.ContainerModification.ContentKey)
            .Select(entry => entry.index)
            .DefaultIfEmpty(-1)
            .First();
        if (existingIndex < 0)
        {
            return false;
        }

        ModificationViewModel existing = source[existingIndex];
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
