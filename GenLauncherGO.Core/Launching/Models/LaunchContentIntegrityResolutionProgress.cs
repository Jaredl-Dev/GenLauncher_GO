using System;
using GenLauncherGO.Core.Updating.Models;

namespace GenLauncherGO.Core.Launching.Models;

/// <summary>
/// Reports package progress or completion for one launch-integrity resolution target.
/// </summary>
public sealed record LaunchContentIntegrityResolutionProgress
{
    private LaunchContentIntegrityResolutionProgress(
        string targetId,
        PackageUpdateProgress? packageProgress,
        bool completed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);

        TargetId = targetId;
        PackageProgress = packageProgress;
        Completed = completed;
    }

    public string TargetId { get; }

    public PackageUpdateProgress? PackageProgress { get; }

    public bool Completed { get; }

    public static LaunchContentIntegrityResolutionProgress Package(
        string targetId,
        PackageUpdateProgress packageProgress)
    {
        ArgumentNullException.ThrowIfNull(packageProgress);

        return new LaunchContentIntegrityResolutionProgress(targetId, packageProgress, completed: false);
    }

    public static LaunchContentIntegrityResolutionProgress Complete(string targetId)
    {
        return new LaunchContentIntegrityResolutionProgress(targetId, packageProgress: null, completed: true);
    }
}
