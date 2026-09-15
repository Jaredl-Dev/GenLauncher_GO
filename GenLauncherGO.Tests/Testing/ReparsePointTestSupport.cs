using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Creates directory junctions for the reparse-point safety tests.
///     <para>
///         Production rejects unsafe paths by testing <see cref="FileAttributes.ReparsePoint" /> and never inspects the
///         reparse tag, so a junction exercises the same checks as a symbolic link. Junctions need no elevation and no
///         Windows Developer Mode, which keeps these safety tests running for every contributor instead of skipping on
///         accounts that cannot create symbolic links.
///     </para>
/// </summary>
internal static class ReparsePointTestSupport
{
    private const uint IoReparseTagMountPoint = 0xA0000003;
    private const uint FsctlSetReparsePoint = 0x000900A4;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;

    /// <summary>
    ///     Creates a directory junction at <paramref name="junctionPath" /> that resolves to
    ///     <paramref name="targetPath" />.
    /// </summary>
    public static void CreateDirectoryJunction(string junctionPath, string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(junctionPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        string fullTargetPath = Path.GetFullPath(targetPath);
        Directory.CreateDirectory(fullTargetPath);
        Directory.CreateDirectory(junctionPath);

        byte[] reparseData = BuildMountPointReparseData(fullTargetPath);

        using SafeFileHandle handle = CreateFile(
            junctionPath,
            GenericWrite,
            0,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Could not open '{junctionPath}' to write its junction data.");
        }

        if (!DeviceIoControl(
                handle,
                FsctlSetReparsePoint,
                reparseData,
                reparseData.Length,
                IntPtr.Zero,
                0,
                out int _,
                IntPtr.Zero))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Could not set the junction reparse point on '{junctionPath}'.");
        }
    }

    /// <summary>
    ///     Creates a junction at <paramref name="junctionPath" /> pointing at a directory outside the launcher-owned
    ///     tree that holds one canary file, which is the arrangement every containment test needs.
    /// </summary>
    /// <remarks>
    ///     The canary is what proves a rejected operation stopped before it followed the link: asserting only that the
    ///     junction survived cannot tell a refusal apart from a delete that happened to leave the link behind.
    /// </remarks>
    public static ProtectedJunction CreateJunctionToProtectedTarget(
        TestDirectory directory,
        string junctionPath,
        string targetRelativePath = "ExternalTarget",
        string canaryFileName = "target.txt")
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(junctionPath);

        const string CanaryContents = "target";

        string targetDirectory = directory.CreateDirectory(targetRelativePath);
        string canaryFilePath = directory.CreateFile(
            Path.Combine(targetRelativePath, canaryFileName),
            CanaryContents);
        CreateDirectoryJunction(junctionPath, targetDirectory);

        return new ProtectedJunction(junctionPath, targetDirectory, canaryFilePath, CanaryContents);
    }

    /// <summary>
    ///     Builds a REPARSE_DATA_BUFFER describing a mount point. The substitute name uses the NT object-manager prefix
    ///     that the mount-point format requires; the print name is the display path.
    /// </summary>
    private static byte[] BuildMountPointReparseData(string fullTargetPath)
    {
        const int ReparseHeaderLength = 8;
        const int MountPointHeaderLength = 8;
        const int TerminatorLength = 2;

        byte[] substituteName = Encoding.Unicode.GetBytes($@"\??\{fullTargetPath}");
        byte[] printName = Encoding.Unicode.GetBytes(fullTargetPath);

        int pathBufferLength = substituteName.Length + TerminatorLength + printName.Length + TerminatorLength;
        byte[] buffer = new byte[ReparseHeaderLength + MountPointHeaderLength + pathBufferLength];

        BitConverter.GetBytes(IoReparseTagMountPoint).CopyTo(buffer, 0);
        BitConverter.GetBytes((ushort)(MountPointHeaderLength + pathBufferLength)).CopyTo(buffer, 4);
        BitConverter.GetBytes((ushort)0).CopyTo(buffer, 6);
        BitConverter.GetBytes((ushort)0).CopyTo(buffer, 8);
        BitConverter.GetBytes((ushort)substituteName.Length).CopyTo(buffer, 10);
        BitConverter.GetBytes((ushort)(substituteName.Length + TerminatorLength)).CopyTo(buffer, 12);
        BitConverter.GetBytes((ushort)printName.Length).CopyTo(buffer, 14);

        substituteName.CopyTo(buffer, ReparseHeaderLength + MountPointHeaderLength);
        printName.CopyTo(
            buffer,
            ReparseHeaderLength + MountPointHeaderLength + substituteName.Length + TerminatorLength);

        return buffer;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        byte[] lpInBuffer,
        int nInBufferSize,
        IntPtr lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);
}

/// <summary>
///     A junction and the outside directory it resolves to, with the canary that proves the target was untouched.
/// </summary>
internal sealed record ProtectedJunction(
    string JunctionPath,
    string TargetDirectory,
    string CanaryFilePath,
    string CanaryContents)
{
    /// <summary>
    ///     Reads the canary as it stands now, which must still equal <see cref="CanaryContents" />.
    /// </summary>
    public string ReadCanary()
    {
        return File.ReadAllText(CanaryFilePath);
    }
}
