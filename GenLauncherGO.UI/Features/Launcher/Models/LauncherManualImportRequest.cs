using Avalonia.Controls;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.UI.Features.Launcher.Models;

/// <summary>
/// Describes a user-driven manual launcher content import request.
/// </summary>
/// <param name="Kind">The kind of content to import.</param>
/// <param name="Owner">The owner window used for dialogs.</param>
/// <param name="ParentContentName">The parent modification name, or <see langword="null"/> for original-game content.</param>
internal sealed record LauncherManualImportRequest(
    ModificationType Kind,
    Window Owner,
    string? ParentContentName = null);
