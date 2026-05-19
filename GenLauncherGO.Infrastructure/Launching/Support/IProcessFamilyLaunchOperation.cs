using System;
using System.Threading.Tasks;

namespace GenLauncherGO.Infrastructure.Launching.Support;

/// <summary>
///     Represents a launched Windows process family that can be observed and force closed.
/// </summary>
internal interface IProcessFamilyLaunchOperation
{
    /// <summary>
    ///     Gets the executable name for the currently running tracked process.
    /// </summary>
    string CurrentExecutableName { get; }

    /// <summary>
    ///     Gets the task that completes when every tracked process in the launched process family has exited.
    /// </summary>
    Task<TimeSpan> Completion { get; }

    /// <summary>
    ///     Occurs when <see cref="CurrentExecutableName" /> changes.
    /// </summary>
    event EventHandler? CurrentExecutableNameChanged;

    /// <summary>
    ///     Force closes all currently tracked running processes in the launched process family.
    /// </summary>
    void ForceClose();
}
