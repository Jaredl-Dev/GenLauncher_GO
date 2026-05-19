namespace GenLauncherGO.Infrastructure.Launching.Support;

/// <summary>
/// Creates hard links between installed package files and game-directory targets.
/// </summary>
internal interface IHardLinkCreator
{
    /// <summary>
    /// Attempts to create a hard link.
    /// </summary>
    bool TryCreateHardLink(string targetPath, string sourcePath);
}
