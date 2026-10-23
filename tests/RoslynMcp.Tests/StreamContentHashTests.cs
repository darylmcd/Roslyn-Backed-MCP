using System.Security.Cryptography;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class StreamContentHashTests
{
    [TestMethod]
    public async Task PartialReads_HashOnlyReturnedBytes_ThroughEof_WithoutTakingStreamOwnership()
    {
        var bytes = new byte[65537];
        new Random(42).NextBytes(bytes);
        using var stream = new ControlledReadStream(bytes);
        var actual = await StreamContentHash.ComputeSha256Async(stream, CancellationToken.None);
        Assert.IsTrue(actual.AsSpan().SequenceEqual(SHA256.HashData(bytes)));
        Assert.IsTrue(stream.Reads > bytes.Length / 13);
        Assert.AreEqual(stream.Length, stream.Position);
        Assert.IsTrue(stream.CanRead);
    }

    [TestMethod]
    public async Task CancellationBetweenReads_PropagatesToken_WithoutPublishingHashOrClosingStream()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new ControlledReadStream(new byte[100]);
        stream.AfterFirstRead = cancellation.Cancel;
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            StreamContentHash.ComputeSha256Async(stream, cancellation.Token));
        Assert.AreEqual(cancellation.Token, failure.CancellationToken);
        Assert.AreEqual(2, stream.Reads);
        Assert.AreEqual(13L, stream.Position);
        Assert.IsTrue(stream.CanRead);
    }

    [TestMethod]
    public async Task ReadFailure_PropagatesExactFailure_WithoutRetryOrClosingStream()
    {
        using var stream = new ControlledReadStream(new byte[100]);
        var expected = new IOException("Owned test read failed.");
        stream.SecondReadFailure = expected;
        var actual = await Assert.ThrowsExactlyAsync<IOException>(() =>
            StreamContentHash.ComputeSha256Async(stream, CancellationToken.None));
        Assert.AreSame(expected, actual);
        Assert.AreEqual(2, stream.Reads);
        Assert.AreEqual(13L, stream.Position);
        Assert.IsTrue(stream.CanRead);
    }

    [TestMethod]
    public async Task EntryCancellation_DoesNotReadStream()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var stream = new ControlledReadStream(new byte[100]);
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            StreamContentHash.ComputeSha256Async(stream, cancellation.Token));
        Assert.AreEqual(cancellation.Token, failure.CancellationToken);
        Assert.AreEqual(0, stream.Reads);
        Assert.IsTrue(stream.CanRead);
    }

    private sealed class ControlledReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        internal int Reads { get; private set; }
        internal Action? AfterFirstRead { get; set; }
        internal IOException? SecondReadFailure { get; set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reads++;
            cancellationToken.ThrowIfCancellationRequested();
            if (Reads == 2 && SecondReadFailure is { } failure) throw failure;
            var read = Read(buffer.Span[..Math.Min(buffer.Length, 13)]);
            if (Reads == 1) AfterFirstRead?.Invoke();
            return ValueTask.FromResult(read);
        }
    }
}
