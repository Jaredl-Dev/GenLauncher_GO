using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using GenLauncherGO.Infrastructure.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Infrastructure.Launching.Support;

/// <summary>
///     Queries Windows volume identity and creates hard links through the Windows file-system API.
/// </summary>
internal sealed class WindowsHardLinkCreator : IHardLinkCreator
{
    private readonly ILogger<WindowsHardLinkCreator> _logger;

    public WindowsHardLinkCreator(ILogger<WindowsHardLinkCreator>? logger = null)
    {
        _logger = logger ?? NullLogger<WindowsHardLinkCreator>.Instance;
    }

    public bool ArePathsOnSameVolume(string firstPath, string secondPath)
    {
        string firstDirectory = ResolveParentDirectory(firstPath);
        string secondDirectory = ResolveParentDirectory(secondPath);
        return PhysicalDirectoryPath.GetIdentity(firstDirectory).VolumeSerialNumber ==
               PhysicalDirectoryPath.GetIdentity(secondDirectory).VolumeSerialNumber;
    }

    public bool TryCreateHardLink(string targetPath, string sourcePath)
    {
        bool created = CreateHardLink(
            PhysicalDirectoryPath.ToExtendedLengthPath(targetPath),
            PhysicalDirectoryPath.ToExtendedLengthPath(sourcePath),
            0);
        if (!created)
        {
            int errorCode = Marshal.GetLastWin32Error();
            _logger.LogWarning(
                "Failed to create hard link {TargetFileName} from {SourceFileName}. Win32 error {ErrorCode}: {ErrorMessage}",
                Path.GetFileName(targetPath),
                Path.GetFileName(sourcePath),
                errorCode,
                new Win32Exception(errorCode).Message);
        }

        return created;
    }

    private static string ResolveParentDirectory(string path)
    {
        return Directory.Exists(path)
            ? path
            : Path.GetDirectoryName(path)
              ?? throw new InvalidOperationException("Deployment file paths must have a parent directory.");
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateHardLink(
        string lpFileName,
        string lpExistingFileName,
        int lpSecurityAttributes);
}
