using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;
namespace RoslynMcp.Tests;

[TestClass]
public sealed class PhysicalDirectoryAcquisitionAuthorityTests
{
    [TestMethod]
    public async Task FreshOwnedAcquisition_DoesNotDeletePopulatedOrdinaryReplacement()
    {
        var fixture = Path.Combine(TestTempRoot.Current, "owned-acquire-" + Guid.NewGuid().ToString("N"));
        var issued = Path.Combine(fixture, "issued");
        var retained = Path.Combine(fixture, "retained-original");
        var marker = Path.Combine(issued, "unknown-marker.txt");
        var replaced = false;
        var renameAttempted = false;
        var renameBlocked = false;
        PhysicalDirectoryScope.OwnedRoot? owned = null;
        ReplacingParent? parent = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(fixture);
            parent = new ReplacingParent(PhysicalDirectoryScope.OpenRoute(fixture), () =>
            {
                renameAttempted = true;
                try { Directory.Move(issued, retained); }
                catch (IOException) when (OperatingSystem.IsWindows()) { renameBlocked = true; return; }
                Directory.CreateDirectory(issued);
                File.WriteAllText(marker, "unknown replacement bytes");
                replaced = true;
            });
            Exception? observed = null;
            try
            {
                owned = PhysicalDirectoryScope.CreateOwnedChild(parent, "issued");
                owned.Delete(CancellationToken.None);
            }
            catch (IOException failure) { observed = failure; }
            Assert.IsTrue(renameAttempted, "The actual directory rename must be attempted.");
            if (OperatingSystem.IsWindows())
            {
                Assert.IsTrue(renameBlocked, "The atomically returned original directory handle must block rename before ownership publication.");
                Assert.IsFalse(replaced);
                Assert.IsNull(observed);
                Assert.IsFalse(Directory.Exists(issued), "The unchanged original owned directory must be removed.");
            }
            else
            {
                Assert.IsTrue(replaced, "Real create/rename/populated replacement must precede empty-object admission.");
                Assert.IsTrue(parent.OpenObservedReplacement);
                Assert.IsTrue(Directory.Exists(retained));
                Assert.AreEqual("unknown replacement bytes", parent.MarkerBytesAtOpen);
                Assert.IsTrue(parent.OrdinaryReplacementAtOpen);
                Assert.IsTrue(File.Exists(marker), "Fresh ownership must not delete pre-admission unknown replacement contents.");
                Assert.AreEqual("unknown replacement bytes", File.ReadAllText(marker));
                Assert.IsNotNull(observed, "A populated replacement must be refused before cleanup ownership is issued.");
                Assert.IsNull(owned);
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Acquisition regression and fixture release failed.", primary,
                CleanupFailureCollector.FromAction(() => owned?.Dispose()),
                CleanupFailureCollector.FromAction(() => parent?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
        }
    }

    [TestMethod]
    public async Task FreshOwnedAcquisition_AdmitsEmptyObjectAndCleansOnlyItsNamespace()
    {
        var fixture = Path.Combine(TestTempRoot.Current, "owned-empty-" + Guid.NewGuid().ToString("N"));
        var issued = Path.Combine(fixture, "issued");
        PhysicalDirectoryScope.OwnedRoot? owned = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(fixture);
            owned = PhysicalDirectoryScope.CreateOwnedRoot(issued);
            var payload = Path.Combine(issued, "owned.txt");
            File.WriteAllText(payload, "post-admission owned bytes");
            var outcome = owned.Delete(CancellationToken.None);
            Assert.IsFalse(File.Exists(payload));
            Assert.AreEqual(OperatingSystem.IsWindows() ? PhysicalDirectoryCleanupOutcome.Removed : PhysicalDirectoryCleanupOutcome.Retained, outcome);
            Assert.AreEqual(!OperatingSystem.IsWindows(), Directory.Exists(issued));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Empty acquisition fixture release failed.", primary,
                CleanupFailureCollector.FromAction(() => owned?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
        }
    }

    [TestMethod]
    public async Task FreshOwnedAcquisition_RefusesExistingPopulatedDestination()
    {
        var fixture = Path.Combine(TestTempRoot.Current, "owned-existing-" + Guid.NewGuid().ToString("N"));
        var issued = Path.Combine(fixture, "issued");
        var marker = Path.Combine(issued, "unknown.txt");
        PhysicalDirectoryScope.OwnedRoot? unexpected = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(issued);
            File.WriteAllText(marker, "pre-existing bytes");
            Exception? observed = null;
            try { unexpected = PhysicalDirectoryScope.CreateOwnedRoot(issued); }
            catch (IOException failure) { observed = failure; }
            Assert.AreEqual("pre-existing bytes", File.ReadAllText(marker));
            Assert.IsNull(unexpected);
            Assert.IsNotNull(observed, "Fresh acquisition must remain exclusive, not adopt an existing name.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Existing destination fixture release failed.", primary,
                CleanupFailureCollector.FromAction(() => unexpected?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
        }
    }

    [TestMethod]
    public async Task WindowsPhysicalRoute_PinsEveryPrefixAndReleasesAllPins()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Effective no-delete route pins are a Windows contract.");
        var fixture = Path.Combine(TestTempRoot.Current, "linear-route-" + Guid.NewGuid().ToString("N"));
        var retained = fixture + "-released";
        var ancestor = Path.Combine(fixture, "ancestor");
        var leaf = Path.Combine(ancestor, "middle", "leaf");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(leaf);
            await File.WriteAllTextAsync(Path.Combine(leaf, "sentinel.txt"), "held route bytes");
            {
                var route = PhysicalDirectoryScope.OpenRoute(leaf);
                Exception? resourceFailure12 = null;
                try
                {
                    foreach (var prefix in new[]
                    {
                        fixture,
                        ancestor,
                        Path.Combine(ancestor, "middle"),
                        leaf
                    }

                    )
                    {
                        Assert.ThrowsExactly<IOException>(() => Directory.Move(prefix, prefix + "-replacement"), "Every acquired route prefix must retain an effective no-delete pin.");
                        route.AssertCurrentPath();
                    }

                    var independent = PhysicalDirectoryScope.OpenRoute(leaf);
                    Exception? resourceFailure13 = null;
                    try
                    {
                        Assert.IsTrue(route.SameIdentity(independent));
                    }
                    catch (Exception failure)
                    {
                        resourceFailure13 = failure;
                        throw;
                    }
                    finally
                    {
                        OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure13, independent);
                    }
                }
                catch (Exception failure)
                {
                    resourceFailure12 = failure;
                    throw;
                }
                finally
                {
                    OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure12, route);
                }
            }

            Directory.Move(fixture, retained);
            Assert.AreEqual("held route bytes", await File.ReadAllTextAsync(Path.Combine(retained, "ancestor", "middle", "leaf", "sentinel.txt")), "Every route pin must be released after disposal.");
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Windows route pin fixture release failed.", primary, CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)), CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(retained)));
        }
    }

    private sealed class ReplacingParent(PhysicalDirectoryScope inner, Action replace) : PhysicalDirectoryScope(inner.Path)
    {
        private bool _disposed;
        internal bool OpenObservedReplacement { get; private set; }
        internal bool OrdinaryReplacementAtOpen { get; private set; }
        internal string? MarkerBytesAtOpen { get; private set; }
        internal override PhysicalDirectoryScope CreateOwnedChildScope(string name)
        {
            var created = inner.CreateOwnedChildScope(name);
            PhysicalDirectoryScope? pending = created;
            try
            {
                // POSIX's supported boundary is the later EMPTY-object admission,
                // not mkdir creator identity. Recreate the real open-gap candidate.
                if (!OperatingSystem.IsWindows())
                {
                    pending = null;
                    created.Dispose();
                }

                replace();
                if (OperatingSystem.IsWindows())
                {
                    pending = null;
                    return created;
                }

                return inner.OpenChild(name) ?? throw new AssertFailedException("Replacement must be physically open.");
            }
            catch (Exception failure)
            {
                OwnedResourceCleanup.DisposePreservingFailure("Acquired child fallback cleanup failed.", failure, pending);
                throw;
            }
        }
        internal override PhysicalDirectoryScope? OpenChild(string name)
        {
            var path = System.IO.Path.Combine(Path, name);
            var marker = System.IO.Path.Combine(path, "unknown-marker.txt");
            OpenObservedReplacement = File.Exists(marker);
            OrdinaryReplacementAtOpen = (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
            if (OpenObservedReplacement) MarkerBytesAtOpen = File.ReadAllText(marker);
            return inner.OpenChild(name);
        }
        internal override PhysicalDirectoryEntryKind? GetEntryKind(string name, CancellationToken ct) => inner.GetEntryKind(name, ct);
        internal override DateTime? ReadFileLastWriteTimeUtcIfPresent(string name, CancellationToken ct) => inner.ReadFileLastWriteTimeUtcIfPresent(name, ct);
        internal override IReadOnlyList<string> Entries() => inner.Entries();
        internal override bool SameIdentity(PhysicalDirectoryScope other) => inner.SameIdentity(other);
        internal override bool IsLink(string name) => inner.IsLink(name);
        internal override IDisposable? LockOwner(string name) => inner.LockOwner(name);
        internal override DateTime LastActivityUtc => inner.LastActivityUtc;
        internal override bool TryDeleteFile(string name, CancellationToken ct) => inner.TryDeleteFile(name, ct);
        internal override PhysicalDirectoryScope? OpenDirectoryForAccess(string name, bool create, PhysicalDirectoryCreationMode creationMode, CancellationToken ct)
            => inner.OpenDirectoryForAccess(name, create, creationMode, ct);
        private PhysicalDirectoryScope Inner => inner;
        internal override bool SameFilesystem(PhysicalDirectoryScope other) => inner.SameFilesystem(other is ReplacingParent wrapper ? wrapper.Inner : other);
        internal override Microsoft.Win32.SafeHandles.SafeFileHandle CreateFreshFile(string name, CancellationToken ct)
            => inner.CreateFreshFile(name, ct);
        internal override Microsoft.Win32.SafeHandles.SafeFileHandle? OpenReadFileIfPresent(string name, CancellationToken ct)
            => inner.OpenReadFileIfPresent(name, ct);
        internal override Microsoft.Win32.SafeHandles.SafeFileHandle? OpenTransitionFileIfPresent(string name, bool writable, CancellationToken ct)
            => inner.OpenTransitionFileIfPresent(name, writable, ct);
        internal override Microsoft.Win32.SafeHandles.SafeFileHandle CreateTransitionFile(string name, CancellationToken ct)
            => inner.CreateTransitionFile(name, ct);
        internal override bool CurrentFileMatches(string name, Microsoft.Win32.SafeHandles.SafeFileHandle witness)
            => inner.CurrentFileMatches(name, witness);
        internal override void MoveFileNoReplace(string name, Microsoft.Win32.SafeHandles.SafeFileHandle? witness,
            PhysicalDirectoryScope destination, string destinationName, CancellationToken ct)
            => inner.MoveFileNoReplace(name, witness, destination, destinationName, ct);
        internal override void ReclaimAdmittedTransitionFile(string name, Microsoft.Win32.SafeHandles.SafeFileHandle witness, CancellationToken ct)
            => inner.ReclaimAdmittedTransitionFile(name, witness, ct);
        internal override UnixFileMode? ReadFileModeIfPresent(string name, CancellationToken ct)
            => inner.ReadFileModeIfPresent(name, ct);
        internal override void PublishPreparedFile(string temporary, Microsoft.Win32.SafeHandles.SafeFileHandle preparedWitness, string name, Microsoft.Win32.SafeHandles.SafeFileHandle? replacedWitness, CancellationToken ct)
            => inner.PublishPreparedFile(temporary, preparedWitness, name, replacedWitness, ct);
        internal override void ReleaseNamespacePin() => inner.ReleaseNamespacePin();
        internal override PhysicalDirectoryCleanupOutcome RemoveHeldChild(string name, PhysicalDirectoryScope child, CancellationToken ct) => inner.RemoveHeldChild(name, child, ct);
        public override void Dispose() { if (_disposed) return; _disposed = true; inner.Dispose(); }
    }
}
