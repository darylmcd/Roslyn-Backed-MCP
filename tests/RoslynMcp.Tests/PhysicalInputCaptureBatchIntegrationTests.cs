using System.Collections.Immutable;
using System.Security.Cryptography;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PhysicalInputCaptureBatchIntegrationTests
{
    [TestMethod]
    public async Task RealBatch_PreservesFileDirectoryMissingKinds_AndRejectsChangedTail()
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(PhysicalInputCaptureBatchIntegrationTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var files = Enumerable.Range(0, 7).Select(index => Path.Combine(root, index + ".bin")).ToArray();
            var bytes = new byte[65537];
            new Random(42).NextBytes(bytes);
            foreach (var file in files) await File.WriteAllBytesAsync(file, bytes);
            var paths = files.Append(root).Append(Path.Combine(root, "missing")).ToImmutableArray();
            var inputs = await PhysicalInputCaptureBatch.CaptureAsync(paths, CancellationToken.None);
            CollectionAssert.AreEqual(paths.ToArray(), inputs.Select(input => input.FilePath).ToArray());
            foreach (var input in inputs.Take(files.Length))
            {
                Assert.AreEqual(PhysicalInputKind.File, input.Kind);
                Assert.IsTrue(input.ContentHash.AsSpan().SequenceEqual(SHA256.HashData(bytes)));
            }
            Assert.AreEqual(PhysicalInputKind.Directory, inputs[^2].Kind);
            Assert.AreEqual(PhysicalInputKind.Missing, inputs[^1].Kind);
            await PhysicalInputCaptureBatch.AssertCurrentAsync(inputs, CancellationToken.None);
            bytes[^1] ^= 0xff;
            await File.WriteAllBytesAsync(files[^1], bytes);
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => PhysicalInputCaptureBatch.AssertCurrentAsync(inputs, CancellationToken.None));
        }
        catch (Exception primaryFailure) { primary = primaryFailure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Batch fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    public async Task RealBatch_ReplacedParentAliasCannotReuseEarlierPhysicalIdentity()
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(PhysicalInputCaptureBatchIntegrationTests), Guid.NewGuid().ToString("N"));
        var component = Path.Combine(root, "component");
        var target = Path.Combine(root, "target");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(component);
            Directory.CreateDirectory(target);
            var file = Path.Combine(component, "input.bin");
            await File.WriteAllTextAsync(file, "identical bytes");
            await File.WriteAllTextAsync(Path.Combine(target, "input.bin"), "identical bytes");
            var inputs = await PhysicalInputCaptureBatch.CaptureAsync([file], CancellationToken.None);
            File.Delete(file);
            Directory.Delete(component);
            if (!TestFixtureFileSystem.TryCreateDirectoryLink(component, target))
            {
                Assert.Inconclusive("Directory links are unavailable in this test environment.");
                return;
            }
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => PhysicalInputCaptureBatch.AssertCurrentAsync(inputs, CancellationToken.None));
        }
        catch (Exception primaryFailure) { primary = primaryFailure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Batch alias cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    public async Task RealBatch_PreCancelledTokenWinsBeforeInvalidPathObservation()
    {
        CancellationTokenSource? cancellation = null;
        Exception? primary = null;
        try
        {
            cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var failure = await Assert.ThrowsAsync<OperationCanceledException>(() => PhysicalInputCaptureBatch.CaptureAsync(["relative-invalid-path"], cancellation.Token));
            Assert.AreEqual(cancellation.Token, failure.CancellationToken);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Batch cancellation fixture cleanup failed.", primary, cancellation); }
    }
}
