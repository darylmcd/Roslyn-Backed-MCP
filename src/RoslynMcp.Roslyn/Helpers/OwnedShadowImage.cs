using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Transfers an already-owned file object to read-only access without reopening its directory entry.</summary>
internal static class OwnedShadowImage
{
    [SupportedOSPlatform("windows")]
    internal static FileStream OpenReadViewWindows(SafeFileHandle original, FileShare share)
    {
        const uint genericRead = 0x80000000;
        const uint overlapped = 0x40000000;
        const uint openReparsePoint = 0x00200000;
        var handle = ReOpenFile(original, genericRead, share, overlapped | openReparsePoint);
        if (handle.IsInvalid)
        {
            var code = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new IOException("An owned analyzer image could not be held safely (native error " + code + ").");
        }
        try { return new FileStream(handle, FileAccess.Read, 1, isAsync: true); }
        catch { handle.Dispose(); throw; }
    }

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle ReOpenFile(SafeFileHandle original, uint desiredAccess, FileShare shareMode, uint flags);
}
