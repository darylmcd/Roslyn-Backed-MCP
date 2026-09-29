using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RoslynMcp.Tests;

/// <summary>
/// Maps a free drive letter to a directory for the calling logon session, as <c>subst</c> does,
/// with <c>DefineDosDeviceW</c>. The physical path resolver follows the DOS-device target,
/// while the kernel also reports files under the backing volume's drive letter.
/// Dispose removes the mapping.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsDriveAlias : IDisposable
{
    private const uint DddRemoveDefinition = 0x00000002;
    private const uint DddExactMatchOnRemove = 0x00000004;

    // Skips the WM_SETTINGCHANGE broadcast to every top-level window, so shells never pick up
    // the transient letter.
    private const uint DddNoBroadcastSystem = 0x00000008;

    private const int ErrorFileNotFound = 2;
    private const int ErrorInsufficientBuffer = 122;

    // QueryDosDeviceW returns every stacked mapping; the first buffer fits a handful of them.
    private static readonly int[] s_queryCapacities = [1024, 32_768];

    private static readonly object s_createLock = new();

    private readonly string _targetPath;
    private bool _disposed;

    private WindowsDriveAlias(string driveName, string targetPath)
    {
        DriveName = driveName;
        _targetPath = targetPath;
    }

    /// <summary>The mapped drive, such as <c>X:</c>.</summary>
    public string DriveName { get; }

    /// <summary>The root of the mapped drive, such as <c>X:\</c>.</summary>
    public string RootPath => DriveName + Path.DirectorySeparatorChar;

    /// <summary>
    /// Maps a free drive letter to <paramref name="targetDirectory"/>, which must exist. Throws
    /// <see cref="Win32Exception"/> when the mapping cannot be created, and
    /// <see cref="InvalidOperationException"/> when no drive letter is free.
    /// </summary>
    public static WindowsDriveAlias Create(string targetDirectory)
    {
        var fullTargetPath = Path.GetFullPath(targetDirectory);
        var targetPath = string.Equals(Path.GetPathRoot(fullTargetPath), fullTargetPath, StringComparison.OrdinalIgnoreCase)
            ? fullTargetPath
            : fullTargetPath.TrimEnd(Path.DirectorySeparatorChar);

        // The lock serializes tests in this process. A random letter order keeps other test
        // processes in the same logon session off the same letter.
        lock (s_createLock)
        {
            var letters = "DEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();
            Random.Shared.Shuffle(letters);
            foreach (var letter in letters)
            {
                var driveName = letter + ":";
                if (QueryMappings(driveName, out _) != ErrorFileNotFound)
                {
                    continue;
                }

                if (!DefineDosDeviceW(DddNoBroadcastSystem, driveName, targetPath))
                {
                    throw new Win32Exception(
                        Marshal.GetLastPInvokeError(),
                        $"DefineDosDeviceW could not map {driveName} to '{targetPath}'.");
                }

                // DefineDosDeviceW stacks definitions. Another process may have defined the same
                // letter between the check and the define, so keep it only as the sole mapping.
                if (QueryMappings(driveName, out var mappings) == 0
                    && mappings.Length == 1
                    && string.Equals(mappings[0], @"\??\" + targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    return new WindowsDriveAlias(driveName, targetPath);
                }

                Remove(driveName, targetPath);
            }
        }

        throw new InvalidOperationException("No free drive letter is available for a drive alias.");
    }

    /// <summary>
    /// Removes the mapping. Throws <see cref="Win32Exception"/> if it cannot: a leaked alias
    /// outlives the test process for the rest of the logon session.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Remove(DriveName, _targetPath);
    }

    private static void Remove(string driveName, string targetPath)
    {
        if (!DefineDosDeviceW(
                DddRemoveDefinition | DddExactMatchOnRemove | DddNoBroadcastSystem,
                driveName,
                targetPath))
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                $"DefineDosDeviceW could not remove the {driveName} alias of '{targetPath}'. " +
                "Remove it with: subst " + driveName + " /d");
        }
    }

    /// <returns>0 with every stacked mapping, the one in effect first; otherwise the Win32 error
    /// (<c>ERROR_FILE_NOT_FOUND</c> when the letter is not defined).</returns>
    private static int QueryMappings(string driveName, out string[] mappings)
    {
        mappings = [];
        var error = 0;
        foreach (var capacity in s_queryCapacities)
        {
            var buffer = new char[capacity];
            var length = QueryDosDeviceW(driveName, buffer, (uint)buffer.Length);
            if (length != 0)
            {
                // A list of NUL-terminated strings ending in an extra NUL.
                mappings = new string(buffer, 0, checked((int)length))
                    .Split('\0', StringSplitOptions.RemoveEmptyEntries);
                return 0;
            }

            error = Marshal.GetLastPInvokeError();
            if (error != ErrorInsufficientBuffer)
            {
                break;
            }
        }

        return error;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DefineDosDeviceW(uint flags, string deviceName, string targetPath);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDeviceW(string deviceName, [Out] char[] targetPath, uint maxLength);
}
