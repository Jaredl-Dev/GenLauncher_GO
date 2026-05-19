using System.Collections.Generic;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Infrastructure.Mods.Contracts;

/// <summary>
///     Provides local file-system operations for launcher-managed content.
/// </summary>
internal interface ILocalLauncherContentService
{
    /// <summary>
    ///     Finds installed content versions under the launcher-owned mods directory.
    /// </summary>
    IReadOnlyList<LauncherContentVersion> FindInstalledVersions(LauncherPaths paths);

    /// <summary>
    ///     Removes empty package-recovery directories that no longer contain a durable backup.
    /// </summary>
    void DeleteEmptyPackageBackupDirectories(LauncherPaths paths);

    /// <summary>
    ///     Deletes an installed content version from the launcher-owned mods directory.
    /// </summary>
    void DeleteVersion(
        LauncherPaths paths,
        LauncherContentKey contentKey);

    /// <summary>
    ///     Deletes all installed content files for a content card from the launcher-owned mods directory.
    /// </summary>
    void DeleteContent(
        LauncherPaths paths,
        LauncherContentKey contentKey);

    /// <summary>
    ///     Deletes the launcher-owned image cache when no content card still references the same content name.
    /// </summary>
    void DeleteImagesIfUnused(
        LauncherPaths paths,
        LauncherContentKey contentKey,
        LauncherData launcherData);
}
