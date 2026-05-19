using System;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.UI.Features.Mods;

internal sealed class ModificationVersionSelection
{
    public ModificationVersionSelection(
        LauncherContentVersion selectedVersion,
        ModificationViewModel modificationViewModel)
    {
        SelectedVersion = selectedVersion ?? throw new ArgumentNullException(nameof(selectedVersion));
        ModificationViewModel = modificationViewModel ??
                                throw new ArgumentNullException(nameof(modificationViewModel));
    }

    public string VersionName => SelectedVersion.Version;

    public LauncherContentVersion SelectedVersion { get; }

    public ModificationViewModel ModificationViewModel { get; }
}
