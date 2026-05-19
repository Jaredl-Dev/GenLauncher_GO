using System;

namespace GenLauncherGO.UI.Features.Dialogs.Models;

internal sealed class ManualModificationDialogResult(
    string modificationName,
    string version)
{
    public string ModificationName { get; } =
        modificationName ?? throw new ArgumentNullException(nameof(modificationName));

    public string Version { get; } = version ?? throw new ArgumentNullException(nameof(version));
}
