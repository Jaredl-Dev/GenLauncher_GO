using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Features.Startup;

namespace GenLauncherGO.Features.Integrity;

/// <summary>
///     Verifies, snapshots, and cleans launcher-owned content.
/// </summary>
internal interface IContentIntegrityService
{
    /// <summary>
    ///     Verifies all targets against trusted snapshots owned by the supplied immutable game namespace.
    /// </summary>
    Task<ContentIntegrityReport> VerifyAsync(
        LauncherPaths paths,
        IReadOnlyList<ContentIntegrityTarget> targets,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Captures a trusted snapshot in the supplied immutable game namespace only when a target currently contains
    ///     exactly the expected safe file set.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> when a snapshot was captured; otherwise, <see langword="false" /> when the current
    ///     file set contains extras, missing files, empty directories, unsafe links, or unreadable entries.
    /// </returns>
    Task<bool> CaptureSnapshotIfMatchesExpectedFileSetAsync(
        LauncherPaths paths,
        ContentIntegrityTarget target,
        IReadOnlySet<string> expectedRelativePaths,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Replaces a target's trusted snapshot with its current safe directory contents.
    /// </summary>
    Task CaptureSnapshotAsync(
        LauncherPaths paths,
        ContentIntegrityTarget target,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Deletes persisted snapshots outside the retained target set and reports how many were removed.
    /// </summary>
    /// <remarks>
    ///     Content the user deletes would otherwise leave its snapshot behind forever, because nothing else revisits
    ///     the snapshot directory once a target stops being verified.
    /// </remarks>
    int PruneSnapshots(LauncherPaths paths, IReadOnlySet<string> retainedTargetIds);

    /// <summary>
    ///     Deletes managed entries explicitly listed for deletion in a verification report, resolving them only within
    ///     the verified targets.
    /// </summary>
    Task ApplyCleanupAsync(
        ContentIntegrityReport report,
        IReadOnlyList<ContentIntegrityTarget> targets,
        CancellationToken cancellationToken);
}
