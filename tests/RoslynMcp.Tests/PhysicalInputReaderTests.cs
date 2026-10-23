using System.Security.Cryptography;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PhysicalInputReaderTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task ChangedLeafOrParent_WithIdenticalBytes_RefusesBeforeIssuingReadableStream(bool swapParent)
        => WithRootAsync(async root =>
        {
            var parent = Path.Combine(root, "approved");
            var outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(parent);
            Directory.CreateDirectory(outside);
            var requested = Path.Combine(parent, "input.bin");
            var substituted = Path.Combine(outside, "input.bin");
            var bytes = "same bytes cannot grant another object's path"u8.ToArray();
            await File.WriteAllBytesAsync(requested, bytes);
            await File.WriteAllBytesAsync(substituted, bytes);
            WorkspaceManager.EnsurePhysicalPath(requested);

            // Deterministically model the gap between pathname approval and actual open.
            File.Delete(requested);
            bool linked;
            if (swapParent)
            {
                Directory.Delete(parent);
                linked = TestFixtureFileSystem.TryCreateDirectoryLink(parent, outside);
            }
            else linked = TestFixtureFileSystem.TryCreateFileLink(requested, substituted);
            if (!linked) Assert.Inconclusive("Filesystem links are unavailable in this environment.");
            using var opened = File.OpenHandle(requested, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, FileOptions.Asynchronous);
            var readableIssued = false;
            Assert.ThrowsExactly<InvalidOperationException>(() =>
            {
                using var lease = PhysicalInputReader.BindOpenedFile(requested, opened, CancellationToken.None);
                readableIssued = true;
            });
            Assert.IsFalse(readableIssued, "Held-object validation must reject before exposing any readable stream.");
            Assert.IsTrue(opened.IsClosed, "The binder owns and disposes rejected handles.");
        });

    [TestMethod]
    public Task OpenedObject_MovedBeforeBinding_CannotBorrowReplacementPathWithIdenticalBytes()
        => WithRootAsync(async root =>
        {
            var requested = Path.Combine(root, "input.bin");
            var moved = Path.Combine(root, "moved.bin");
            var bytes = "same bytes, different held object"u8.ToArray();
            await File.WriteAllBytesAsync(requested, bytes);
            WorkspaceManager.EnsurePhysicalPath(requested);
            using var opened = File.OpenHandle(requested, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, FileOptions.Asynchronous);
            File.Move(requested, moved);
            await File.WriteAllBytesAsync(requested, bytes);
            var readableIssued = false;
            Assert.ThrowsExactly<InvalidOperationException>(() =>
            {
                using var lease = PhysicalInputReader.BindOpenedFile(requested, opened, CancellationToken.None);
                readableIssued = true;
            });
            Assert.IsFalse(readableIssued);
            Assert.IsTrue(opened.IsClosed);
        });

    [TestMethod]
    [DataRow(0)]
    [DataRow(65537)]
    public Task StableFile_ReadsExactBytesAndHashesSameHeldKind(int length)
        => WithRootAsync(async root =>
        {
            var path = Path.Combine(root, "input.bin");
            var bytes = new byte[length];
            new Random(170117).NextBytes(bytes);
            await File.WriteAllBytesAsync(path, bytes);
            CollectionAssert.AreEqual(bytes, await PhysicalInputReader.ReadAllBytesAsync(path, CancellationToken.None));
            CollectionAssert.AreEqual(bytes, PhysicalInputReader.ReadAllBytes(path));
            var identity = await PhysicalInputReader.CaptureIdentityAsync(path, CancellationToken.None);
            Assert.AreEqual(PhysicalInputKind.File, identity.Kind);
            Assert.IsTrue(identity.ContentHash.AsSpan().SequenceEqual(SHA256.HashData(bytes)));
            File.Delete(path);
            Assert.IsFalse(File.Exists(path));
        });

    [TestMethod]
    public Task DirectoryAndMissingRoutes_RetainDistinctKindsAndByteReaderRefusal()
        => WithRootAsync(async root =>
        {
            var directory = await PhysicalInputReader.CaptureIdentityAsync(root, CancellationToken.None);
            Assert.AreEqual(PhysicalInputKind.Directory, directory.Kind);
            foreach (var path in new[] { Path.Combine(root, "missing.bin"), Path.Combine(root, "absent", "nested", "missing.bin") })
            {
                var missing = await PhysicalInputReader.CaptureIdentityAsync(path, CancellationToken.None);
                Assert.AreEqual(PhysicalInputKind.Missing, missing.Kind);
            }
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => PhysicalInputReader.ReadAllBytesAsync(root, CancellationToken.None));
        });

    [TestMethod]
    public Task MissingLeafThroughLinkedParent_IsRefusedInsteadOfClassifiedMissing()
        => WithRootAsync(async root =>
        {
            var outside = Path.Combine(root, "outside");
            var alias = Path.Combine(root, "alias");
            Directory.CreateDirectory(outside);
            if (!TestFixtureFileSystem.TryCreateDirectoryLink(alias, outside))
                Assert.Inconclusive("Filesystem links are unavailable in this environment.");
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                PhysicalInputReader.CaptureIdentityAsync(Path.Combine(alias, "absent", "input.bin"), CancellationToken.None));
        });

    [TestMethod]
    public Task ByteReaders_DistinguishMissingLeafFromMissingParent()
        => WithRootAsync(async root =>
        {
            var missingLeaf = Path.Combine(root, "missing.bin");
            var missingParent = Path.Combine(root, "absent", "nested", "missing.bin");
            await Assert.ThrowsExactlyAsync<FileNotFoundException>(() =>
                PhysicalInputReader.ReadAllBytesAsync(missingLeaf, CancellationToken.None));
            await Assert.ThrowsExactlyAsync<DirectoryNotFoundException>(() =>
                PhysicalInputReader.ReadAllBytesAsync(missingParent, CancellationToken.None));
            Assert.ThrowsExactly<FileNotFoundException>(() => PhysicalInputReader.ReadAllBytes(missingLeaf));
            Assert.ThrowsExactly<DirectoryNotFoundException>(() => PhysicalInputReader.ReadAllBytes(missingParent));
        });

    [TestMethod]
    public Task CancelledBinding_DisposesAcquiredHandleAndPreservesToken()
        => WithRootAsync(async root =>
        {
            var path = Path.Combine(root, "input.bin");
            await File.WriteAllBytesAsync(path, [1, 2, 3]);
            using var opened = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, FileOptions.Asynchronous);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var failure = Assert.Throws<OperationCanceledException>(() =>
            {
                using var lease = PhysicalInputReader.BindOpenedFile(path, opened, cancellation.Token);
            });
            Assert.AreEqual(cancellation.Token, failure.CancellationToken);
            Assert.IsTrue(opened.IsClosed);
        });

    [TestMethod]
    public async Task PreCancelledCapture_DoesNotObserveInvalidPath()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            PhysicalInputReader.CaptureIdentityAsync("relative-invalid-path", cancellation.Token));
        Assert.AreEqual(cancellation.Token, failure.CancellationToken);
    }

    [TestMethod]
    public Task ExclusiveSharingFailure_IsNotMissing_AndDoesNotLeakHandle()
        => WithRootAsync(async root =>
        {
            var path = Path.Combine(root, "input.bin");
            await File.WriteAllBytesAsync(path, [1, 2, 3]);
            using (var blocker = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                await Assert.ThrowsAsync<IOException>(() => PhysicalInputReader.ReadAllBytesAsync(path, CancellationToken.None));
                await Assert.ThrowsAsync<IOException>(() => PhysicalInputReader.CaptureIdentityAsync(path, CancellationToken.None));
            }
            File.Delete(path);
            Assert.IsFalse(File.Exists(path));
        });

    [TestMethod]
    public Task WindowsByteReader_RetainsShareReadWhileStreamIsOwned()
        => WithRootAsync(async root =>
        {
            if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows share-write/delete enforcement is required.");
            var path = Path.Combine(root, "input.bin");
            await File.WriteAllBytesAsync(path, [1, 2, 3]);
            using (var input = PhysicalInputReader.OpenRead(path, CancellationToken.None))
            {
                Assert.Throws<IOException>(() =>
                {
                    using var writer = new FileStream(path, FileMode.Open, FileAccess.Write,
                        FileShare.ReadWrite | FileShare.Delete);
                });
                Assert.Throws<IOException>(() => File.Delete(path));
                using var otherReader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                Assert.AreEqual(1, otherReader.ReadByte());
            }
            using (var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read))
                writer.WriteByte(4);
            File.Delete(path);
        });

    [TestMethod]
    public Task WindowsIdentityCapture_PreservesReadWriteDeleteSharing()
        => WithRootAsync(async root =>
        {
            if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows share-write/delete enforcement is required.");
            var path = Path.Combine(root, "input.bin");
            var bytes = new byte[] { 1, 2, 3 };
            await File.WriteAllBytesAsync(path, bytes);
            // DeleteOnClose requests DELETE access as well as this handle's read/write access.
            // The identity reader must retain its original sharing contract for all three.
            using (var concurrentOwner = new FileStream(path, FileMode.Open, FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.DeleteOnClose))
            {
                var identity = await PhysicalInputReader.CaptureIdentityAsync(path, CancellationToken.None);
                Assert.AreEqual(PhysicalInputKind.File, identity.Kind);
                Assert.IsTrue(identity.ContentHash.AsSpan().SequenceEqual(SHA256.HashData(bytes)));
            }
            Assert.IsFalse(File.Exists(path));
        });

    [TestMethod]
    public Task UnixHeldInput_CopiesThroughSameValidatedStream()
        => WithRootAsync(async root =>
        {
            if (OperatingSystem.IsWindows()) Assert.Inconclusive("The Unix held-source copy route is required.");
            var source = Path.Combine(root, "source.bin");
            var destination = Path.Combine(root, "copy.bin");
            var bytes = new byte[262145];
            new Random(170117).NextBytes(bytes);
            await File.WriteAllBytesAsync(source, bytes);
            await using (var input = PhysicalInputReader.OpenRead(source, CancellationToken.None))
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await input.Stream.CopyToAsync(output, CancellationToken.None);
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(destination));
            File.Delete(source);
            Assert.IsFalse(File.Exists(source));
        });

    [TestMethod]
    public Task WindowsOwnedRootCopy_UsesExplicitCoordinateForHandleBackedSource()
        => WithRootAsync(async root =>
        {
            if (!OperatingSystem.IsWindows()) Assert.Inconclusive("The Windows owned-root native copy route is required.");
            var source = Path.Combine(root, "source.bin");
            var bytes = new byte[262145];
            new Random(170117).NextBytes(bytes);
            await File.WriteAllBytesAsync(source, bytes);
            using var owner = PhysicalDirectoryScope.CreateOwnedRoot(Path.Combine(root, "owned"));
            await using var input = PhysicalInputReader.OpenRead(source, CancellationToken.None);
            Assert.IsFalse(Path.IsPathFullyQualified(input.Stream.Name),
                "The handle-backed source must not supply a pathname through FileStream.Name.");
            await owner.CopyFromStreamAsync(Path.Combine("nested", "copy.bin"), source, input.Stream, CancellationToken.None);
            var copy = Path.Combine(owner.Path, "nested", "copy.bin");
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(copy));
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(source));
            Assert.IsTrue(input.Stream.CanRead, "The copy operation must not dispose its caller-owned source lease.");
        });

    [TestMethod]
    [DataRow("pipe")]
    [DataRow("globalroot")]
    [DataRow("nul")]
    public Task WindowsDeviceNamespace_RefusesBeforeIssuingStreamOrIdentity(string kind)
        => WithRootAsync(async root =>
        {
            if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows device namespace admission is required.");
            var path = kind switch
            {
                "pipe" => @"\\.\pipe\RoslynMcpOwnedInput_" + Guid.NewGuid().ToString("N"),
                "globalroot" => @"\\?\GLOBALROOT\Device\Null",
                "nul" => Path.Combine(root, "NUL"),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            Assert.ThrowsExactly<InvalidOperationException>(() =>
            {
                using var lease = PhysicalInputReader.OpenRead(path, CancellationToken.None);
            });
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                PhysicalInputReader.ReadAllBytesAsync(path, CancellationToken.None));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                PhysicalInputReader.CaptureIdentityAsync(path, CancellationToken.None));
        });

    [TestMethod]
    public Task WindowsExtendedDosPath_RetainsValidPhysicalRead()
        => WithRootAsync(async root =>
        {
            if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows extended DOS path admission is required.");
            if (root.Length < 3 || root[1] != ':') Assert.Inconclusive("A drive-letter fixture is required for this DOS-coordinate control.");
            var path = Path.Combine(root, "input.bin");
            var extended = @"\\?\" + path;
            var bytes = new byte[] { 1, 2, 3, 4 };
            await File.WriteAllBytesAsync(path, bytes);
            CollectionAssert.AreEqual(bytes, await PhysicalInputReader.ReadAllBytesAsync(extended, CancellationToken.None));
            CollectionAssert.AreEqual(bytes, PhysicalInputReader.ReadAllBytes(extended));
            var identity = await PhysicalInputReader.CaptureIdentityAsync(extended, CancellationToken.None);
            Assert.AreEqual(PhysicalInputKind.File, identity.Kind);
            Assert.IsTrue(identity.ContentHash.AsSpan().SequenceEqual(SHA256.HashData(bytes)));
        });

    [TestMethod]
    public void UnletteredVolumeMapping_RetainsGuidIdentityWithoutInventingDrive()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows volume-coordinate mapping is required.");
        const string volume = @"\\?\Volume{12345678-1234-1234-1234-123456789abc}\nested\input.bin";
        var unlettered = PhysicalPathResolver.CanonicalizeVolumeHandlePath(volume, null);
        Assert.AreEqual(volume, unlettered);
        Assert.AreNotEqual(@"C:\nested\input.bin", unlettered);
        Assert.AreEqual(@"C:\nested\input.bin", PhysicalPathResolver.CanonicalizeVolumeHandlePath(volume, @"C:\"));
    }

    [TestMethod]
    public Task WindowsLongHeldPath_ResizesNativeReplyAndPreservesBytesKindAndOwnership()
        => WithRootAsync(async root =>
        {
            if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows variable-length native handle paths are required.");
            if (root.Length < 3 || root[1] != ':') Assert.Inconclusive("A drive-letter fixture is required for the extended path control.");
            var directory = root;
            while (directory.Length < 600)
                directory = Path.Combine(directory, "physical-input-long-component-0123456789");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "input.bin");
            var extended = @"\\?\" + path;
            var bytes = new byte[65537];
            new Random(170117).NextBytes(bytes);
            await File.WriteAllBytesAsync(extended, bytes);
            Assert.IsTrue(path.Length > 512, "The actual held-object name must exceed the native query's initial buffer.");
            CollectionAssert.AreEqual(bytes, await PhysicalInputReader.ReadAllBytesAsync(extended, CancellationToken.None));
            CollectionAssert.AreEqual(bytes, PhysicalInputReader.ReadAllBytes(extended));
            var identity = await PhysicalInputReader.CaptureIdentityAsync(extended, CancellationToken.None);
            Assert.AreEqual(PhysicalInputKind.File, identity.Kind);
            Assert.IsTrue(identity.ContentHash.AsSpan().SequenceEqual(SHA256.HashData(bytes)));
            Microsoft.Win32.SafeHandles.SafeFileHandle handle;
            await using (var input = PhysicalInputReader.OpenRead(extended, CancellationToken.None))
            {
                handle = input.Stream.SafeFileHandle;
                Assert.IsFalse(handle.IsClosed);
                using var copied = new MemoryStream();
                await input.Stream.CopyToAsync(copied, CancellationToken.None);
                CollectionAssert.AreEqual(bytes, copied.ToArray());
                Assert.IsFalse(handle.IsClosed, "Consuming bytes must not transfer lease ownership.");
            }
            Assert.IsTrue(handle.IsClosed, "The lease must close its native handle after the resized query.");
            File.Delete(extended);
            Assert.IsFalse(File.Exists(extended));
        });

    private static async Task WithRootAsync(Func<string, Task> action)
    {
        var lexical = Path.Combine(TestTempRoot.Current, nameof(PhysicalInputReaderTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(lexical);
        var root = PhysicalPathResolver.Resolve(lexical);
        Exception? primary = null;
        try { await action(root); }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Held input fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
