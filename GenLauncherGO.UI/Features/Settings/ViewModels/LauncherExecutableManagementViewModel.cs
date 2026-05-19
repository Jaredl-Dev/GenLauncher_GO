using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Exceptions;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Settings.Models;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.UI.Features.Settings.ViewModels;

/// <summary>
/// Provides searchable executable registration and editor state for one active-game category.
/// </summary>
internal sealed class LauncherExecutableManagementViewModel : ObservableObject
{
    private readonly ILauncherPreferencesService _preferencesService;
    private readonly IGameExecutableDiscoveryService _discoveryService;
    private readonly LauncherExecutableSelectionService _selectionService;
    private readonly LauncherRuntimeContext _runtimeContext;
    private readonly ILauncherStringLocalizer _stringLocalizer;
    private readonly List<ExecutableOption> _allEntries = new();
    private LauncherExecutableManagementKind _kind;
    private string _displayName = string.Empty;
    private string _executableName = string.Empty;
    private string _searchText = string.Empty;
    private string _validationMessage = string.Empty;
    private string? _editingExecutableName;
    private bool _lastPersistenceSaveFailed;

    public LauncherExecutableManagementViewModel(
        ILauncherPreferencesService preferencesService,
        IGameExecutableDiscoveryService discoveryService,
        LauncherExecutableSelectionService selectionService,
        LauncherRuntimeContext runtimeContext,
        ILauncherStringLocalizer stringLocalizer)
    {
        _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        _discoveryService = discoveryService ?? throw new ArgumentNullException(nameof(discoveryService));
        _selectionService = selectionService ?? throw new ArgumentNullException(nameof(selectionService));
        _runtimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));

        RequestRemoveCommand = new RelayCommand<object?>(
            option => RequestRemove(option as ExecutableOption),
            option => option is ExecutableOption { CanRemove: true });
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? CloseRequested;

    /// <summary>
    /// Occurs when a custom entry is awaiting removal confirmation.
    /// </summary>
    public event Action<ExecutableOption>? RemoveRequested;

    public ObservableCollection<ExecutableOption> Entries { get; } = new();

    public IRelayCommand<object?> RequestRemoveCommand { get; }

    public IRelayCommand CloseCommand { get; }

    public string GameDirectory => _runtimeContext.RuntimePaths.ActivePaths.GameDirectory;

    public string WindowTitle => _kind == LauncherExecutableManagementKind.GameClient
        ? _stringLocalizer["ManageGameClients"]
        : _stringLocalizer["ManageWorldBuilders"];

    public string EditorTitle => IsEditing
        ? _stringLocalizer["EditExecutable"]
        : _stringLocalizer["AddExecutable"];

    public bool IsEditing => _editingExecutableName != null;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetProperty(ref _displayName, value ?? string.Empty))
            {
                ValidationMessage = string.Empty;
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public string ExecutableName
    {
        get => _executableName;
        private set
        {
            if (SetProperty(ref _executableName, value))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public bool CanSave =>
        !string.IsNullOrWhiteSpace(DisplayName) &&
        !string.IsNullOrWhiteSpace(ExecutableName);

    public bool LastPersistenceSaveFailed
    {
        get => _lastPersistenceSaveFailed;
        private set => SetProperty(ref _lastPersistenceSaveFailed, value);
    }

    public void Initialize(LauncherExecutableManagementKind kind)
    {
        _kind = kind;
        SearchText = string.Empty;
        ResetEditor();
        RefreshEntries();
        OnPropertyChanged(nameof(WindowTitle));
    }

    public void BeginAdd()
    {
        ResetEditor();
    }

    public void BeginEdit(ExecutableOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (!option.CanRemove)
        {
            throw new ArgumentException("Built-in executable registrations cannot be edited.", nameof(option));
        }

        _editingExecutableName = option.ExecutableName;
        DisplayName = option.DisplayName;
        ExecutableName = option.ExecutableName;
        ValidationMessage = string.Empty;
        LastPersistenceSaveFailed = false;
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(EditorTitle));
    }

    /// <summary>
    /// Validates and applies a local path returned by the executable picker.
    /// </summary>
    public void SetSelectedExecutablePath(string selectedPath)
    {
        try
        {
            string fullPath = Path.GetFullPath(selectedPath);
            string? selectedDirectory = Path.GetDirectoryName(fullPath);
            if (selectedDirectory == null ||
                !string.Equals(
                    Path.TrimEndingDirectorySeparator(selectedDirectory),
                    Path.TrimEndingDirectorySeparator(GameDirectory),
                    StringComparison.OrdinalIgnoreCase))
            {
                ValidationMessage = _stringLocalizer["ExecutableMustBeInGameRoot"];
                ExecutableName = string.Empty;
                return;
            }

            string executableName = LauncherFileSystemLayout.NormalizeExecutableFileName(Path.GetFileName(fullPath));
            if (!_discoveryService.IsExecutableAvailable(executableName))
            {
                ValidationMessage = _stringLocalizer["ExecutableUnavailable"];
                ExecutableName = string.Empty;
                return;
            }

            ExecutableName = executableName;
            ValidationMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(DisplayName))
            {
                DisplayName = Path.GetFileNameWithoutExtension(executableName);
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or NotSupportedException)
        {
            ValidationMessage = _stringLocalizer["ExecutableUnavailable"];
            ExecutableName = string.Empty;
        }
    }

    /// <summary>
    /// Persists the current add/edit draft and refreshes the manager list.
    /// </summary>
    public bool TrySaveEditor()
    {
        LastPersistenceSaveFailed = false;
        LauncherCustomExecutable executable;
        try
        {
            executable = new LauncherCustomExecutable(DisplayName, ExecutableName);
        }
        catch (ArgumentException)
        {
            ValidationMessage = _stringLocalizer["ExecutableDetailsRequired"];
            return false;
        }

        bool executableChanged = !string.Equals(
            executable.ExecutableName,
            _editingExecutableName,
            StringComparison.OrdinalIgnoreCase);
        if ((_editingExecutableName == null || executableChanged) &&
            !_discoveryService.IsExecutableAvailable(executable.ExecutableName))
        {
            ValidationMessage = _stringLocalizer["ExecutableUnavailable"];
            ExecutableName = string.Empty;
            return false;
        }

        IEnumerable<ExecutableOption> comparisonEntries = _allEntries.Where(entry =>
            _editingExecutableName == null ||
            !string.Equals(
                entry.ExecutableName,
                _editingExecutableName,
                StringComparison.OrdinalIgnoreCase));
        if (comparisonEntries.Any(entry =>
                string.Equals(entry.DisplayName, executable.DisplayName, StringComparison.OrdinalIgnoreCase)))
        {
            ValidationMessage = _stringLocalizer["ExecutableNameAlreadyExists"];
            return false;
        }

        if (comparisonEntries.Any(entry =>
                string.Equals(entry.ExecutableName, executable.ExecutableName, StringComparison.OrdinalIgnoreCase)))
        {
            ValidationMessage = _stringLocalizer["ExecutableFileAlreadyExists"];
            return false;
        }

        LauncherPreferences current = _preferencesService.Current;
        LauncherGamePreferences gamePreferences = GetActiveGamePreferences(current);
        IReadOnlyList<LauncherCustomExecutable> currentEntries = GetCustomEntries(gamePreferences);
        IReadOnlyList<LauncherCustomExecutable> updatedEntries = _editingExecutableName == null
            ? currentEntries.Append(executable).ToArray()
            : currentEntries.Select(entry => string.Equals(
                    entry.ExecutableName,
                    _editingExecutableName,
                    StringComparison.OrdinalIgnoreCase)
                ? executable
                : entry).ToArray();
        LauncherGamePreferences updatedGamePreferences = SetCustomEntries(gamePreferences, updatedEntries);
        if (_editingExecutableName != null &&
            string.Equals(
                GetSelectedExecutable(gamePreferences),
                _editingExecutableName,
                StringComparison.OrdinalIgnoreCase))
        {
            updatedGamePreferences = SetSelectedExecutable(
                updatedGamePreferences,
                executable.ExecutableName);
        }

        if (!TryUpdatePreferences(current, updatedGamePreferences))
        {
            return false;
        }

        ResetEditor();
        RefreshEntries();
        return true;
    }

    /// <summary>
    /// Removes a confirmed custom entry without deleting its executable file.
    /// </summary>
    public bool Remove(ExecutableOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        LastPersistenceSaveFailed = false;
        if (!option.CanRemove)
        {
            return false;
        }

        LauncherPreferences current = _preferencesService.Current;
        LauncherGamePreferences gamePreferences = GetActiveGamePreferences(current);
        IReadOnlyList<LauncherCustomExecutable> updatedEntries = GetCustomEntries(gamePreferences)
            .Where(executable => !string.Equals(
                executable.ExecutableName,
                option.ExecutableName,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        LauncherGamePreferences updatedGamePreferences = SetCustomEntries(gamePreferences, updatedEntries);
        if (string.Equals(
                GetSelectedExecutable(updatedGamePreferences),
                option.ExecutableName,
                StringComparison.OrdinalIgnoreCase))
        {
            IEnumerable<ExecutableOption> remainingOptions = _allEntries.Where(entry =>
                !string.Equals(
                    entry.ExecutableName,
                    option.ExecutableName,
                    StringComparison.OrdinalIgnoreCase));
            ExecutableOption? fallback = remainingOptions.FirstOrDefault(entry => entry.IsAvailable)
                                         ?? remainingOptions.FirstOrDefault();
            updatedGamePreferences = SetSelectedExecutable(
                updatedGamePreferences,
                fallback?.ExecutableName ?? string.Empty);
        }

        if (!TryUpdatePreferences(current, updatedGamePreferences))
        {
            return false;
        }

        RefreshEntries();
        return true;
    }

    private void RequestRemove(ExecutableOption? option)
    {
        if (option is { CanRemove: true })
        {
            RemoveRequested?.Invoke(option);
        }
    }

    private void ResetEditor()
    {
        _editingExecutableName = null;
        DisplayName = string.Empty;
        ExecutableName = string.Empty;
        ValidationMessage = string.Empty;
        LastPersistenceSaveFailed = false;
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(EditorTitle));
    }

    private void RefreshEntries()
    {
        _allEntries.Clear();
        IReadOnlyList<ExecutableOption> options = _kind == LauncherExecutableManagementKind.GameClient
            ? _selectionService.GetGameClientOptions()
            : _selectionService.GetWorldBuilderOptions();
        _allEntries.AddRange(options);
        ApplyFilter();
        RequestRemoveCommand.NotifyCanExecuteChanged();
    }

    private void ApplyFilter()
    {
        Entries.Clear();
        IEnumerable<ExecutableOption> filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allEntries
            : _allEntries.Where(entry =>
                entry.DisplayName.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase) ||
                entry.ExecutableName.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase));
        foreach (ExecutableOption entry in filtered)
        {
            Entries.Add(entry);
        }
    }

    private bool TryUpdatePreferences(
        LauncherPreferences current,
        LauncherGamePreferences updatedGamePreferences)
    {
        try
        {
            _preferencesService.Update(current with
            {
                Games = current.Games.With(
                    _runtimeContext.CurrentlyManagedGame,
                    updatedGamePreferences),
            });
            return true;
        }
        catch (LauncherPreferencesPersistenceException)
        {
            LastPersistenceSaveFailed = true;
            return false;
        }
    }

    private LauncherGamePreferences GetActiveGamePreferences(LauncherPreferences preferences)
    {
        return preferences.Games.Get(_runtimeContext.CurrentlyManagedGame);
    }

    private IReadOnlyList<LauncherCustomExecutable> GetCustomEntries(LauncherGamePreferences preferences)
    {
        return _kind == LauncherExecutableManagementKind.GameClient
            ? preferences.CustomGameClients
            : preferences.CustomWorldBuilders;
    }

    private LauncherGamePreferences SetCustomEntries(
        LauncherGamePreferences preferences,
        IReadOnlyList<LauncherCustomExecutable> entries)
    {
        return _kind == LauncherExecutableManagementKind.GameClient
            ? preferences with { CustomGameClients = entries }
            : preferences with { CustomWorldBuilders = entries };
    }

    private string GetSelectedExecutable(LauncherGamePreferences preferences)
    {
        return _kind == LauncherExecutableManagementKind.GameClient
            ? preferences.SelectedGameClient
            : preferences.SelectedWorldBuilder;
    }

    private LauncherGamePreferences SetSelectedExecutable(
        LauncherGamePreferences preferences,
        string executableName)
    {
        return _kind == LauncherExecutableManagementKind.GameClient
            ? preferences with { SelectedGameClient = executableName }
            : preferences with { SelectedWorldBuilder = executableName };
    }

}
