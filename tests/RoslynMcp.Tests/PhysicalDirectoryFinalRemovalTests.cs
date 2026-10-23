using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;


[TestClass]
public sealed class PhysicalDirectoryFinalRemovalTests
{
    [TestMethod]
    public async Task FinalUnlink_DoesNotDeleteReplacementAfterCheckedHandleReleased()
    {
        var fixture = Path.Combine(TestTempRoot.Current, "final-unlink-" + Guid.NewGuid().ToString("N"));
        var parentPath = Path.Combine(fixture, "parent");
        var issued = Path.Combine(parentPath, "issued");
        var retained = Path.Combine(parentPath, "retained");
        var unrelated = Path.Combine(parentPath, "unrelated.txt");
        Directory.CreateDirectory(issued);
        File.WriteAllText(unrelated, "unrelated sibling bytes");
        var replaced = false;
        Exception? primaryFailure = null;
        PhysicalDirectoryScope? actualParent = null;
        PhysicalDirectoryScope? actualRoot = null;
        ForwardScope? parent = null;
        PhysicalDirectoryScope.OwnedRoot? owned = null;
        try
        {
            actualParent = PhysicalDirectoryScope.OpenRoute(parentPath);
            actualRoot = actualParent.OpenChild("issued") ?? throw new AssertFailedException("Original directory must be physically open.");
            parent = new ForwardScope(actualParent, () =>
            {
                // Runs after the original namespace pin is released, before deletion binds.
                // These are real filesystem operations, not simulated identities.
                Directory.Move(issued, retained);
                Directory.CreateDirectory(issued);
                replaced = true;
            }, false, null, null, null);
            owned = new PhysicalDirectoryScope.OwnedRoot(parent, new ForwardScope(actualRoot, null, false, null, null, null), "issued");
            Exception? observed = null;
            try { owned.Delete(CancellationToken.None); }
            catch (IOException failure) { observed = failure; }
            Assert.IsTrue(replaced, "The adversarial namespace replacement must actually execute.");
            Assert.AreEqual("unrelated sibling bytes", File.ReadAllText(unrelated));
            Assert.IsTrue(Directory.Exists(retained), "The original object survives at its new name.");
            Assert.IsTrue(Directory.Exists(issued), "Cleanup must not remove the unrelated empty replacement.");
            Assert.IsNotNull(observed, "Changed issued namespace must refuse cleanup.");
        }
        catch (Exception failure) { primaryFailure = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Final unlink regression and fixture cleanup failed.", primaryFailure,
                CleanupFailureCollector.FromAction(() => owned?.Dispose()),
                CleanupFailureCollector.FromAction(() => parent?.Dispose()),
                CleanupFailureCollector.FromAction(() => actualRoot?.Dispose()),
                CleanupFailureCollector.FromAction(() => actualParent?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NonDirectorySelection_DoesNotDeleteReplacementPhysicalDirectory(bool link)
    {
        var fixture = Path.Combine(TestTempRoot.Current, "entry-removal-" + Guid.NewGuid().ToString("N"));
        var entry = Path.Combine(fixture, "entry");
        var retained = Path.Combine(fixture, "retained");
        var external = Path.Combine(fixture, "external");
        Directory.CreateDirectory(external);
        var sentinel = Path.Combine(external, "preserve.txt");
        File.WriteAllText(sentinel, "unrelated external bytes");
        if (link)
        {
            if (OperatingSystem.IsWindows()) WindowsDirectoryJunction.Create(entry, external);
            else Directory.CreateSymbolicLink(entry, external);
        }
        else File.WriteAllText(entry, "selected file bytes");
        Exception? primary = null;
        var replaced = false;
        try
        {
            using var actualParent = PhysicalDirectoryScope.OpenRoute(fixture);
            using var parent = new ForwardScope(actualParent, null, false, () =>
            {
                if (link) Directory.Move(entry, retained);
                else File.Move(entry, retained);
                Directory.CreateDirectory(entry);
                replaced = true;
            }, null, null);
            Exception? observed = null;
            try { parent.DeleteChild("entry", directoriesOnly: false, CancellationToken.None); }
            catch (IOException failure) { observed = failure; }
            Assert.IsTrue(replaced, "The actual file/link selection must be replaced before its removal primitive runs.");
            Assert.AreEqual("unrelated external bytes", File.ReadAllText(sentinel));
            if (!link) Assert.AreEqual("selected file bytes", File.ReadAllText(retained));
            Assert.IsTrue(Directory.Exists(entry), "An unwitnessed physical-directory replacement is not a file/link cleanup capability.");
            Assert.HasCount(0, Directory.GetFileSystemEntries(entry));
            Assert.IsNotNull(observed, "The file/link primitive must refuse the physical directory it actually opens.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Entry-removal regression and fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() =>
                {
                    if (link && Directory.Exists(retained) && (File.GetAttributes(retained) & FileAttributes.ReparsePoint) != 0)
                        Directory.Delete(retained);
                }),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
        }
    }

    [TestMethod]
    public async Task OwnedWriter_HoldsActualParentAcrossNamespaceReplacement()
    {
        var fixture = Path.Combine(TestTempRoot.Current, "writer-route-" + Guid.NewGuid().ToString("N"));
        var issued = Path.Combine(fixture, "issued");
        var nested = Path.Combine(issued, "nested");
        var retained = Path.Combine(issued, "retained");
        var external = Path.Combine(fixture, "external");
        Directory.CreateDirectory(external);
        File.WriteAllText(Path.Combine(external, "target"), "external bytes");
        var attempted = false;
        var blocked = false;
        var replaced = false;
        PhysicalDirectoryScope.OwnedRoot? owner = null;
        Exception? primary = null;
        try
        {
            var parent = new ForwardScope(PhysicalDirectoryScope.OpenRoute(fixture), null, false, null, () =>
            {
                Assert.IsFalse(attempted, "The bounded route must acquire the single requested parent once.");
                attempted = true;
                try { Directory.Move(nested, retained); }
                catch (IOException) when (OperatingSystem.IsWindows()) { blocked = true; return; }
                Directory.CreateSymbolicLink(nested, external);
                replaced = true;
            }, null);
            owner = PhysicalDirectoryScope.CreateOwnedChild(parent, "issued");
            Directory.CreateDirectory(nested);
            await owner.WriteFileAsync("nested/target", "owned bytes"u8.ToArray(), CancellationToken.None);
            Assert.IsTrue(attempted, "The actor must execute after an actual child directory handle is held.");
            Assert.AreEqual(OperatingSystem.IsWindows(), blocked, "Windows pins must block the real rename attempt.");
            Assert.AreEqual(!OperatingSystem.IsWindows(), replaced, "POSIX must exercise the actual renamed-parent descriptor.");
            Assert.AreEqual("external bytes", File.ReadAllText(Path.Combine(external, "target")));
            Assert.AreEqual("owned bytes", File.ReadAllText(Path.Combine(replaced ? retained : nested, "target")));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Owned writer route and cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => owner?.Dispose()),
                CleanupFailureCollector.FromAction(() => { if (replaced) Directory.Delete(nested); }),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OwnedWriter_IsRevokedAfterDisposeOrFailedCleanup(bool failedCleanup)
    {
        var fixture = Path.Combine(TestTempRoot.Current, "writer-revoked-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        PhysicalDirectoryScope.OwnedRoot? owner = null;
        Exception? primary = null;
        var removalAttempts = 0;
        var cleanupArmed = false;
        try
        {
            var parent = new ForwardScope(PhysicalDirectoryScope.OpenRoute(fixture), null, false, () =>
            {
                if (cleanupArmed && ++removalAttempts == 1) throw new IOException("Controlled cleanup failure before removal.");
            }, null, null);
            owner = PhysicalDirectoryScope.CreateOwnedChild(parent, "issued");
            await owner.WriteFileAsync("original", "original bytes"u8.ToArray(), CancellationToken.None);
            cleanupArmed = true;
            if (failedCleanup)
            {
                Assert.ThrowsExactly<IOException>(() => owner.Delete(CancellationToken.None));
                Assert.AreEqual(1, removalAttempts, "A real cleanup attempt must precede writer revocation.");
            }
            else owner.Dispose();
            await Assert.ThrowsExactlyAsync<IOException>(() => owner.WriteFileAsync("late", "late bytes"u8.ToArray(), CancellationToken.None));
            Assert.IsFalse(File.Exists(Path.Combine(fixture, "issued", "late")));
            Assert.AreEqual("original bytes", File.ReadAllText(Path.Combine(fixture, "issued", "original")));
            if (failedCleanup)
            {
                var result = owner.Delete(CancellationToken.None);
                Assert.AreEqual(OperatingSystem.IsWindows() ? PhysicalDirectoryCleanupOutcome.Removed : PhysicalDirectoryCleanupOutcome.Retained, result);
                Assert.AreEqual(2, removalAttempts, "Cleanup retry retains its distinct authority after writer revocation.");
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Revoked writer regression and cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => owner?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
        }
    }

    internal sealed class ForwardScope(PhysicalDirectoryScope inner, Action? checkedRelease, bool invokeOnDispose, Action? beforeFileRemoval, Action? afterDirectoryAcquired, Action<string, string>? afterChildOpened) : PhysicalDirectoryScope(inner.Path)
    {
        private bool _disposed;
        internal override PhysicalDirectoryEntryKind? GetEntryKind(string name, CancellationToken ct) => inner.GetEntryKind(name, ct);
        internal override DateTime? ReadFileLastWriteTimeUtcIfPresent(string name, CancellationToken ct) => inner.ReadFileLastWriteTimeUtcIfPresent(name, ct);
        internal override IReadOnlyList<string> Entries() => inner.Entries();
        internal override PhysicalDirectoryScope? OpenChild(string name)
        {
            var result = inner.OpenChild(name);
            if (result is null) return null;
            try
            {
                afterChildOpened?.Invoke(inner.Path, name);
                return new ForwardScope(result, null, false, beforeFileRemoval, afterDirectoryAcquired, afterChildOpened);
            }
            catch (Exception failure)
            {
                OwnedResourceCleanup.Dispose("Forwarded child acquisition callback failed.", failure, result);
                throw;
            }
        }
        internal override PhysicalDirectoryScope CreateOwnedChildScope(string name)
            => new ForwardScope(inner.CreateOwnedChildScope(name), null, false, beforeFileRemoval, afterDirectoryAcquired, afterChildOpened);
        internal override bool SameIdentity(PhysicalDirectoryScope other) => inner.SameIdentity(other is ForwardScope forwarded ? forwarded.Inner : other);
        private PhysicalDirectoryScope Inner => inner;
        internal override bool IsLink(string name) => inner.IsLink(name);
        internal override IDisposable? LockOwner(string name) => inner.LockOwner(name);
        internal override DateTime LastActivityUtc => inner.LastActivityUtc;
        internal override bool TryDeleteFile(string name, CancellationToken ct)
        {
            beforeFileRemoval?.Invoke();
            return inner.TryDeleteFile(name, ct);
        }
        internal override PhysicalDirectoryScope? OpenDirectoryForAccess(string name, bool create, PhysicalDirectoryCreationMode creationMode, CancellationToken ct)
        {
            var acquired = inner.OpenDirectoryForAccess(name, create, creationMode, ct);
            if (acquired is null) return null;
            try
            {
                afterDirectoryAcquired?.Invoke();
                return new ForwardScope(acquired, null, false, beforeFileRemoval, afterDirectoryAcquired, afterChildOpened);
            }
            catch (Exception failure)
            {
                OwnedResourceCleanup.Dispose("Forwarded directory acquisition callback failed.", failure, acquired);
                throw;
            }
        }
        internal override Microsoft.Win32.SafeHandles.SafeFileHandle CreateFreshFile(string name, CancellationToken ct)
            => inner.CreateFreshFile(name, ct);
        internal override Microsoft.Win32.SafeHandles.SafeFileHandle? OpenReadFileIfPresent(string name, CancellationToken ct)
            => inner.OpenReadFileIfPresent(name, ct);
        internal override Microsoft.Win32.SafeHandles.SafeFileHandle? OpenTransitionFileIfPresent(string name, bool writable, CancellationToken ct)
            => inner.OpenTransitionFileIfPresent(name, writable, ct);
        internal override bool SameFilesystem(PhysicalDirectoryScope other) => inner.SameFilesystem(other is ForwardScope wrapper ? wrapper.Inner : other);
        internal override Microsoft.Win32.SafeHandles.SafeFileHandle CreateTransitionFile(string name, CancellationToken ct)
            => inner.CreateTransitionFile(name, ct);
        internal override bool CurrentFileMatches(string name, Microsoft.Win32.SafeHandles.SafeFileHandle witness)
            => inner.CurrentFileMatches(name, witness);
        internal override void MoveFileNoReplace(string name, Microsoft.Win32.SafeHandles.SafeFileHandle? witness,
            PhysicalDirectoryScope destination, string destinationName, CancellationToken ct)
            => inner.MoveFileNoReplace(name, witness, destination is ForwardScope forwarded ? forwarded.Inner : destination, destinationName, ct);
        internal override void ReclaimAdmittedTransitionFile(string name, Microsoft.Win32.SafeHandles.SafeFileHandle witness, CancellationToken ct)
            => inner.ReclaimAdmittedTransitionFile(name, witness, ct);
        internal override UnixFileMode? ReadFileModeIfPresent(string name, CancellationToken ct)
            => inner.ReadFileModeIfPresent(name, ct);
        internal override void PublishPreparedFile(string temporary, Microsoft.Win32.SafeHandles.SafeFileHandle preparedWitness, string name, Microsoft.Win32.SafeHandles.SafeFileHandle? replacedWitness, CancellationToken ct)
            => inner.PublishPreparedFile(temporary, preparedWitness, name, replacedWitness, ct);
        internal override void ReleaseNamespacePin() => inner.ReleaseNamespacePin();
        internal override PhysicalDirectoryCleanupOutcome RemoveHeldChild(string name, PhysicalDirectoryScope child, CancellationToken ct)
        {
            // Release the original namespace pin, then actually replace its name
            // BEFORE the production primitive acquires/checks its deletion handle.
            child.ReleaseNamespacePin();
            checkedRelease?.Invoke();
            return inner.RemoveHeldChild(name, child is ForwardScope forwarded ? forwarded.Inner : child, ct);
        }
        public override void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            inner.Dispose();
            if (invokeOnDispose) checkedRelease?.Invoke();
        }
    }
}
