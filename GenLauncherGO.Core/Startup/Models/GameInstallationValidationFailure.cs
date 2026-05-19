namespace GenLauncherGO.Core.Startup.Models;

/// <summary>
///     Identifies why a selected game installation cannot be used.
/// </summary>
public enum GameInstallationValidationFailure
{
    /// <summary>
    ///     The installation is valid.
    /// </summary>
    None = 0,

    /// <summary>
    ///     No installation directory was supplied.
    /// </summary>
    PathMissing = 1,

    /// <summary>
    ///     The supplied directory does not exist.
    /// </summary>
    DirectoryNotFound = 2,

    /// <summary>
    ///     The launcher executable directory is the game installation or one of its descendants.
    /// </summary>
    LauncherLocationOverlapsGame = 3,

    /// <summary>
    ///     The path traverses a reparse point and is unsafe for launcher mutations.
    /// </summary>
    UnsafeFileSystemPath = 4,

    /// <summary>
    ///     Windows could not safely resolve or inspect the directory.
    /// </summary>
    PathUnavailable = 5,

    /// <summary>
    ///     The directory does not contain a safe built-in executable for the selected game.
    /// </summary>
    BuiltInExecutableNotFound = 6
}
