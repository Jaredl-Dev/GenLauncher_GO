using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.UI.Features.Mods;

internal sealed class ModificationVersionSelection
{
    public ModificationVersionSelection(
        LauncherContentVersion selectedModification,
        string version,
        ModificationViewModel modificationViewModel)
    {
        SelectedVersion = selectedModification;
        VersionName = version;
        ModificationViewModel = modificationViewModel;
    }

    public ModificationVersionSelection()
    {
    }

    public string VersionName { get; set; } = string.Empty;

    public LauncherContentVersion SelectedVersion { get; set; } = null!;

    public ModificationViewModel ModificationViewModel { get; set; } = null!;
}
