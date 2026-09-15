using System;
using GenLauncherGO.Features.Startup;

namespace GenLauncherGO.Features.Settings;

internal sealed record LauncherCustomExecutable
{
    public LauncherCustomExecutable(string displayName, string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        DisplayName = displayName.Trim();
        ExecutablePath = LauncherFileSystemLayout.NormalizeExecutablePath(executablePath);
    }

    public string DisplayName { get; }

    public string ExecutablePath { get; }
}
