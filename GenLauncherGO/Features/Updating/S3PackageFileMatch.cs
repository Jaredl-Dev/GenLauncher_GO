using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Shared.IO;

namespace GenLauncherGO.Features.Updating;

/// <summary>
///     Decides whether the file already on disk is the file a remote manifest entry describes.
/// </summary>
/// <remarks>
///     A download reads a match as "already transferred" and launch verification reads it as "already trusted". Both
///     answer the same question about the same bytes, so they share this rule; if they diverged, one path would accept
///     a package the other rejects.
/// </remarks>
internal static class S3PackageFileMatch
{
    /// <summary>
    ///     Determines whether the downloaded or installed variant of a manifest file matches its recorded size and,
    ///     when the hash policy covers it, its MD5.
    /// </summary>
    /// <param name="fileHashService">The hash service used when the manifest publishes a reliable MD5.</param>
    /// <param name="file">The manifest entry describing the expected file.</param>
    /// <param name="destinationFilePath">
    ///     The path the manifest entry resolves to. The converted <c>.gib</c> variant is accepted in its place, because
    ///     an installed package holds <c>.big</c> files under that name.
    /// </param>
    /// <param name="hashCheckedExtensions">The file kinds whose manifest MD5 is validated.</param>
    /// <param name="cancellationToken">The token that cancels hashing.</param>
    public static async Task<bool> MatchesAsync(
        IFileHashService fileHashService,
        RemoteFileManifestEntry file,
        string destinationFilePath,
        IReadOnlySet<string> hashCheckedExtensions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fileHashService);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(hashCheckedExtensions);

        string existingFilePath = BigFileVariantPath.GetExistingDownloadedPath(destinationFilePath);
        if (string.IsNullOrWhiteSpace(existingFilePath) ||
            new FileInfo(existingFilePath).Length != (long)file.Size)
        {
            return false;
        }

        if (!S3HashValidationPolicy.ShouldCheckHash(file, hashCheckedExtensions))
        {
            return true;
        }

        string hashSum = await fileHashService.ComputeMd5HashAsync(existingFilePath, cancellationToken)
            .ConfigureAwait(false);

        return string.Equals(hashSum, file.Hash, StringComparison.OrdinalIgnoreCase);
    }
}
