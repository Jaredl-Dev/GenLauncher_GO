using System;
using System.Threading;
using GenLauncherGO.Infrastructure.Archives.Contracts;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Records what was asked of the extractor and lets the test decide what lands in the destination.
/// </summary>
internal sealed class RecordingArchiveExtractor : IArchiveExtractor
{
    public string? ArchiveFilePath { get; private set; }

    public string? DestinationDirectory { get; private set; }

    public bool? ConvertBigFilesToGib { get; private set; }

    /// <summary>
    ///     Populates the destination directory, which is given as its only argument.
    /// </summary>
    public Action<string>? ExtractHandler { get; init; }

    public void ExtractToDirectory(
        string archiveFilePath,
        string destinationDirectory,
        bool convertBigFilesToGib = false,
        CancellationToken cancellationToken = default)
    {
        ArchiveFilePath = archiveFilePath;
        DestinationDirectory = destinationDirectory;
        ConvertBigFilesToGib = convertBigFilesToGib;
        ExtractHandler?.Invoke(destinationDirectory);
    }
}
