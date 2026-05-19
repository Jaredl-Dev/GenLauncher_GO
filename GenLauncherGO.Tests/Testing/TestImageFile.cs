using System;
using System.IO;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Supplies the smallest real image the launcher's bitmap loading accepts.
/// </summary>
internal static class TestImageFile
{
    private const string OnePixelPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    /// <summary>
    ///     A one-pixel PNG, so a test asserting on decoded pixel size has a known answer.
    /// </summary>
    private static byte[] OnePixelPng { get; } = Convert.FromBase64String(OnePixelPngBase64);

    public static string Write(TestDirectory directory, string fileName)
    {
        ArgumentNullException.ThrowIfNull(directory);

        string filePath = directory.GetPath(fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllBytes(filePath, OnePixelPng);
        return filePath;
    }
}
