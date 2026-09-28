using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace RoslynMcp.Host.Stdio.Runtime;

/// <summary>How a <see cref="ProcessExecutablePathResolver"/> lookup ended.</summary>
internal enum ProcessExecutablePathStatus
{
    /// <summary><see cref="ProcessExecutablePathResolution.Path"/> holds the executable image path.</summary>
    Resolved,

    /// <summary>The process no longer exists, so there is nothing left to inspect or terminate.</summary>
    Exited,

    /// <summary>The process exists but its executable path could not be read.</summary>
    Unavailable,
}

/// <summary>Result of resolving another process's executable image path.</summary>
/// <param name="Status">How the lookup ended.</param>
/// <param name="Path">The executable image path when <paramref name="Status"/> is
/// <see cref="ProcessExecutablePathStatus.Resolved"/>; otherwise <see langword="null"/>.</param>
/// <param name="Reason">A stable kebab-case failure code when <paramref name="Status"/> is
/// <see cref="ProcessExecutablePathStatus.Unavailable"/>; otherwise <see langword="null"/>.</param>
internal readonly record struct ProcessExecutablePathResolution(
    ProcessExecutablePathStatus Status,
    string? Path,
    string? Reason)
{
    public static ProcessExecutablePathResolution Exited { get; } =
        new(ProcessExecutablePathStatus.Exited, null, null);

    public static ProcessExecutablePathResolution Resolved(string path) =>
        new(ProcessExecutablePathStatus.Resolved, path, null);

    public static ProcessExecutablePathResolution Unavailable(string reason) =>
        new(ProcessExecutablePathStatus.Unavailable, null, reason);
}

/// <summary>
/// Resolves another process's executable image path from state the operating system records
/// when it creates the process, so the path is readable as soon as the process exists.
/// </summary>
/// <remarks>
/// <para><see cref="Process.MainModule"/> is not safe for a process that may have just started.
/// On Windows it walks the target's loader module list (<c>EnumProcessModules</c> over the PEB
/// loader data). The list does not exist until the new process's primary thread has run the
/// loader, so until then <c>MainModule</c> returns <see langword="null"/> or throws
/// <c>ERROR_PARTIAL_COPY</c>, which let a just-started <c>testhost</c> escape the
/// <c>workspace_close</c> drain. <c>QueryFullProcessImageNameW</c> reads the image name the
/// kernel stored at creation and needs only <c>PROCESS_QUERY_LIMITED_INFORMATION</c>.</para>
/// <para>On Linux, <c>/proc/&lt;pid&gt;/exe</c> is the kernel's link to the executed image,
/// which does not depend on the target's dynamic loader. Other platforms have no dedicated
/// query here and fall back to <see cref="Process.MainModule"/>.</para>
/// </remarks>
internal static class ProcessExecutablePathResolver
{
    /// <summary>The operating system refused to open the process for a path query.</summary>
    public const string AccessDeniedReason = "access-denied";

    /// <summary>The path query failed for a reason other than access or process exit.</summary>
    public const string PathQueryFailedReason = "path-query-failed";

    // Most image paths fit the first buffer; the second is the NT maximum path length.
    private static readonly int[] s_windowsPathCapacities = [1024, 32_768];

    public static ProcessExecutablePathResolution Resolve(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (OperatingSystem.IsWindows())
        {
            return ResolveWindows(process.Id);
        }

        if (OperatingSystem.IsLinux())
        {
            return ResolveLinux(process.Id);
        }

        return ResolveFromMainModule(process);
    }

    [SupportedOSPlatform("windows")]
    private static ProcessExecutablePathResolution ResolveWindows(int processId)
    {
        using var handle = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryLimitedInformation,
            inheritHandle: false,
            processId);
        if (handle.IsInvalid)
        {
            // OpenProcess reports ERROR_INVALID_PARAMETER when no process has this id any more.
            var openError = Marshal.GetLastPInvokeError();
            return openError == NativeMethods.ErrorInvalidParameter
                ? ProcessExecutablePathResolution.Exited
                : ProcessExecutablePathResolution.Unavailable(ReasonForWindowsError(openError));
        }

        foreach (var capacity in s_windowsPathCapacities)
        {
            var buffer = new char[capacity];
            var length = (uint)buffer.Length;
            if (NativeMethods.QueryFullProcessImageNameW(handle, 0, buffer, ref length))
            {
                return ProcessExecutablePathResolution.Resolved(new string(buffer, 0, checked((int)length)));
            }

            var queryError = Marshal.GetLastPInvokeError();
            if (queryError != NativeMethods.ErrorInsufficientBuffer)
            {
                return ProcessExecutablePathResolution.Unavailable(ReasonForWindowsError(queryError));
            }
        }

        return ProcessExecutablePathResolution.Unavailable(PathQueryFailedReason);
    }

    private static string ReasonForWindowsError(int error) =>
        error == NativeMethods.ErrorAccessDenied ? AccessDeniedReason : PathQueryFailedReason;

    private static ProcessExecutablePathResolution ResolveLinux(int processId)
    {
        try
        {
            var target = File.ResolveLinkTarget($"/proc/{processId}/exe", returnFinalTarget: false);
            return target is null
                ? ProcessExecutablePathResolution.Unavailable(PathQueryFailedReason)
                : ProcessExecutablePathResolution.Resolved(target.FullName);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // No /proc entry, or a zombie with no image left: the process has exited.
            return ProcessExecutablePathResolution.Exited;
        }
        catch (UnauthorizedAccessException)
        {
            return ProcessExecutablePathResolution.Unavailable(AccessDeniedReason);
        }
        catch (IOException)
        {
            return ProcessExecutablePathResolution.Unavailable(PathQueryFailedReason);
        }
    }

    private static ProcessExecutablePathResolution ResolveFromMainModule(Process process)
    {
        try
        {
            var path = process.MainModule?.FileName;
            if (!string.IsNullOrEmpty(path))
            {
                return ProcessExecutablePathResolution.Resolved(path);
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            // Classified below: an exited process is benign, a live one is reported as unavailable.
        }

        return HasExited(process)
            ? ProcessExecutablePathResolution.Exited
            : ProcessExecutablePathResolution.Unavailable(PathQueryFailedReason);
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            // Unknown exit state: treat the process as live so the caller reports it.
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static class NativeMethods
    {
        public const uint ProcessQueryLimitedInformation = 0x1000;
        public const int ErrorAccessDenied = 5;
        public const int ErrorInvalidParameter = 87;
        public const int ErrorInsufficientBuffer = 122;

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern SafeProcessHandle OpenProcess(
            uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            int processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryFullProcessImageNameW(
            SafeProcessHandle process,
            uint flags,
            [Out] char[] executableName,
            ref uint size);
    }
}
