using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RoslynMcp.Tests;

/// <summary>
/// Starts a process with <c>CREATE_SUSPENDED</c>. The image is mapped and the process is listed
/// and killable, but its primary thread has not yet run the loader, so the process has no loader
/// module list. Every freshly started process is in this state briefly after <c>CreateProcess</c>
/// returns. Suspending the process keeps it there, so a test can hit that state
/// deterministically instead of racing for it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsSuspendedProcess : IDisposable
{
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateNoWindow = 0x08000000;

    private readonly nint _processHandle;
    private readonly nint _threadHandle;
    private bool _disposed;

    private WindowsSuspendedProcess(Process process, nint processHandle, nint threadHandle)
    {
        Process = process;
        _processHandle = processHandle;
        _threadHandle = threadHandle;
    }

    /// <summary>The suspended process, opened by id.</summary>
    public Process Process { get; }

    /// <summary>
    /// Creates <paramref name="executablePath"/> suspended. Arguments must not contain spaces or
    /// quotes. The caller disposes the result, which terminates the process if it is still alive.
    /// </summary>
    public static WindowsSuspendedProcess Start(string executablePath, IEnumerable<string> arguments)
    {
        var commandLine = ("\"" + executablePath + "\" " + string.Join(' ', arguments) + "\0").ToCharArray();
        var startupInfo = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
        if (!CreateProcessW(
                executablePath,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                inheritHandles: false,
                CreateSuspended | CreateNoWindow,
                IntPtr.Zero,
                currentDirectory: null,
                ref startupInfo,
                out var processInformation))
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                $"CreateProcessW failed to start '{executablePath}' suspended.");
        }

        try
        {
            return new WindowsSuspendedProcess(
                Process.GetProcessById(processInformation.ProcessId),
                processInformation.ProcessHandle,
                processInformation.ThreadHandle);
        }
        catch
        {
            TerminateProcess(processInformation.ProcessHandle, 1);
            CloseHandle(processInformation.ThreadHandle);
            CloseHandle(processInformation.ProcessHandle);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        // Best effort: a test that expected the drain to kill the process has already asserted
        // on it; this only stops a process the drain failed to kill from outliving the test.
        TerminateProcess(_processHandle, 1);
        Process.WaitForExit(5_000);
        Process.Dispose();
        CloseHandle(_threadHandle);
        CloseHandle(_processHandle);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Size;
        public nint Reserved2;
        public nint StdInput;
        public nint StdOutput;
        public nint StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint ProcessHandle;
        public nint ThreadHandle;
        public int ProcessId;
        public int ThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(
        string applicationName,
        char[] commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(nint process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
