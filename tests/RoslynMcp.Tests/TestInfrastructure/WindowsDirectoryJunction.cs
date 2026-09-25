using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace RoslynMcp.Tests;

/// <summary>
/// Creates an NTFS directory junction in-process by writing a mount-point reparse buffer with
/// <c>FSCTL_SET_REPARSE_POINT</c>. Junctions need no symlink privilege, which is why fixtures fall
/// back to them; doing it in-process replaces a <c>cmd /c mklink /J</c> launch whose start-up
/// latency under full-suite load exceeded its wait bound and silently skipped link-boundary tests.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsDirectoryJunction
{
    private const uint IoReparseTagMountPoint = 0xA0000003;
    private const uint FsctlSetReparsePoint = 0x000900A4;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;

    // ReparseTag (4) + ReparseDataLength (2) + Reserved (2).
    private const int ReparseHeaderSize = 8;

    // SubstituteNameOffset/Length + PrintNameOffset/Length, 2 bytes each.
    private const int MountPointHeaderSize = 8;

    /// <summary>
    /// Creates <paramref name="linkPath"/> as a junction to <paramref name="targetPath"/>.
    /// <paramref name="linkPath"/> must not exist. Throws <see cref="IOException"/> naming both
    /// paths on failure, after removing the link directory this call created.
    /// </summary>
    public static void Create(string linkPath, string targetPath)
    {
        if (Path.Exists(linkPath))
        {
            throw new IOException($"Cannot create junction '{linkPath}': the path already exists.");
        }

        var fullTarget = Path.GetFullPath(targetPath);
        var substituteName = Encoding.Unicode.GetBytes(@"\??\" + fullTarget);
        var printName = Encoding.Unicode.GetBytes(fullTarget);
        var reparseDataLength = MountPointHeaderSize + substituteName.Length + 2 + printName.Length + 2;
        var buffer = new byte[ReparseHeaderSize + reparseDataLength];

        BitConverter.TryWriteBytes(buffer.AsSpan(0), IoReparseTagMountPoint);
        BitConverter.TryWriteBytes(buffer.AsSpan(4), checked((ushort)reparseDataLength));
        BitConverter.TryWriteBytes(buffer.AsSpan(8), (ushort)0);
        BitConverter.TryWriteBytes(buffer.AsSpan(10), checked((ushort)substituteName.Length));
        BitConverter.TryWriteBytes(buffer.AsSpan(12), checked((ushort)(substituteName.Length + 2)));
        BitConverter.TryWriteBytes(buffer.AsSpan(14), checked((ushort)printName.Length));
        var pathBuffer = ReparseHeaderSize + MountPointHeaderSize;
        substituteName.CopyTo(buffer, pathBuffer);
        printName.CopyTo(buffer, pathBuffer + substituteName.Length + 2);

        Directory.CreateDirectory(linkPath);
        try
        {
            using var handle = CreateFileW(
                linkPath,
                GenericWrite,
                0,
                IntPtr.Zero,
                OpenExisting,
                FileFlagBackupSemantics | FileFlagOpenReparsePoint,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                throw Failure("open the link directory", linkPath, fullTarget);
            }

            if (!DeviceIoControl(
                    handle,
                    FsctlSetReparsePoint,
                    buffer,
                    buffer.Length,
                    IntPtr.Zero,
                    0,
                    out _,
                    IntPtr.Zero))
            {
                throw Failure("write the mount-point reparse data", linkPath, fullTarget);
            }
        }
        catch
        {
            Directory.Delete(linkPath);
            throw;
        }
    }

    private static IOException Failure(string step, string linkPath, string targetPath)
    {
        var error = new Win32Exception(Marshal.GetLastPInvokeError());
        return new IOException(
            $"Could not {step} for junction '{linkPath}' -> '{targetPath}': {error.Message}",
            error);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        byte[] inBuffer,
        int inBufferSize,
        IntPtr outBuffer,
        int outBufferSize,
        out int bytesReturned,
        IntPtr overlapped);
}
