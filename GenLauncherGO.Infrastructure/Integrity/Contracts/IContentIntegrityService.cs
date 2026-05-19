using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Infrastructure.Integrity.Contracts;

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
    ///     Deletes managed entries explicitly listed for deletion in a verification report, resolving them only within
    ///     the verified targets.
    /// </summary>
    Task ApplyCleanupAsync(
        ContentIntegrityReport report,
        IReadOnlyList<ContentIntegrityTarget> targets,
        CancellationToken cancellationToken);
}
