namespace GenLauncherGO.Core.Shell.Contracts;

/// <summary>
/// Opens launcher-related external targets through the operating system shell.
/// </summary>
public interface ILauncherShellService
{
    /// <summary>
    /// Opens an absolute URI with the operating system shell.
    /// </summary>
    void OpenUri(string uri);

    /// <summary>
    /// Opens a folder with the operating system shell.
    /// </summary>
    void OpenFolder(
        string folderPath,
        bool requireFiles = false,
        bool createIfMissing = false);
}
