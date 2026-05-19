using System;
using System.Collections.Generic;
using System.Linq;

namespace GenLauncherGO.UI.Features.Dialogs.Models;

internal sealed class ManualModificationDialogResult(
    IReadOnlyList<string> files,
    string? parentContentName,
    string modificationName,
    string version)
{
    public IReadOnlyList<string> Files { get; } =
        (files ?? throw new ArgumentNullException(nameof(files))).ToList();

    public string? ParentContentName { get; } = parentContentName;

    public string ModificationName { get; } =
        modificationName ?? throw new ArgumentNullException(nameof(modificationName));

    public string Version { get; } = version ?? throw new ArgumentNullException(nameof(version));
}
