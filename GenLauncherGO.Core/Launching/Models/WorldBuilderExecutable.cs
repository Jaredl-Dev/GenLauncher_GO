using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Core.Launching.Models;

public sealed class WorldBuilderExecutable
{
    public WorldBuilderExecutable(
        string executableName,
        WorldBuilderExecutableKind kind,
        bool isAvailable)
    {
        ExecutableName = LauncherFileSystemLayout.NormalizeExecutableFileName(executableName);
        Kind = kind;
        IsAvailable = isAvailable;
    }

    public string ExecutableName { get; }

    public WorldBuilderExecutableKind Kind { get; }

    public bool IsAvailable { get; }
}
