using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RoslynMcp.Tests;

/// <summary>
/// Reads the 8.3 short form of an existing path with <c>GetShortPathNameW</c>. A volume with 8.3
/// name generation turned off, or a component that is already 8.3-compliant, keeps its long name,
/// so the result can equal the input.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsShortPathName
{
    public static string Get(string path)
    {
        var buffer = new char[1024];
        while (true)
        {
            // Returns the length without the terminator on success, or the capacity the result
            // needs (terminator included) when the buffer is too small.
            var length = GetShortPathNameW(path, buffer, (uint)buffer.Length);
            if (length == 0)
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    $"GetShortPathNameW could not read the short form of '{path}'.");
            }

            if (length < buffer.Length)
            {
                return new string(buffer, 0, checked((int)length));
            }

            buffer = new char[length];
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathNameW(string longPath, [Out] char[] shortPath, uint bufferLength);
}
