using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using RoslynMcp.Core.Services;
using Microsoft.Win32.SafeHandles;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Reads the bytes currently present in a redirected <see cref="System.Diagnostics.Process"/>
/// output pipe, straight from its handle, without waiting for more.
/// </summary>
/// <remarks>
/// The source must be the only reader of the pipe. It bypasses the stream and reader that
/// <see cref="System.Diagnostics.Process"/> wraps around the handle, so neither may be read
/// while a source is in use: a buffered read would take bytes the source cannot see.
/// </remarks>
internal static class ProcessPipeSource
{
    internal static IAvailableByteSource Create(StreamReader redirectedOutput)
    {
        ArgumentNullException.ThrowIfNull(redirectedOutput);
        SafeHandle handle = redirectedOutput.BaseStream switch
        {
            FileStream file => file.SafeFileHandle,
            PipeStream pipe => pipe.SafePipeHandle,
            var other => throw InvalidOperationErrors.Internal(
                $"Redirected process output uses an unsupported stream type '{other.GetType().FullName}'."),
        };

        return OperatingSystem.IsWindows() ? new WindowsPipeSource(handle) : new UnixPipeSource(handle);
    }

    [SupportedOSPlatform("windows")]
    private sealed class WindowsPipeSource(SafeHandle handle) : IAvailableByteSource
    {
        private const int ErrorBrokenPipe = 109;

        public AvailableReadStatus ReadAvailable(Span<byte> destination, out int bytesRead)
        {
            bytesRead = 0;
            if (!PeekNamedPipe(handle, IntPtr.Zero, 0, IntPtr.Zero, out var available, IntPtr.Zero))
            {
                return EndOrThrow();
            }

            if (available == 0)
            {
                return AvailableReadStatus.Empty;
            }

            // Sole reader: the bytes just counted are still there, so this cannot block.
            var count = (uint)Math.Min((uint)destination.Length, available);
            if (!ReadFile(handle, ref MemoryMarshal.GetReference(destination), count, out var read, IntPtr.Zero))
            {
                return EndOrThrow();
            }

            bytesRead = checked((int)read);
            return bytesRead == 0 ? AvailableReadStatus.Empty : AvailableReadStatus.Data;
        }

        private static AvailableReadStatus EndOrThrow()
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == ErrorBrokenPipe)
            {
                // Every writer, descendants included, closed its end and the pipe is drained.
                return AvailableReadStatus.End;
            }

            throw new IOException("Could not read redirected process output.", new Win32Exception(error));
        }

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekNamedPipe(
            SafeHandle pipe, IntPtr buffer, uint bufferSize, IntPtr bytesRead, out uint totalBytesAvailable,
            IntPtr bytesLeftThisMessage);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReadFile(
            SafeHandle file, ref byte buffer, uint bytesToRead, out uint bytesRead, IntPtr overlapped);
    }

    [UnsupportedOSPlatform("windows")]
    private sealed class UnixPipeSource(SafeHandle handle) : IAvailableByteSource
    {
        // Values shared by Linux and macOS.
        private const short PollIn = 0x0001;
        private const int ErrorInterrupted = 4;      // EINTR
        private const int ErrorAgainLinux = 11;      // EAGAIN on Linux
        private const int ErrorAgainMacOs = 35;      // EAGAIN on macOS

        public AvailableReadStatus ReadAvailable(Span<byte> destination, out int bytesRead)
        {
            bytesRead = 0;
            var addedReference = false;
            try
            {
                handle.DangerousAddRef(ref addedReference);
                var descriptor = (int)handle.DangerousGetHandle();
                var pollState = new PollDescriptor { FileDescriptor = descriptor, Events = PollIn };
                var ready = Poll(ref pollState, 1, 0);
                if (ready < 0)
                {
                    return EmptyIfTransientOrThrow();
                }

                if (ready == 0)
                {
                    return AvailableReadStatus.Empty;
                }

                if ((pollState.ReturnedEvents & PollIn) == 0)
                {
                    // Hang-up or error with nothing left to read.
                    return AvailableReadStatus.End;
                }

                var read = Read(descriptor, ref MemoryMarshal.GetReference(destination), destination.Length);
                if (read < 0)
                {
                    return EmptyIfTransientOrThrow();
                }

                if (read == 0)
                {
                    return AvailableReadStatus.End;
                }

                bytesRead = checked((int)read);
                return AvailableReadStatus.Data;
            }
            finally
            {
                if (addedReference)
                {
                    handle.DangerousRelease();
                }
            }
        }

        private static AvailableReadStatus EmptyIfTransientOrThrow()
        {
            var error = Marshal.GetLastPInvokeError();
            if (error is ErrorInterrupted or ErrorAgainLinux or ErrorAgainMacOs)
            {
                return AvailableReadStatus.Empty;
            }

            throw new IOException("Could not read redirected process output.", new Win32Exception(error));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PollDescriptor
        {
            public int FileDescriptor;
            public short Events;
            public short ReturnedEvents;
        }

        [DllImport("libc", EntryPoint = "poll", SetLastError = true, ExactSpelling = true)]
        private static extern int Poll(ref PollDescriptor descriptors, nuint count, int timeoutMilliseconds);

        [DllImport("libc", EntryPoint = "read", SetLastError = true, ExactSpelling = true)]
        private static extern nint Read(int descriptor, ref byte buffer, nint count);
    }
}
