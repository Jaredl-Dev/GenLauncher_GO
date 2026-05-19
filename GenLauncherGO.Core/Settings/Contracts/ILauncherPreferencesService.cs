using System;
using GenLauncherGO.Core.Settings.Exceptions;
using GenLauncherGO.Core.Settings.Models;

namespace GenLauncherGO.Core.Settings.Contracts;

/// <summary>
///     Provides the current launcher preferences and persists preference updates.
/// </summary>
public interface ILauncherPreferencesService
{
    /// <summary>
    ///     Gets the current launcher preferences.
    /// </summary>
    LauncherPreferences Current { get; }

    /// <summary>
    ///     Occurs after launcher preferences have changed.
    /// </summary>
    event EventHandler<LauncherPreferences>? PreferencesChanged;

    /// <summary>
    ///     Persists the supplied launcher preferences and publishes the updated state.
    /// </summary>
    /// <exception cref="LauncherPreferencesPersistenceException">
    ///     Thrown when the requested preferences cannot be persisted. In that case,
    ///     <see cref="Current" /> and <see cref="PreferencesChanged" /> remain unchanged.
    /// </exception>
    void Update(LauncherPreferences preferences);
}
