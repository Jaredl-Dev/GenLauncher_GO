using System;
using GenLauncherGO.Core.Startup.Models;

namespace GenLauncherGO.Core.Startup.Contracts;

/// <summary>
/// Provides host-process and operating-system operations needed by launcher startup.
/// </summary>
public interface ILauncherHostEnvironmentService
{
    /// <summary>
    /// Brings the first visible window for the current process name to the foreground when possible.
    /// </summary>
    void ActivateCurrentProcessWindow();

    /// <summary>
    /// Gets the directory containing the running launcher executable.
    /// </summary>
    string GetExecutableDirectory();

    /// <summary>
    /// Returns whether the current process is running with elevated administrator privileges.
    /// </summary>
    bool IsCurrentProcessElevated();

    /// <summary>
    /// Returns whether a directory is under a protected Program Files location.
    /// </summary>
    bool IsProtectedProgramFilesDirectory(string directory);

    /// <summary>
    /// Attempts to start a replacement instance of the current launcher process.
    /// </summary>
    LauncherRestartResult TryRestartCurrentProcess();

    /// <summary>
    /// Attempts to acquire the launcher single-instance guard; the returned guard reports whether startup may continue.
    /// </summary>
    ILauncherSingleInstanceGuard TryAcquireSingleInstance(string instanceName, TimeSpan retryDelay);
}
