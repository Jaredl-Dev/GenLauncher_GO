using System;

namespace GenLauncherGO.Core.Settings.Exceptions;

public sealed class LauncherPreferencesPersistenceException : Exception
{
    public LauncherPreferencesPersistenceException(Exception innerException)
        : base("Launcher preferences could not be persisted.", innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
    }
}
