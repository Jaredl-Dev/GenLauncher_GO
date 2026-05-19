using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Infrastructure.Mods.Models;

/// <summary>
///     Stores local state for one launcher content version without remote manifest metadata.
/// </summary>
internal sealed class LauncherContentVersionState
{
    public ModificationType ModificationType { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string DependenceName { get; set; } = string.Empty;

    public bool Installed { get; set; }

    public bool IsSelected { get; set; }

    /// <summary>
    ///     Gets or sets whether partial download content was deliberately kept for resuming in a later session.
    /// </summary>
    public bool DownloadSuspended { get; set; }

    /// <summary>
    ///     Gets or sets the progress the suspended download had reached, so its bar can be restored.
    /// </summary>
    public double SuspendedProgressPercentage { get; set; }

    public ContentSourceKind ContentSourceKind { get; set; } = ContentSourceKind.UnknownLegacy;
}
