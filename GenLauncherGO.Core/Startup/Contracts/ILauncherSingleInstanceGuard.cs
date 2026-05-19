using System;

namespace GenLauncherGO.Core.Startup.Contracts;

/// <summary>
/// Represents ownership of the launcher single-instance guard.
/// </summary>
public interface ILauncherSingleInstanceGuard : IDisposable
{
    /// <summary>
    /// Gets a value indicating whether the guard was acquired by the current process.
    /// </summary>
    bool IsAcquired { get; }
}
