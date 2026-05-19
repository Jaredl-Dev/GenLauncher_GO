using GenLauncherGO.Core.Integrity.Models;

namespace GenLauncherGO.Core.Mods.Models;

/// <summary>
///     Stores mutable local state for one launcher content version.
/// </summary>
/// <remarks>
///     Remote metadata is immutable on <see cref="LauncherContentVersion" />. Installation discovery, selection, and
///     integrity trust decisions mutate this separate state and are the only values persisted locally for a version.
/// </remarks>
public sealed class LauncherContentInstallation
{
    public bool Installed { get; set; }

    public bool IsSelected { get; set; }

    /// <summary>
    ///     Gets or sets whether a download for this version stopped with its partial content deliberately kept.
    /// </summary>
    /// <remarks>
    ///     Set when the launcher closes while a download is in flight, so the next session offers to resume instead of
    ///     starting over. An explicit cancel clears the partial content and leaves this false.
    /// </remarks>
    public bool DownloadSuspended { get; set; }

    /// <summary>
    ///     Gets or sets the progress the suspended download had reached, as a percentage.
    /// </summary>
    /// <remarks>
    ///     This restores the progress bar's position for the user. It is a display value only: the byte offset a
    ///     resumed transfer actually continues from is derived from the partial content on disk, so a stale value here
    ///     can never cause the wrong range to be requested.
    /// </remarks>
    public double SuspendedProgressPercentage { get; set; }

    public ContentSourceKind ContentSourceKind { get; set; } = ContentSourceKind.UnknownLegacy;
}
