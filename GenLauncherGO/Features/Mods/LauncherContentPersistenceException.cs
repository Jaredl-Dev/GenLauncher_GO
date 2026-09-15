using System;

namespace GenLauncherGO.Features.Mods;

internal sealed class LauncherContentPersistenceException : Exception
{
    public LauncherContentPersistenceException(Exception innerException)
        : base("Launcher content state could not be persisted.", innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
    }
}
