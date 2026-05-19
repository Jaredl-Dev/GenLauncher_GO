using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GenLauncherGO.Infrastructure.Common;

/// <summary>
///     Identifies an existing file-system object independently of aliases in its path spelling.
/// </summary>
internal readonly record struct PhysicalFileSystemIdentity(uint VolumeSerialNumber, ulong FileIndex);

/// <summary>
///     Resolves existing Windows directories through handles for security-sensitive comparisons and recovery metadata.
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
    ///     Returns the canonical path observed through a handle to an existing directory.
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
    ///     Returns the stable volume and file-index identity of an existing directory.
    /// </summary>
    public static PhysicalFileSystemIdentity GetIdentity(string path)
    {
        using SafeFileHandle handle = OpenDirectory(path);
        return GetIdentity(handle);
    }

    /// <summary>
    ///     Returns the stable volume and file-index identity of an existing file.
    /// </summary>
    public static PhysicalFileSystemIdentity GetFileIdentity(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The file does not exist.", fullPath);
        }

        using SafeFileHandle handle = OpenExistingPathHandle(fullPath, 0);
        return GetIdentity(handle);
    }

    private static PhysicalFileSystemIdentity GetIdentity(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out ByHandleFileInformation information))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        ulong fileIndex = ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow;
        return new PhysicalFileSystemIdentity(information.VolumeSerialNumber, fileIndex);
    }

    private static SafeFileHandle OpenDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException("The directory does not exist.");
        }

        return OpenExistingPathHandle(fullPath, FileFlagBackupSemantics);
    }

    private static SafeFileHandle OpenExistingPathHandle(string fullPath, uint flagsAndAttributes)
    {
        SafeFileHandle handle = CreateFile(
            ToExtendedLengthPath(fullPath),
            FileReadAttributes,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            flagsAndAttributes,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }

        return handle;
    }

    /// <summary>
    ///     Returns the absolute Win32 extended-length form of a local or UNC path.
    /// </summary>
    public static string ToExtendedLengthPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(ExtendedPathPrefix, StringComparison.Ordinal))
        {
            return fullPath;
        }

        return fullPath.StartsWith(@"\\", StringComparison.Ordinal)
            ? ExtendedUncPrefix + fullPath[2..]
            : ExtendedPathPrefix + fullPath;
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

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true,
        CharSet = CharSet.Unicode)]
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

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public FILETIME CreationTime;
        public FILETIME LastAccessTime;
        public FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
