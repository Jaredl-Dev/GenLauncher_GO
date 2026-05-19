using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Core.Launching.Models;

public sealed class GameClientExecutable
{
    public GameClientExecutable(
        string executableName,
        GameClientExecutableKind kind,
        bool isAvailable)
    {
        ExecutableName = LauncherFileSystemLayout.NormalizeExecutableFileName(executableName);
        Kind = kind;
        IsAvailable = isAvailable;
    }

    public string ExecutableName { get; }

    public GameClientExecutableKind Kind { get; }

    public bool IsAvailable { get; }
}
