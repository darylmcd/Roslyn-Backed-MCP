using System.Security.Cryptography;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class StreamContentHashAdaptiveTests
{
    [TestMethod]
    [DataRow(262143)]
    [DataRow(262144)]
    [DataRow(262145)]
    [DataRow(1048577)]
    public async Task NonseekablePartialReads_PreserveExactHashAtAndAcrossGrowth(int length)
    {
        var bytes = Bytes(length);
        using var stream = new NonseekableReadStream(bytes, 8191);
        var actual = await StreamContentHash.ComputeSha256Async(stream, CancellationToken.None);
        Assert.IsTrue(actual.AsSpan().SequenceEqual(SHA256.HashData(bytes)));
        Assert.AreEqual(length, stream.Consumed);
        Assert.IsTrue(stream.ObservedEof);
        Assert.IsTrue(stream.CanRead, "Hashing must not take ownership of the caller's stream.");
    }

    [TestMethod]
    public async Task DataAppendedBeforeEof_IsIncludedAfterGrowth()
    {
        var initial = Bytes(262144);
        var appended = Bytes(513);
        using var stream = new NonseekableReadStream(initial, int.MaxValue);
        stream.BeforeRead = (_, _) =>
        {
            if (stream.Consumed == initial.Length) stream.Append(appended);
        };
        var actual = await StreamContentHash.ComputeSha256Async(stream, CancellationToken.None);
        Assert.IsTrue(actual.AsSpan().SequenceEqual(SHA256.HashData(initial.Concat(appended).ToArray())));
        Assert.AreEqual(initial.Length + appended.Length, stream.Consumed);
        Assert.IsTrue(stream.ObservedEof);
        Assert.IsTrue(stream.CanRead);
    }

    [TestMethod]
    public async Task CancellationAfterGrowth_PropagatesOriginalToken_AndRetainsStreamOwnership()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new NonseekableReadStream(Bytes(1048577), int.MaxValue);
        var reachedExpandedRead = false;
        stream.BeforeRead = (buffer, _) =>
        {
            if (buffer.Length <= 64 * 1024) return;
            reachedExpandedRead = true;
            cancellation.Cancel();
        };
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            StreamContentHash.ComputeSha256Async(stream, cancellation.Token));
        Assert.IsTrue(reachedExpandedRead, "The cancellation must exercise the expanded buffer lifetime.");
        Assert.AreEqual(cancellation.Token, failure.CancellationToken);
        Assert.IsFalse(stream.ObservedEof);
        Assert.IsTrue(stream.CanRead);
    }

    [TestMethod]
    public async Task ReadFailureAfterGrowth_PropagatesSameFailure_WithoutRetryOrClosingStream()
    {
        using var stream = new NonseekableReadStream(Bytes(1048577), int.MaxValue);
        var expected = new IOException("Controlled expanded read failure.");
        var failures = 0;
        stream.BeforeRead = (buffer, _) =>
        {
            if (buffer.Length <= 64 * 1024) return;
            buffer.Span.Fill(0xa5); // A failed read may still populate the owned buffer.
            failures++;
            throw expected;
        };
        var actual = await Assert.ThrowsExactlyAsync<IOException>(() =>
            StreamContentHash.ComputeSha256Async(stream, CancellationToken.None));
        Assert.AreSame(expected, actual);
        Assert.AreEqual(1, failures);
        Assert.IsFalse(stream.ObservedEof);
        Assert.IsTrue(stream.CanRead);
    }

    private static byte[] Bytes(int length)
    {
        var bytes = new byte[length];
        new Random(170116).NextBytes(bytes);
        return bytes;
    }

    private sealed class NonseekableReadStream(byte[] bytes, int maximumRead) : Stream
    {
        private byte[] _bytes = bytes;
        private bool _disposed;
        internal int Consumed { get; private set; }
        internal bool ObservedEof { get; private set; }
        internal Action<Memory<byte>, CancellationToken>? BeforeRead { get; set; }
        internal void Append(byte[] suffix) => _bytes = _bytes.Concat(suffix).ToArray();
        public override bool CanRead => !_disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException("Length is not available.");
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            BeforeRead?.Invoke(buffer, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(Math.Min(buffer.Length, maximumRead), _bytes.Length - Consumed);
            _bytes.AsMemory(Consumed, count).CopyTo(buffer);
            Consumed += count;
            if (count == 0) ObservedEof = true;
            return ValueTask.FromResult(count);
        }
        protected override void Dispose(bool disposing) { _disposed = true; base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
