using System.Buffers;
using System.Text;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Outcome of one non-waiting read attempt against a redirected process pipe.</summary>
internal enum AvailableReadStatus
{
    /// <summary>No bytes are in the pipe right now; a writer still holds it open.</summary>
    Empty,

    /// <summary>At least one byte was read.</summary>
    Data,

    /// <summary>Every writer closed the pipe and it is drained.</summary>
    End,
}

/// <summary>
/// A byte source that only ever hands over bytes already present, without waiting for more.
/// </summary>
internal interface IAvailableByteSource
{
    AvailableReadStatus ReadAvailable(Span<byte> destination, out int bytesRead);
}

/// <summary>
/// Captures one redirected output stream of a child process without ever parking a read.
/// </summary>
/// <remarks>
/// <para>
/// A descendant that inherited the pipe (an MSBuild worker node, VBCSCompiler) can hold it open
/// long after the child exits, so end-of-stream may never arrive and the capture has to stop on a
/// deadline. Stopping by cancelling a pending read is lossy: if the cancellation is observed
/// before the read has run, bytes the child already wrote stay in the pipe and are discarded.
/// </para>
/// <para>
/// The pump therefore asks only for bytes that are already in the pipe. Everything the child
/// wrote is in the pipe by the time it exits, so one read attempt that starts after the exit was
/// observed and finds the pipe empty proves the child's output was captured in full, however late
/// that attempt was scheduled. The post-exit grace period only decides how long to keep listening
/// for output from descendants.
/// </para>
/// </remarks>
internal static class ProcessOutputPump
{
    private const int ReadBufferSize = 4096;

    /// <summary>Consecutive non-empty reads after which the pump yields its worker thread.</summary>
    private const int ReadsBetweenYields = 64;

    internal static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(15);

    /// <summary>
    /// Upper bound on bytes accepted once the grace period has expired. The child's own unread
    /// output is at most one pipe buffer (at most 1 MiB on Linux, far less elsewhere); the bound
    /// only stops a descendant that never stops writing from extending the drain forever.
    /// </summary>
    internal const int PostGraceByteLimit = 1024 * 1024;

    internal static async Task<string> ReadAsync(
        IAvailableByteSource source,
        Encoding encoding,
        int outputLimit,
        Action<string>? onDecodedChunk,
        Func<bool> hasExited,
        TimeSpan postExitGracePeriod,
        TimeSpan pollInterval,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var bytes = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
        var decoder = new ProcessOutputDecoder(encoding);
        var bounded = new BoundedTextBuffer(outputLimit);
        long? exitObservedAt = null;
        var postGraceBytes = 0;
        var consecutiveReads = 0;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Sample the exit before the read attempt. Only then does an empty pipe prove that
                // everything the child wrote before exiting has been consumed.
                if (exitObservedAt is null && hasExited())
                {
                    exitObservedAt = timeProvider.GetTimestamp();
                }

                var graceExpired = exitObservedAt is { } observed &&
                    timeProvider.GetElapsedTime(observed) >= postExitGracePeriod;

                var status = source.ReadAvailable(bytes.AsSpan(0, ReadBufferSize), out var bytesRead);
                if (status == AvailableReadStatus.End)
                {
                    break;
                }

                if (status == AvailableReadStatus.Data)
                {
                    Append(decoder.Decode(bytes.AsSpan(0, bytesRead), flush: false), bounded, onDecodedChunk);
                    if (graceExpired)
                    {
                        postGraceBytes += bytesRead;
                        if (postGraceBytes >= PostGraceByteLimit)
                        {
                            break;
                        }
                    }

                    if (++consecutiveReads >= ReadsBetweenYields)
                    {
                        consecutiveReads = 0;
                        await Task.Yield();
                    }

                    continue;
                }

                if (graceExpired)
                {
                    break;
                }

                consecutiveReads = 0;
                await Task.Delay(pollInterval, timeProvider, cancellationToken).ConfigureAwait(false);
            }

            Append(decoder.Decode([], flush: true), bounded, onDecodedChunk);
            return bounded.ToString();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    private static void Append(string decoded, BoundedTextBuffer bounded, Action<string>? onDecodedChunk)
    {
        if (decoded.Length == 0)
        {
            return;
        }

        bounded.Append(decoded);
        onDecodedChunk?.Invoke(decoded);
    }

    /// <summary>Keeps only the final <c>maxLength</c> characters of everything appended.</summary>
    private sealed class BoundedTextBuffer(int maxLength)
    {
        private readonly StringBuilder _builder = new(Math.Min(maxLength, ReadBufferSize));

        public void Append(ReadOnlySpan<char> value)
        {
            if (value.Length >= maxLength)
            {
                _builder.Clear();
                _builder.Append(value[^maxLength..]);
                return;
            }

            _builder.Append(value);
            if (_builder.Length > maxLength)
            {
                _builder.Remove(0, _builder.Length - maxLength);
            }
        }

        public override string ToString() => _builder.ToString();
    }
}

/// <summary>
/// Incremental decoder with the byte-order-mark behaviour of the <see cref="StreamReader"/> that
/// <see cref="System.Diagnostics.Process"/> creates for redirected output: a leading UTF-8,
/// UTF-16 or UTF-32 mark selects that encoding and is not emitted; otherwise the configured
/// encoding is used. Bytes are never dropped between calls.
/// </summary>
internal sealed class ProcessOutputDecoder(Encoding fallbackEncoding)
{
    private const int LongestByteOrderMark = 4;

    private readonly byte[] _prefix = new byte[LongestByteOrderMark];
    private int _prefixLength;
    private Decoder? _decoder;

    public string Decode(ReadOnlySpan<byte> bytes, bool flush)
    {
        if (_decoder is null)
        {
            // Hold back the first bytes until they can no longer be the start of a mark.
            var take = Math.Min(LongestByteOrderMark - _prefixLength, bytes.Length);
            bytes[..take].CopyTo(_prefix.AsSpan(_prefixLength));
            _prefixLength += take;
            bytes = bytes[take..];

            var prefix = _prefix.AsSpan(0, _prefixLength);
            if (!flush && _prefixLength < LongestByteOrderMark && CouldStartByteOrderMark(prefix))
            {
                return string.Empty;
            }

            var (detected, markLength) = DetectByteOrderMark(prefix);
            _decoder = (detected ?? fallbackEncoding).GetDecoder();
            var head = Convert(prefix[markLength..], flush: flush && bytes.IsEmpty);
            return bytes.IsEmpty ? head : head + Convert(bytes, flush);
        }

        return Convert(bytes, flush);
    }

    private string Convert(ReadOnlySpan<byte> bytes, bool flush)
    {
        var decoder = _decoder!;
        var charCount = decoder.GetCharCount(bytes, flush);
        if (charCount == 0)
        {
            // Still advance the decoder state over a partial sequence.
            decoder.GetChars(bytes, [], flush);
            return string.Empty;
        }

        var chars = ArrayPool<char>.Shared.Rent(charCount);
        try
        {
            var written = decoder.GetChars(bytes, chars, flush);
            return new string(chars, 0, written);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(chars);
        }
    }

    private static bool CouldStartByteOrderMark(ReadOnlySpan<byte> prefix)
    {
        if (prefix.IsEmpty)
        {
            return true;
        }

        // UTF-16 little-endian (FF FE) is itself a proper prefix of the UTF-32 little-endian mark.
        ReadOnlySpan<byte> utf8 = [0xEF, 0xBB, 0xBF];
        ReadOnlySpan<byte> utf16BigEndian = [0xFE, 0xFF];
        ReadOnlySpan<byte> utf32LittleEndian = [0xFF, 0xFE, 0x00, 0x00];
        ReadOnlySpan<byte> utf32BigEndian = [0x00, 0x00, 0xFE, 0xFF];
        return IsPrefixOf(prefix, utf8) || IsPrefixOf(prefix, utf16BigEndian) ||
            IsPrefixOf(prefix, utf32LittleEndian) || IsPrefixOf(prefix, utf32BigEndian);

        static bool IsPrefixOf(ReadOnlySpan<byte> candidate, ReadOnlySpan<byte> mark)
            => candidate.Length < mark.Length && mark.StartsWith(candidate);
    }

    private static (Encoding? Encoding, int MarkLength) DetectByteOrderMark(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length >= 4 && prefix[0] == 0xFF && prefix[1] == 0xFE && prefix[2] == 0 && prefix[3] == 0)
        {
            return (new UTF32Encoding(bigEndian: false, byteOrderMark: true), 4);
        }

        if (prefix.Length >= 4 && prefix[0] == 0 && prefix[1] == 0 && prefix[2] == 0xFE && prefix[3] == 0xFF)
        {
            return (new UTF32Encoding(bigEndian: true, byteOrderMark: true), 4);
        }

        if (prefix.Length >= 3 && prefix[0] == 0xEF && prefix[1] == 0xBB && prefix[2] == 0xBF)
        {
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 3);
        }

        if (prefix.Length >= 2 && prefix[0] == 0xFE && prefix[1] == 0xFF)
        {
            return (new UnicodeEncoding(bigEndian: true, byteOrderMark: true), 2);
        }

        if (prefix.Length >= 2 && prefix[0] == 0xFF && prefix[1] == 0xFE)
        {
            return (new UnicodeEncoding(bigEndian: false, byteOrderMark: true), 2);
        }

        return (null, 0);
    }
}
