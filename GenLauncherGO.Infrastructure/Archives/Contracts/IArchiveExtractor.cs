using System;
using System.IO;
using System.Threading;

namespace GenLauncherGO.Infrastructure.Archives.Contracts;

internal interface IArchiveExtractor
{
    /// <summary>
    /// Extracts an archive into the specified destination directory, creating directories and overwriting existing
    /// extracted files when needed. Entries may not escape the destination, and extraction can optionally rename
    /// <c>.big</c> files to <c>.gib</c>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="archiveFilePath"/> or <paramref name="destinationDirectory"/> is empty or
    /// whitespace.
    /// </exception>
    /// <exception cref="IOException">
    /// Thrown when the archive or destination files cannot be read or written.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// Thrown when the archive is invalid or contains an entry that would extract outside the destination directory.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">
    /// Thrown when the current process does not have access to read the archive or write extracted files.
    /// </exception>
    void ExtractToDirectory(
        string archiveFilePath,
        string destinationDirectory,
        bool convertBigFilesToGib = false,
        CancellationToken cancellationToken = default);
}
