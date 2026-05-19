using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenLauncherGO.Core.Launching.Contracts;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Launcher.Models;
using GenLauncherGO.UI.Features.Launcher.Services;
using GenLauncherGO.UI.Features.Startup;
using GenLauncherGO.UI.Shared.Localization;

namespace GenLauncherGO.UI.Features.Settings.ViewModels;

/// <summary>
///     Provides searchable executable registration and editor state for one active-game category.
/// </summary>
internal sealed class LauncherExecutableManagementViewModel : ObservableObject
{
    private readonly List<ExecutableOption> _allEntries = [];
    private readonly IGameExecutableDiscoveryService _discoveryService;
    private readonly ILauncherPreferencesService _preferencesService;
    private readonly LauncherRuntimeContext _runtimeContext;
    private readonly LauncherExecutableSelectionService _selectionService;
    private readonly ILauncherStringLocalizer _stringLocalizer;
    private string? _editingExecutableName;
    private GameLaunchTargetKind _kind;

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

    public ObservableCollection<ExecutableOption> Entries { get; } = [];

    public IRelayCommand<object?> RequestRemoveCommand { get; }

    public IRelayCommand CloseCommand { get; }

    public string GameDirectory => _runtimeContext.RuntimePaths.ActivePaths.GameDirectory;

    public string WindowTitle => _kind == GameLaunchTargetKind.GameClient
        ? _stringLocalizer["ManageGameClients"]
        : _stringLocalizer["ManageWorldBuilders"];

    public string EditorTitle => IsEditing
        ? _stringLocalizer["EditExecutable"]
        : _stringLocalizer["AddExecutable"];

    public bool IsEditing => _editingExecutableName != null;

    public string SearchText
    {
        get;
        set
        {
            if (SetProperty(ref field, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    } = string.Empty;

    public string DisplayName
    {
        get;
        set
        {
            if (SetProperty(ref field, value ?? string.Empty))
            {
                ValidationMessage = string.Empty;
                OnPropertyChanged(nameof(CanSave));
            }
        }
    } = string.Empty;

    public string ExecutableName
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
    } = string.Empty;

    public string ValidationMessage
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    public bool CanSave =>
        !string.IsNullOrWhiteSpace(DisplayName) &&
        !string.IsNullOrWhiteSpace(ExecutableName);

    public bool LastPersistenceSaveFailed
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public event EventHandler? CloseRequested;

    /// <summary>
    ///     Occurs when a custom entry is awaiting removal confirmation.
    /// </summary>
    public event Action<ExecutableOption>? RemoveRequested;

    public void Initialize(GameLaunchTargetKind kind)
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
    ///     Validates and applies a local path returned by the executable picker.
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
    ///     Persists the current add/edit draft and refreshes the manager list.
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

        // Materialized because both name checks below read it.
        var comparisonEntries = _allEntries.Where(entry =>
            _editingExecutableName == null ||
            !string.Equals(
                entry.ExecutableName,
                _editingExecutableName,
                StringComparison.OrdinalIgnoreCase)).ToList();
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
    ///     Removes a confirmed custom entry without deleting its executable file.
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
            // Materialized because the available-first lookup below falls back to a
            // second read of the same sequence.
            var remainingOptions = _allEntries.Where(entry =>
                !string.Equals(
                    entry.ExecutableName,
                    option.ExecutableName,
                    StringComparison.OrdinalIgnoreCase)).ToList();
            ExecutableOption? fallback = LauncherExecutableSelectionService.SelectPreferredOption(
                remainingOptions,
                null);
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
        IReadOnlyList<ExecutableOption> options = _selectionService.GetOptions(_kind);
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
        bool saved = LauncherPreferencesUpdate.TryApply(
            _preferencesService,
            current with
            {
                Games = current.Games.With(
                    _runtimeContext.CurrentlyManagedGame,
                    updatedGamePreferences)
            });
        if (!saved)
        {
            LastPersistenceSaveFailed = true;
        }

        return saved;
    }

    private LauncherGamePreferences GetActiveGamePreferences(LauncherPreferences preferences)
    {
        return preferences.Games.Get(_runtimeContext.CurrentlyManagedGame);
    }

    private IReadOnlyList<LauncherCustomExecutable> GetCustomEntries(LauncherGamePreferences preferences)
    {
        return preferences.GetCustomExecutables(_kind);
    }

    private LauncherGamePreferences SetCustomEntries(
        LauncherGamePreferences preferences,
        IReadOnlyList<LauncherCustomExecutable> entries)
    {
        return preferences.WithCustomExecutables(_kind, entries);
    }

    private string GetSelectedExecutable(LauncherGamePreferences preferences)
    {
        return preferences.GetSelectedExecutable(_kind);
    }

    private LauncherGamePreferences SetSelectedExecutable(
        LauncherGamePreferences preferences,
        string executableName)
    {
        return preferences.WithSelectedExecutable(_kind, executableName);
    }
}
