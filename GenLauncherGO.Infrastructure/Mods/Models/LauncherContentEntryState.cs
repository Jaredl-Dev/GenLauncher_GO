using System.Collections.Generic;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Infrastructure.Mods.Models;

/// <summary>
///     Stores local state for one launcher content card without remote manifest metadata.
/// </summary>
internal sealed class LauncherContentEntryState
{
    public ModificationType ModificationType { get; set; }

    public string Name { get; set; } = string.Empty;

    public string DependenceName { get; set; } = string.Empty;

    public bool Installed { get; set; }

    public bool IsSelected { get; set; }

    public int NumberInList { get; set; }

    /// <remarks>
    ///     The property name is the existing on-disk YAML key and must remain compatible with saved launcher data.
    /// </remarks>
    public List<LauncherContentVersionState> ModificationVersions { get; set; } =
        [];
}
