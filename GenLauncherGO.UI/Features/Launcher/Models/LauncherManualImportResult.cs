using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.UI.Features.Launcher.Models;

internal sealed record LauncherManualImportResult(
    ModificationType Kind,
    LauncherContent Modification);
