using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GenLauncherGO.Infrastructure.Common;

/// <summary>
/// Identifies an existing directory independently of aliases in its path spelling.
/// </summary>
internal readonly record struct PhysicalDirectoryIdentity(uint VolumeSerialNumber, ulong FileIndex);

/// <summary>
/// Resolves existing Windows directories through handles for security-sensitive comparisons and recovery metadata.
/// </summary>
internal static class PhysicalDirectoryPath
{
    private const uint FileReadAttributes = 0x0080;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileNameNormalized = 0x0;
    private const uint VolumeNameDos = 0x0;
    private const string ExtendedPathPrefix = @"\\?\";
    private const string ExtendedUncPrefix = @"\\?\UNC\";

    /// <summary>
    /// Returns the canonical path observed through a handle to an existing directory.
    /// </summary>
    public static string ResolveExisting(string path)
    {
        using SafeFileHandle handle = OpenDirectory(path);
        var buffer = new StringBuilder(512);
        uint requiredLength = GetFinalPathNameByHandle(
            handle,
            buffer,
            (uint)buffer.Capacity,
            FileNameNormalized | VolumeNameDos);
        if (requiredLength == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (requiredLength >= buffer.Capacity)
        {
            buffer.EnsureCapacity(checked((int)requiredLength + 1));
            requiredLength = GetFinalPathNameByHandle(
                handle,
                buffer,
                (uint)buffer.Capacity,
                FileNameNormalized | VolumeNameDos);
            if (requiredLength == 0 || requiredLength >= buffer.Capacity)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        return NormalizeHandlePath(buffer.ToString());
    }

    /// <summary>
    /// Returns the stable volume and file-index identity of an existing directory.
    /// </summary>
    public static PhysicalDirectoryIdentity GetIdentity(string path)
    {
        using SafeFileHandle handle = OpenDirectory(path);
        if (!GetFileInformationByHandle(handle, out ByHandleFileInformation information))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        ulong fileIndex = ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow;
        return new PhysicalDirectoryIdentity(information.VolumeSerialNumber, fileIndex);
    }

    private static SafeFileHandle OpenDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException("The directory does not exist.");
        }

        SafeFileHandle handle = CreateFile(
            fullPath,
            FileReadAttributes,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }

        return handle;
    }

    private static string NormalizeHandlePath(string path)
    {
        string normalizedPath;
        if (path.StartsWith(ExtendedUncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = @"\\" + path[ExtendedUncPrefix.Length..];
        }
        else if (path.StartsWith(ExtendedPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = path[ExtendedPathPrefix.Length..];
        }
        else
        {
            normalizedPath = path;
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(normalizedPath));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        StringBuilder filePath,
        uint filePathLength,
        uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation fileInformation);
}
