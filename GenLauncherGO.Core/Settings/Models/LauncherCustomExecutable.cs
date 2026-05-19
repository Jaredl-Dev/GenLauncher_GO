using System;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Core.Settings.Models;

public sealed record LauncherCustomExecutable
{
    public LauncherCustomExecutable(string displayName, string executableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        DisplayName = displayName.Trim();
        ExecutableName = LauncherFileSystemLayout.NormalizeExecutableFileName(executableName);
    }

    public string DisplayName { get; }

    public string ExecutableName { get; }
}
