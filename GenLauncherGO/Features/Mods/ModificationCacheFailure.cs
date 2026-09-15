using System;
using System.IO;

namespace GenLauncherGO.Features.Mods;

/// <summary>
///     Classifies expected filesystem and path failures handled by modification image and palette cache boundaries.
/// </summary>
internal static class ModificationCacheFailure
{
    public static bool IsRecoverable(Exception exception)
    {
        return exception is InvalidDataException or IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException;
    }
}
