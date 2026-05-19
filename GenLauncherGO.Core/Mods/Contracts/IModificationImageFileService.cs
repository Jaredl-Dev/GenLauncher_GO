using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Core.Mods.Contracts;

/// <summary>
/// Provides launcher modification image cache file operations.
/// </summary>
public interface IModificationImageFileService
{
    /// <summary>
    /// Finds an existing cached modification image with any extension.
    /// </summary>
    string? FindExistingImageFilePath(string modificationName, string imageBaseName);

    /// <summary>
    /// Counts cached image files for a modification.
    /// </summary>
    int CountImageFiles(string modificationName);

    /// <summary>
    /// Determines whether a path inside the active launcher-owned image cache points to an existing file.
    /// </summary>
    bool ImageExists(string? imageFilePath);

    /// <summary>
    /// Removes cached images for a logical image identity and reports whether no matching image remains.
    /// </summary>
    /// <remarks>The implementation resolves the logical identity inside the active image cache ownership boundary.</remarks>
    bool TryDeleteImage(string modificationName, string imageBaseName);

    /// <summary>
    /// Replaces cached images for a modification image base name with a selected source image.
    /// </summary>
    Task<string> ReplaceImageAsync(
        ModificationImageReplacementRequest request,
        CancellationToken cancellationToken);
}
