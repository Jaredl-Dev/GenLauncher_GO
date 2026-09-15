using System.Collections.Generic;
using System.Threading;
using GenLauncherGO.Shared.IO;

namespace GenLauncherGO.Features.Mods;

/// <summary>
///     Imports user-selected modification content into a launcher-managed content folder.
/// </summary>
internal interface IManualModificationImporter
{
    /// <summary>
    ///     Imports the source files and folders into the explicitly owned destination directory.
    /// </summary>
    /// <param name="sourcePaths">
    ///     The selected files and folders. A folder contributes its contents, not the folder itself.
    /// </param>
    /// <param name="destinationPath">The launcher-owned content directory the import is written to.</param>
    /// <param name="cancellationToken">The token used to cancel import work.</param>
    void Import(
        IReadOnlyList<string> sourcePaths,
        OwnedContentPath destinationPath,
        CancellationToken cancellationToken = default);
}
