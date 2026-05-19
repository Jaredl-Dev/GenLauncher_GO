using System;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Exceptions;
using GenLauncherGO.Core.Settings.Models;

namespace GenLauncherGO.UI.Features.Settings;

/// <summary>
///     Applies user-requested preference changes and converts the expected persistence failure into a UI outcome.
/// </summary>
internal static class LauncherPreferencesUpdate
{
    public static bool TryApply(
        ILauncherPreferencesService preferencesService,
        LauncherPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferencesService);
        ArgumentNullException.ThrowIfNull(preferences);

        try
        {
            preferencesService.Update(preferences);
            return true;
        }
        catch (LauncherPreferencesPersistenceException)
        {
            return false;
        }
    }
}
