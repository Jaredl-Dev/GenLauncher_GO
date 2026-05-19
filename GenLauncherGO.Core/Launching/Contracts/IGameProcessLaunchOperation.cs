using System;
using System.Threading.Tasks;

namespace GenLauncherGO.Core.Launching.Contracts;

/// <summary>
///     Represents a launched game or tool process family that can be observed and force closed.
/// </summary>
public interface IGameProcessLaunchOperation
{
    /// <summary>
    ///     Gets the executable name for the currently running tracked process.
    /// </summary>
    string CurrentExecutableName { get; }

    /// <summary>
    ///     Gets the task that completes when every tracked process in the launched process family has exited.
    /// </summary>
    Task<bool> Completion { get; }

    /// <summary>
    ///     Occurs when <see cref="CurrentExecutableName" /> changes.
    /// </summary>
    event EventHandler? CurrentExecutableNameChanged;

    /// <summary>
    ///     Force closes the tracked process family.
    /// </summary>
    void ForceClose();
}
