using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.UI.Features.Integrity;

/// <summary>
///     Receives UI progress updates while launch content integrity issues are repaired.
/// </summary>
internal interface ILaunchContentIntegrityProgressTarget
{
    /// <summary>
    ///     Gets the active version represented by this progress target.
    /// </summary>
    LauncherContentVersion ActiveIntegrityVersion { get; }

    /// <summary>
    ///     Applies the initial integrity repair progress state.
    /// </summary>
    /// <param name="message">The initial progress message.</param>
    void BeginIntegrityProgress(string message);

    /// <summary>
    ///     Reports an integrity repair progress update.
    /// </summary>
    /// <param name="message">The progress message.</param>
    /// <param name="percentage">The progress percentage.</param>
    void ReportIntegrityProgress(string message, int percentage);

    /// <summary>
    ///     Restores the normal UI state after integrity repair progress has completed or failed.
    /// </summary>
    void CompleteIntegrityProgress();
}
