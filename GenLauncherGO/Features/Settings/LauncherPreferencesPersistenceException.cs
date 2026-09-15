using System;

namespace GenLauncherGO.Features.Settings;

internal sealed class LauncherPreferencesPersistenceException : Exception
{
    public LauncherPreferencesPersistenceException(Exception innerException)
        : base("Launcher preferences could not be persisted.", innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
    }
}
