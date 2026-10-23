using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Reads the current name of an already opened object; grants no read authority.</summary>
internal static class PhysicalHandlePath
{
    internal static string Get(SafeFileHandle handle, bool allowUnc)
    {
        if (OperatingSystem.IsWindows())
        {
            if (PhysicalPathResolver.TryGetCanonicalWindowsHandlePath(handle, allowUnc, out var path)) return path;
            throw new IOException("The opened physical input's canonical path is unavailable.");
        }
        return GetUnix(handle);
    }

    internal static string GetUnix(SafeFileHandle handle)
    {
        if (OperatingSystem.IsLinux())
        {
            var fdPath = "/proc/self/fd/" + handle.DangerousGetHandle().ToInt32();
            return Path.GetFullPath(new FileInfo(fdPath).LinkTarget
                ?? throw new IOException("The opened physical input's canonical path is unavailable."));
        }
        if (OperatingSystem.IsMacOS())
        {
            const int fGetPath = 50;
            var buffer = new byte[1024];
            if (FcntlGetPath(handle.DangerousGetHandle().ToInt32(), fGetPath, buffer) != 0)
                throw new IOException("The opened physical input's canonical path is unavailable.");
            var end = Array.IndexOf(buffer, (byte)0);
            return Path.GetFullPath(Encoding.UTF8.GetString(buffer, 0, end < 0 ? buffer.Length : end));
        }
        throw new PlatformNotSupportedException("Physical input handle validation requires Windows, Linux or macOS.");
    }

    [DllImport("libc", EntryPoint = "fcntl", SetLastError = true, ExactSpelling = true)]
    private static extern int FcntlGetPath(int fd, int command, byte[] path);
}
