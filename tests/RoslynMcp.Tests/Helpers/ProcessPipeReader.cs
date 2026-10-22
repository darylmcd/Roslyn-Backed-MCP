using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace RoslynMcp.Tests.Helpers;

internal static class ProcessPipeReader
{
    internal static async Task<string> ReadToEndAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        using var pipe = new ProcessPipeStream(reader.BaseStream);
        using var decoder = new StreamReader(pipe, reader.CurrentEncoding, detectEncodingFromByteOrderMarks: true);
        return await decoder.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class ProcessPipeStream(Stream source) : Stream
    {
        private readonly byte[] _buffer = new byte[4096];
        private bool _readPrefix;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (destination.IsEmpty)
                return 0;

            // Always return control to the caller before reading, so it can install the
            // deadline. Queue fairly between chunks even if a pipe is continuously readable:
            // the other stream, process-exit callbacks and deadline callbacks must progress.
            await Task.Factory.StartNew(
                static () => { },
                cancellationToken,
                TaskCreationOptions.PreferFairness,
                TaskScheduler.Default).ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

            // StreamReader must see the complete BOM before choosing an encoding. A pipe
            // can deliver its first bytes separately; UTF-32 has the longest BOM (four bytes).
            var minimum = _readPrefix ? 1 : Math.Min(4, destination.Length);
            var written = 0;
            while (written < minimum)
            {
                var read = OperatingSystem.IsWindows()
                    ? await ReadAvailableWindowsAsync(destination[written..], cancellationToken).ConfigureAwait(false)
                    : await source.ReadAsync(destination[written..], cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                written += read;
            }

            _readPrefix = true;
            return written;
        }

        [SupportedOSPlatform("windows")]
        private async ValueTask<int> ReadAvailableWindowsAsync(Memory<byte> destination, CancellationToken cancellationToken)
        {
            // Windows Process uses synchronous anonymous pipes. Its ReadToEndAsync parks a
            // pool worker in ReadFile, starving other readers and exit callbacks. We are the
            // sole reader: no read is pending while peeking, and only available bytes are read.
            // Bypass FileStream buffering so a peek never overlooks bytes it prefetched.
            var handle = ((FileStream)source).SafeFileHandle;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!PeekNamedPipe(handle, IntPtr.Zero, 0, IntPtr.Zero, out var available, IntPtr.Zero))
                    return EndOfPipeOrThrow();
                if (available != 0)
                {
                    var count = (uint)Math.Min(Math.Min(destination.Length, _buffer.Length), available);
                    if (!ReadFile(handle, _buffer, count, out var read, IntPtr.Zero))
                        return EndOfPipeOrThrow();
                    _buffer.AsMemory(0, checked((int)read)).CopyTo(destination);
                    return checked((int)read);
                }

                // Empty pipes retain no worker, including pipes held by an exited parent's child.
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        private static int EndOfPipeOrThrow()
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == 109) // ERROR_BROKEN_PIPE: every writer closed, including descendants.
                return 0;
            throw new IOException("Could not read redirected process output.", new Win32Exception(error));
        }

        // Process owns the source and disposes it only after both readers are observed.
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekNamedPipe(SafeFileHandle pipe, IntPtr buffer, uint size,
            IntPtr bytesRead, out uint available, IntPtr bytesLeft);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReadFile(SafeFileHandle pipe, [Out] byte[] buffer, uint count,
            out uint read, IntPtr overlapped);
    }
}
