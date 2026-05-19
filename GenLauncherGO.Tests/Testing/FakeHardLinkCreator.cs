using System;
using System.Collections.Generic;
using System.Threading;
using GenLauncherGO.Infrastructure.Launching.Support;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Stands in for the Windows hard-link creator so a test can decide whether linking succeeds, what happens at the
///     staging path while a link is being made, and when the deployment is cancelled.
/// </summary>
internal sealed class FakeHardLinkCreator : IHardLinkCreator
{
    private readonly WindowsHardLinkCreator _windowsHardLinkCreator = new();

    /// <summary>
    ///     Whether a link attempt is allowed to succeed at all.
    /// </summary>
    public bool CanCreateHardLinks { get; init; } = true;

    /// <summary>
    ///     Whether an allowed attempt creates a real hard link, so the deployment's own identity checks run.
    /// </summary>
    public bool UseRealHardLinks { get; init; } = true;

    public bool PathsOnSameVolume { get; init; } = true;

    /// <summary>
    ///     Observes a volume comparison, which is where a test can mutate the paths under the caller.
    /// </summary>
    public Action<string, string>? SameVolumeCheck { get; init; }

    /// <summary>
    ///     Observes a link attempt before it is made, with the target and source paths.
    /// </summary>
    public Action<string, string>? CreateHook { get; init; }

    /// <summary>
    ///     Cancelled after each link attempt, for the deployment cancellation paths.
    /// </summary>
    public CancellationTokenSource? CancelOn { get; init; }

    public List<(string TargetPath, string SourcePath)> CreatedLinks { get; } = [];

    public bool ArePathsOnSameVolume(string firstPath, string secondPath)
    {
        SameVolumeCheck?.Invoke(firstPath, secondPath);
        return PathsOnSameVolume;
    }

    public bool TryCreateHardLink(string targetPath, string sourcePath)
    {
        CreateHook?.Invoke(targetPath, sourcePath);
        bool created = CanCreateHardLinks &&
                       (!UseRealHardLinks || _windowsHardLinkCreator.TryCreateHardLink(targetPath, sourcePath));
        if (created)
        {
            CreatedLinks.Add((targetPath, sourcePath));
        }

        CancelOn?.Cancel();
        return created;
    }
}
