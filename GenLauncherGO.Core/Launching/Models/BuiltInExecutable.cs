using System;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Core.Launching.Models;

/// <summary>
///     Describes one built-in game or World Builder executable discovered in the active game directory.
/// </summary>
public sealed class BuiltInExecutable
{
    public BuiltInExecutable(string executableName, bool isAvailable)
    {
        ExecutableName = LauncherFileSystemLayout.NormalizeExecutableFileName(executableName);
        Kind = ResolveKind(ExecutableName);
        IsAvailable = isAvailable;
    }

    public string ExecutableName { get; }

    public BuiltInExecutableKind Kind { get; }

    public bool IsAvailable { get; }

    private static BuiltInExecutableKind ResolveKind(string executableName)
    {
        if (string.Equals(
                executableName,
                LauncherFileSystemLayout.GeneralsOnlineExecutableFileName,
                StringComparison.OrdinalIgnoreCase))
        {
            return BuiltInExecutableKind.GeneralsOnline;
        }

        return string.Equals(
                   executableName,
                   LauncherFileSystemLayout.RetailGameExecutableFileName,
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   executableName,
                   LauncherFileSystemLayout.RetailWorldBuilderExecutableFileName,
                   StringComparison.OrdinalIgnoreCase)
            ? BuiltInExecutableKind.Retail
            : BuiltInExecutableKind.Community;
    }
}
