using System;
using System.IO;

namespace GenLauncherGO.Infrastructure.Archives;

/// <summary>
/// Defines archive formats accepted consistently by legacy manual import and managed package updates.
/// </summary>
internal static class ArchiveFileSupport
{
    public static bool IsSupported(string filePath)
    {
        string extension = Path.GetExtension(filePath);
        return string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".rar", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".7z", StringComparison.OrdinalIgnoreCase);
    }
}
