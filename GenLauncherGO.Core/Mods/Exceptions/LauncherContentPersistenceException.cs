using System;

namespace GenLauncherGO.Core.Mods.Exceptions;

public sealed class LauncherContentPersistenceException : Exception
{
    public LauncherContentPersistenceException(Exception innerException)
        : base("Launcher content state could not be persisted.", innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
    }
}
