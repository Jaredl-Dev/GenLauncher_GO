using System;
using System.Collections.Generic;
using System.Linq;

namespace GenLauncherGO.UI.Features.Dialogs.Models;

internal sealed class ManualModificationDialogRequest(
    IReadOnlyList<string> files,
    string? parentContentName = null)
{
    public IReadOnlyList<string> Files { get; } =
        (files ?? throw new ArgumentNullException(nameof(files))).ToList();

    public string? ParentContentName { get; } = parentContentName;
}
