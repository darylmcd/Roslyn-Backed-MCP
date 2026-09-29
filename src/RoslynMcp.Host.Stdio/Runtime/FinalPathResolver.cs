using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace RoslynMcp.Host.Stdio.Runtime;

/// <summary>
/// Resolves an existing file or directory to the canonical path Windows reports for it: the
/// form <see cref="ProcessExecutablePathResolver"/> gets back for a process image.
/// </summary>
/// <remarks>
/// <para>A drive-letter alias (<c>subst</c>, <c>DefineDosDevice</c>) is not a filesystem link.
/// The physical path resolver follows its DOS-device target for workspace paths; process image
/// paths may still arrive in a different spelling. <c>GetFinalPathNameByHandleW</c> with
/// <c>FILE_NAME_NORMALIZED | VOLUME_NAME_DOS</c> maps an existing directory into the volume's
/// drive-letter form for comparison with <c>QueryFullProcessImageNameW</c> output.</para>
/// <para>Drive-letter aliases are Windows-only, so off Windows <see cref="TryResolve"/> reports no
/// result and callers compare the path as given.</para>
/// </remarks>
internal static class FinalPathResolver
{
    // Most paths fit the first buffer; the second is the NT maximum path length.
    private static readonly int[] s_windowsPathCapacities = [1024, 32_768];

    /// <summary>
    /// Resolves <paramref name="path"/> to its canonical Windows path, without the <c>\\?\</c>
    /// prefix.
    /// </summary>
    /// <param name="path">An existing file or directory.</param>
    /// <param name="finalPath">The canonical path when the method returns <see langword="true"/>.</param>
    /// <param name="error">The Win32 error when the path could not be opened or queried; 0 on
    /// success and on platforms without the query.</param>
    /// <returns><see langword="false"/> off Windows, or when the path cannot be opened or queried.</returns>
    public static bool TryResolve(string path, [NotNullWhen(true)] out string? finalPath, out int error)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        finalPath = null;
        error = 0;
        return OperatingSystem.IsWindows() && TryResolveWindows(path, out finalPath, out error);
    }

    /// <summary>
    /// Strips the <c>\\?\</c> prefix <c>GetFinalPathNameByHandleW</c> adds, turning
    /// <c>\\?\C:\x</c> into <c>C:\x</c> and <c>\\?\UNC\server\share\x</c> into
    /// <c>\\server\share\x</c>. Any other form is returned unchanged.
    /// </summary>
    internal static string ToWin32Path(string finalPath)
    {
        const string uncPrefix = @"\\?\UNC\";
        const string localPrefix = @"\\?\";
        if (finalPath.StartsWith(uncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + finalPath[uncPrefix.Length..];
        }

        // Only a drive-letter path is a Win32 path once the prefix is gone. A volume-GUID form
        // (\\?\Volume{...}\x) would not be, so it keeps its prefix.
        var isDrivePath = finalPath.Length >= localPrefix.Length + 2
            && finalPath.StartsWith(localPrefix, StringComparison.Ordinal)
            && char.IsAsciiLetter(finalPath[localPrefix.Length])
            && finalPath[localPrefix.Length + 1] == ':';
        return isDrivePath ? finalPath[localPrefix.Length..] : finalPath;
    }

    [SupportedOSPlatform("windows")]
    private static bool TryResolveWindows(string path, [NotNullWhen(true)] out string? finalPath, out int error)
    {
        finalPath = null;

        // No access rights: the handle only queries the name. FILE_FLAG_BACKUP_SEMANTICS lets
        // CreateFileW open a directory, and full sharing never conflicts with another opener.
        using var handle = NativeMethods.CreateFileW(
            path,
            desiredAccess: 0,
            NativeMethods.FileShareReadWriteDelete,
            securityAttributes: IntPtr.Zero,
            NativeMethods.OpenExisting,
            NativeMethods.FileFlagBackupSemantics,
            templateFile: IntPtr.Zero);
        if (handle.IsInvalid)
        {
            error = Marshal.GetLastPInvokeError();
            return false;
        }

        foreach (var capacity in s_windowsPathCapacities)
        {
            var buffer = new char[capacity];
            var length = NativeMethods.GetFinalPathNameByHandleW(
                handle,
                buffer,
                (uint)buffer.Length,
                NativeMethods.FileNameNormalized | NativeMethods.VolumeNameDos);
            if (length == 0)
            {
                error = Marshal.GetLastPInvokeError();
                return false;
            }

            // On success the length excludes the terminator. A length that does not fit is the
            // capacity the path needs, terminator included, so retry with the larger buffer.
            if (length < buffer.Length)
            {
                finalPath = ToWin32Path(new string(buffer, 0, checked((int)length)));
                error = 0;
                return true;
            }
        }

        error = NativeMethods.ErrorInsufficientBuffer;
        return false;
    }

    [SupportedOSPlatform("windows")]
    private static class NativeMethods
    {
        public const uint FileShareReadWriteDelete = 0x1 | 0x2 | 0x4;
        public const uint OpenExisting = 3;
        public const uint FileFlagBackupSemantics = 0x02000000;
        public const uint FileNameNormalized = 0x0;
        public const uint VolumeNameDos = 0x0;
        public const int ErrorInsufficientBuffer = 122;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern SafeFileHandle CreateFileW(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint GetFinalPathNameByHandleW(
            SafeFileHandle file,
            [Out] char[] filePath,
            uint filePathLength,
            uint flags);
    }
}
