using System.Runtime.InteropServices;

namespace RoslynMcp.Tests.Helpers;

internal static class PhysicalFileLinks
{
    internal static void CreateHardLink(string name, string existing)
    {
        var succeeded = OperatingSystem.IsWindows() ? CreateHardLinkWindows(name, existing, IntPtr.Zero) : LinkUnix(existing, name) == 0;
        if (!succeeded) throw new IOException("The actual hardlink fixture could not be created (native error " + Marshal.GetLastPInvokeError() + ").");
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkWindows(string name, string existing, IntPtr securityAttributes);

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int LinkUnix([MarshalAs(UnmanagedType.LPUTF8Str)] string existing, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
}
