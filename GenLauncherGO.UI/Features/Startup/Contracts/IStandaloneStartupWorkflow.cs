using System.Threading.Tasks;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.UI.Features.Startup.Models;

namespace GenLauncherGO.UI.Features.Startup.Contracts;

/// <summary>
///     Owns the blocking Avalonia setup and initial-selection sequence before a game session is composed.
/// </summary>
internal interface IStandaloneStartupWorkflow
{
    /// <summary>
    ///     Shows the blocking location dialog when the launcher is physically inside a supported game installation.
    /// </summary>
    /// <param name="storagePaths">The resolved standalone launcher paths.</param>
    /// <returns><see langword="true" /> when startup must stop.</returns>
    Task<bool> ShowBlockingLauncherLocationAsync(LauncherStoragePaths storagePaths);

    /// <summary>
    ///     Repairs or creates standalone configuration and returns the selected validated installation.
    /// </summary>
    Task<StandaloneStartupResult> RunAsync(
        LauncherStoragePaths storagePaths,
        ILauncherPreferencesService preferencesService);
}
