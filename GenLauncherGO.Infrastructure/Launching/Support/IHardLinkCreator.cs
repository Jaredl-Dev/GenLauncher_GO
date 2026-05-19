namespace GenLauncherGO.Infrastructure.Launching.Support;

/// <summary>
///     Queries hard-link eligibility and creates hard links between installed package files and game-directory targets.
/// </summary>
internal interface IHardLinkCreator
{
    /// <summary>
    ///     Determines whether two paths reside on the same physical volume and can use atomic moves or hard links.
    /// </summary>
    bool ArePathsOnSameVolume(string firstPath, string secondPath);

    /// <summary>
    ///     Attempts to create a hard link.
    /// </summary>
    bool TryCreateHardLink(string targetPath, string sourcePath);
}
