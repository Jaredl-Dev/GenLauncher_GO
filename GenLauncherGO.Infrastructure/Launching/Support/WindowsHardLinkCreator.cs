using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Infrastructure.Launching.Support;

/// <summary>
/// Creates hard links through the Windows file-system API.
/// </summary>
internal sealed class WindowsHardLinkCreator : IHardLinkCreator
{
    private readonly ILogger<WindowsHardLinkCreator> _logger;

    public WindowsHardLinkCreator(ILogger<WindowsHardLinkCreator>? logger = null)
    {
        _logger = logger ?? NullLogger<WindowsHardLinkCreator>.Instance;
    }

    public bool TryCreateHardLink(string targetPath, string sourcePath)
    {
        bool created = CreateHardLink(targetPath, sourcePath, lpSecurityAttributes: 0);
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

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateHardLink(
        string lpFileName,
        string lpExistingFileName,
        int lpSecurityAttributes);
}
