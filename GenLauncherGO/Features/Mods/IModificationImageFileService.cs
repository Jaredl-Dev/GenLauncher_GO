using System.Threading;
using System.Threading.Tasks;

namespace GenLauncherGO.Features.Mods;

/// <summary>
///     Provides launcher modification image cache file operations.
/// </summary>
/// <remarks>
///     The modification type participates in cache identity because advertising names retain legacy filesystem
///     normalization.
/// </remarks>
internal interface IModificationImageFileService
{
    /// <summary>
    ///     Finds an existing cached modification image with any extension.
    /// </summary>
    string? FindExistingImageFilePath(
        ModificationType modificationType,
        string modificationName,
        string imageBaseName);

    /// <summary>
    ///     Counts cached image files for a modification.
    /// </summary>
    int CountImageFiles(ModificationType modificationType, string modificationName);

    /// <summary>
    ///     Determines whether a path inside the active launcher-owned image cache points to an existing file.
    /// </summary>
    bool ImageExists(string? imageFilePath);

    /// <summary>
    ///     Removes cached images for a logical image identity and reports whether no matching image remains.
    /// </summary>
    /// <remarks>The implementation resolves the logical identity inside the active image cache ownership boundary.</remarks>
    bool TryDeleteImage(
        ModificationType modificationType,
        string modificationName,
        string imageBaseName);

    /// <summary>
    ///     Replaces cached images for a modification image base name with a selected source image.
    /// </summary>
    Task<string> ReplaceImageAsync(
        string modificationName,
        string imageBaseName,
        string sourceImagePath,
        CancellationToken cancellationToken);
}
