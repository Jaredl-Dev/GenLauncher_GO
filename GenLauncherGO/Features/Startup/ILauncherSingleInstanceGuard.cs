using System;

namespace GenLauncherGO.Features.Startup;

/// <summary>
///     Represents ownership of the launcher single-instance guard.
/// </summary>
internal interface ILauncherSingleInstanceGuard : IDisposable
{
    /// <summary>
    ///     Gets a value indicating whether the guard was acquired by the current process.
    /// </summary>
    bool IsAcquired { get; }
}
