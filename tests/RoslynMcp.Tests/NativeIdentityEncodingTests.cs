using System.Text;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeIdentityEncodingTests
{
    [TestMethod]
    public void ExactFrame_PreservesEveryCodeUnitAndChunkBoundary()
    {
        var all = new string(Enumerable.Range(0, 65536).Select(value => (char)value).ToArray());
        foreach (var value in new[] { "", "a", new string((char)0xD800, 1), new string((char)0xDC00, 1), all, all[..511], all[..512], all[..513] })
        {
            using var expected = new MemoryStream();
            using var actual = new MemoryStream();
            using var reference = new BinaryWriter(expected, Encoding.UTF8, true);
            using var writer = new BinaryWriter(actual, Encoding.UTF8, true);
            reference.Write(value.Length);
            foreach (var character in value) reference.Write((ushort)character);
            NativeIdentityEncoding.WriteString(writer, value, CancellationToken.None);
            CollectionAssert.AreEqual(expected.ToArray(), actual.ToArray());
            actual.Position = 0;
            using var reader = new BinaryReader(actual, Encoding.UTF8, true);
            Assert.AreEqual(value, NativeIdentityEncoding.ReadString(reader, CancellationToken.None));
            Assert.AreEqual(actual.Length, actual.Position);
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(4)]
    [DataRow(1028)]
    [DataRow(2052)]
    public void ExactFrame_CancellationRetainsTokenAndStopsAfterCompletedWrite(int cancelAt)
    {
        using var source = new CancellationTokenSource();
        using var stream = new MemoryStream();
        using var writer = new CancellingWriter(stream, source, cancelAt);
        if (cancelAt == 0) source.Cancel();
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
            NativeIdentityEncoding.WriteString(writer, new string('x', 1024), source.Token));
        Assert.AreEqual(source.Token, failure.CancellationToken);
        Assert.AreEqual((long)cancelAt, stream.Length);
    }

    private sealed class CancellingWriter(Stream stream, CancellationTokenSource source, int cancelAt) : BinaryWriter(stream, Encoding.UTF8, true)
    {
        private void Observe() { if (BaseStream.Position >= cancelAt) source.Cancel(); }
        public override void Write(int value) { base.Write(value); Observe(); }
        public override void Write(ushort value) { base.Write(value); Observe(); }
        public override void Write(ReadOnlySpan<byte> value) { base.Write(value); Observe(); }
    }
}
