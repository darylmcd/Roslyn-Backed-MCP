using System.Text.Json;

namespace RoslynMcp.Roslyn.Services;

internal sealed record CompilerReferenceCaptureFrame(string Kind, string Context, string[] Fields)
{
    internal const int MaximumBytes = 1024 * 1024;
    internal const int MaximumTotalBytes = 64 * 1024 * 1024;
    // For n UTF16 units the URL token costs 2+4*ceil((4+2n)/3)
    // JSON bytes, at most five times the old string's minimum n+2.
    internal const int MaximumWireBytes = 5 * MaximumBytes;
    internal const int MaximumWireTotalBytes = 5 * MaximumTotalBytes;

    internal long LogicalSize(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Every UTF16 unit needs at least one old JSON byte; every array
        // element needs at least two. Reject impossible frames before the
        // serializer can buffer a single oversized escaped token.
        long minimum = (Kind?.Length ?? 0) + (long)(Context?.Length ?? 0);
        if (Fields is { } fields)
        {
            minimum += 2L * fields.Length;
            if (minimum > MaximumBytes) throw QuotaFailure();
            foreach (var field in fields)
            {
                ct.ThrowIfCancellationRequested();
                minimum += field?.Length ?? 0;
                if (minimum > MaximumBytes) throw QuotaFailure();
            }
        }
        if (minimum > MaximumBytes) throw QuotaFailure();
        // Old default JSON is used ONLY to retain the existing logical quota.
        // Its replacement behavior confers no identity or payload authority.
        using var counter = new QuotaCounter(ct);
        JsonSerializer.Serialize(counter, this);
        ct.ThrowIfCancellationRequested();
        return counter.Length;
    }

    internal byte[] Encode(CancellationToken ct)
    {
        _ = LogicalSize(ct);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this, NativeStructuredIdentity.CreateStringOptions(ct));
        if (bytes.Length > MaximumWireBytes) throw QuotaFailure();
        ct.ThrowIfCancellationRequested();
        return bytes;
    }

    internal static CompilerReferenceCaptureFrame? Decode(ReadOnlySpan<byte> bytes, CancellationToken ct, out long logicalSize)
    {
        ct.ThrowIfCancellationRequested();
        if (bytes.Length > MaximumWireBytes) throw QuotaFailure();
        var frame = JsonSerializer.Deserialize<CompilerReferenceCaptureFrame>(bytes, NativeStructuredIdentity.CreateStringOptions(ct));
        logicalSize = frame?.LogicalSize(ct) ?? 4;
        ct.ThrowIfCancellationRequested();
        return frame;
    }

    public override string ToString() => $"CompilerReferenceCaptureFrame {{ Kind = {Kind}, FieldCount = {Fields.Length} }}";

    private static InvalidDataException QuotaFailure() => new("Compiler reference input exceeds the private frame capacity.");

    private sealed class QuotaCounter(CancellationToken ct) : Stream
    {
        private long _length;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _length;
        public override long Position { get => _length; set => throw new NotSupportedException(); }
        public override void Flush() => ct.ThrowIfCancellationRequested();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ct.ThrowIfCancellationRequested();
            var next = checked(_length + buffer.Length);
            if (next > MaximumBytes) throw QuotaFailure();
            _length = next;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

/// <summary>Independent finite wire and compatibility logical quotas for one private capture.</summary>
internal sealed class CompilerReferenceCaptureBudget(CancellationToken ct)
{
    internal const int MaximumFrames = 1_000_000;
    private int _frames;
    private long _wireBytes;
    private long _logicalBytes;

    internal void AcceptWireLength(int length)
    {
        ct.ThrowIfCancellationRequested();
        if (length is <= 0 or > CompilerReferenceCaptureFrame.MaximumWireBytes || _frames == MaximumFrames
            || _wireBytes + length > CompilerReferenceCaptureFrame.MaximumWireTotalBytes) throw Failure();
        _frames++;
        _wireBytes += length;
    }

    internal void AcceptLogicalLength(long length)
    {
        ct.ThrowIfCancellationRequested();
        if (length is < 0 or > CompilerReferenceCaptureFrame.MaximumBytes
            || _logicalBytes + length > CompilerReferenceCaptureFrame.MaximumTotalBytes) throw Failure();
        _logicalBytes += length;
    }

    private static InvalidDataException Failure() => new("The private compiler capture exceeded its bounded protocol.");
}
