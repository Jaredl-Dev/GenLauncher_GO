using System.Collections.Generic;
using System.Threading;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Core.Mods.Contracts;

/// <summary>
///     Imports user-selected modification files into a launcher-managed content folder.
/// </summary>
public interface IManualModificationImporter
{
    /// <summary>
    ///     Imports the source files into the explicitly owned destination directory.
    /// </summary>
    void Import(
        IReadOnlyList<string> sourceFilePaths,
        OwnedContentPath destinationPath,
        CancellationToken cancellationToken = default);
}
