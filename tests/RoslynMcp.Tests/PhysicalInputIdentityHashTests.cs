using System.Security.Cryptography;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PhysicalInputIdentityHashTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(4095)]
    [DataRow(4096)]
    [DataRow(4097)]
    [DataRow(65535)]
    [DataRow(65536)]
    [DataRow(65537)]
    [DataRow(1048577)]
    public async Task Capture_HashesThroughEof_AndCurrentCheckDetectsChangedTail(int length)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(PhysicalInputIdentityHashTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "input.bin");
            var bytes = new byte[length];
            new Random(42).NextBytes(bytes);
            await File.WriteAllBytesAsync(path, bytes);
            var identity = await PhysicalInputIdentity.CaptureAsync(path, CancellationToken.None);
            Assert.AreEqual(PhysicalInputKind.File, identity.Kind);
            Assert.IsTrue(identity.ContentHash.AsSpan().SequenceEqual(SHA256.HashData(bytes)));
            await PhysicalInputIdentity.AssertCurrentAsync([identity], CancellationToken.None);

            // Preserve length for nonempty files so the refusal proves content, not length, freshness.
            if (bytes.Length == 0) bytes = [1];
            else bytes[^1] ^= 0xff;
            await File.WriteAllBytesAsync(path, bytes);
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                PhysicalInputIdentity.AssertCurrentAsync([identity], CancellationToken.None));
            var changed = await PhysicalInputIdentity.CaptureAsync(path, CancellationToken.None);
            Assert.IsTrue(changed.ContentHash.AsSpan().SequenceEqual(SHA256.HashData(bytes)));
        }
        finally { TestFixtureFileSystem.DeleteDirectoryIfExists(root); }
    }
}
