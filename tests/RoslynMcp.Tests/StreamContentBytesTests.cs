using RoslynMcp.Roslyn.Helpers;
namespace RoslynMcp.Tests;

[TestClass]
public sealed class StreamContentBytesTests
{
    [TestMethod]
    [DataRow(false, 0, 0)]
    [DataRow(true, 0, 0)]
    [DataRow(false, 65537, 65537)]
    [DataRow(true, 65537, 65537)]
    [DataRow(false, 3, 65537)]
    [DataRow(true, 3, 65537)]
    [DataRow(false, 65537, 3)]
    [DataRow(true, 65537, 3)]
    [DataRow(false, -1, 65537)]
    [DataRow(true, -1, 65537)]
    public async Task PartialReads_IgnoreLengthAsEndCondition_AndRetainOwnership(bool asynchronous, int advertised, int actual)
    {
        var expected = new byte[actual]; new Random(42).NextBytes(expected);
        using var input = new Controlled(expected, advertised);
        var result = await Read(asynchronous, input);
        CollectionAssert.AreEqual(expected, result);
        Assert.AreEqual(1, input.EofReads);
        Assert.IsTrue(input.CanRead);
        Assert.AreEqual(asynchronous ? 0 : input.Reads, input.SyncReads);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnrepresentableAdvertisedLength_DoesNotRefuseSmallActualContent(bool asynchronous)
    {
        byte[] expected = [1, 2, 3];
        using var input = new Controlled(expected, long.MaxValue);
        CollectionAssert.AreEqual(expected, await Read(asynchronous, input));
        Assert.AreEqual(1, input.EofReads);
        Assert.IsTrue(input.CanRead);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReadFailure_PreservesExactException_NoRetryOrOwnershipTransfer(bool asynchronous)
    {
        using var input = new Controlled(new byte[40], 40);
        var expected = new IOException("Fixture read failure"); input.Failure = expected;
        var actual = await Assert.ThrowsExactlyAsync<IOException>(() => Read(asynchronous, input));
        Assert.AreSame(expected, actual); Assert.AreEqual(2, input.Reads); Assert.IsTrue(input.CanRead);
    }

    [TestMethod]
    public async Task Cancellation_AfterPartialRead_StopsBeforeNextRead_AndPreservesToken()
    {
        using var stop = new CancellationTokenSource();
        using var input = new Controlled(new byte[40], 40) { AfterFirstRead = stop.Cancel };
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(() => StreamContentBytes.ReadAllAsync(input, stop.Token));
        Assert.AreEqual(stop.Token, failure.CancellationToken); Assert.AreEqual(1, input.Reads); Assert.IsTrue(input.CanRead);
    }

    [TestMethod]
    public async Task EntryCancellation_DoesNotInspectOrReadStream()
    {
        using var stop = new CancellationTokenSource(); stop.Cancel();
        using var input = new Controlled(new byte[40], 40);
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(() => StreamContentBytes.ReadAllAsync(input, stop.Token));
        Assert.AreEqual(stop.Token, failure.CancellationToken); Assert.AreEqual(0, input.Reads); Assert.AreEqual(0, input.LengthReads);
    }

    private static async Task<byte[]> Read(bool asynchronous, Stream input) => asynchronous
        ? await StreamContentBytes.ReadAllAsync(input, CancellationToken.None) : StreamContentBytes.ReadAll(input);

    private sealed class Controlled(byte[] bytes, long advertised) : MemoryStream(bytes)
    {
        internal int Reads, SyncReads, EofReads, LengthReads;
        internal IOException? Failure;
        internal Action? AfterFirstRead;
        public override bool CanSeek => advertised >= 0;
        public override long Length { get { LengthReads++; return advertised; } }
        public override int Read(Span<byte> buffer) { SyncReads++; return ReadCore(buffer); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(ReadCore(buffer.Span)); }
        private int ReadCore(Span<byte> buffer)
        {
            Reads++;
            if (Reads == 2 && Failure is { } error) throw error;
            var read = base.Read(buffer[..Math.Min(13, buffer.Length)]);
            if (read == 0) EofReads++;
            if (Reads == 1) AfterFirstRead?.Invoke();
            return read;
        }
    }
}
