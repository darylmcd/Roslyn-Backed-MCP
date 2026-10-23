using Microsoft.Win32.SafeHandles;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PhysicalSourceTransitionTests
{
    [TestMethod]
    [DataRow(-1, 0)]
    [DataRow(-1, 9)]
    [DataRow(0, -1)]
    [DataRow(0, 9)]
    [DataRow(9, -1)]
    [DataRow(9, 0)]
    [DataRow(9, 19)]
    [DataRow(9, 9)]
    [DataRow(-1, -1)]
    public Task ExactSource_TransitionsMissingEmptyAndExisting(int beforeLength, int afterLength)
        => WithFixtureAsync(async fixture =>
        {
            var before = Bytes(beforeLength);
            var after = Bytes(afterLength);
            if (before is not null) await File.WriteAllBytesAsync(fixture.Source, before);
            await fixture.ApplyAsync(before, after, PhysicalSourceTransitionMode.FreshReplacement);
            AssertBytes(fixture.Source, after);
            Assert.HasCount(0, fixture.Retained);
            AssertNoCustodyContent(fixture.Root);
            CollectionAssert.AreEquivalent(Directory.GetDirectories(fixture.Root, ".rmcp-transition-*"), fixture.RetainedNamespaces,
                "Every retained empty namespace must be explicitly reported to its operation owner.");
        });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task AuthorizationMutation_IsRefusedBeforeCapturingExternalBytes(bool delete)
        => WithFixtureAsync(async fixture =>
        {
            var before = Bytes(9)!;
            var external = Bytes(27)!;
            await File.WriteAllBytesAsync(fixture.Source, before);
            var authorized = false;
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => fixture.ApplyAsync(before, delete ? null : Bytes(19), PhysicalSourceTransitionMode.FreshReplacement,
                (paths, ct) =>
                {
                    Assert.IsTrue(paths.Contains(fixture.Source, FileSystemPath.Comparer));
                    File.WriteAllBytes(fixture.Source, external);
                    authorized = true;
                    return Task.CompletedTask;
                }));
            Assert.IsTrue(authorized);
            AssertBytes(fixture.Source, external);
            Assert.HasCount(0, fixture.Retained);
            Assert.HasCount(0, Directory.GetDirectories(fixture.Root));
        });

    [TestMethod]
    public Task DeniedAuthorization_DoesNotAcquireOrCreateNamespace()
        => WithFixtureAsync(async fixture =>
        {
            var acquired = false;
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => PhysicalSourceTransition.ApplyAsync(
                fixture.Source, null, Bytes(9), PhysicalSourceTransitionMode.FreshReplacement,
                (parent, leaf, temporary, ct) => fixture.AcquireCustodyAsync(parent, leaf, temporary, Authorize, ct),
                (_, _) => throw new UnauthorizedAccessException("Denied fixture grant."),
                fixture.Retained.Add, fixture.RetainedNamespaces.Add, (path, ct) => { acquired = true; return PhysicalSourceTransition.OpenParent(path, ct); }, null, CancellationToken.None));
            Assert.IsFalse(acquired);
            Assert.HasCount(0, Directory.GetFileSystemEntries(fixture.Root));
            Assert.HasCount(0, fixture.Retained);
        });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task CancellationAfterDisplacement_RestoresCapturedBytesBeforeThrowing(bool preserve)
        => WithFixtureAsync(async fixture =>
        {
            var before = Bytes(9)!;
            await File.WriteAllBytesAsync(fixture.Source, before);
            using var cancellation = new CancellationTokenSource();
            var displaced = false;
            fixture.AfterMatch = name =>
            {
                if (name != "captured" || displaced) return;
                Assert.IsFalse(File.Exists(fixture.Source), "Cancellation runs after the native source transfer.");
                displaced = true;
                cancellation.Cancel();
            };
            var failure = await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.ApplyAsync(before, Bytes(19), preserve ? PhysicalSourceTransitionMode.PreserveExistingObject : PhysicalSourceTransitionMode.FreshReplacement, ct: cancellation.Token));
            Assert.AreEqual(cancellation.Token, failure.CancellationToken);
            Assert.IsTrue(displaced);
            AssertBytes(fixture.Source, before);
            Assert.HasCount(0, fixture.Retained);
            AssertNoCustodyContent(fixture.Root);
        });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task LateSourceOccupant_IsPreservedAndRecoveryRequiresCurrentGrant(bool preserve)
        => WithFixtureAsync(async fixture =>
        {
            var before = Bytes(9)!;
            var external = Bytes(27)!;
            var receipt = await fixture.RetainLateSourceOccupantAsync(before, external, Bytes(19)!,
                preserve ? PhysicalSourceTransitionMode.PreserveExistingObject : PhysicalSourceTransitionMode.FreshReplacement);
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => receipt.ResolveAsync(
                (_, _) => throw new UnauthorizedAccessException("Recovery grant revoked."), CancellationToken.None));
            AssertBytes(fixture.Source, external);
            AssertBytes(Path.Combine(receipt.CustodyPath, "captured"), before);
            File.Delete(fixture.Source); // Explicit fixture conflict resolution, not product overwrite authority.
            await receipt.ResolveAsync(Authorize, CancellationToken.None);
            fixture.Retained.Remove(receipt);
            AssertBytes(fixture.Source, before);
            AssertNoCustodyContent(fixture.Root);
        });

    [TestMethod]
    [DataRow("different-source-witness")]
    [DataRow("different-leaf")]
    [DataRow("nonempty-leaf")]
    [DataRow("rebind")]
    public Task CustodyLeafAdmission_RequiresExactSourceAndEmptyIssuedLeafOnce(string shape)
        => WithFixtureAsync(fixture =>
        {
            using var source = PhysicalDirectoryScope.OpenRoute(fixture.Root);
            using var otherSource = PhysicalDirectoryScope.OpenRoute(fixture.Root);
            var parent = PhysicalDirectoryScope.OpenRoute(fixture.Root);
            const string issued = ".rmcp-transition-issued";
            using var authority = new PhysicalSourceCustody(source, parent, issued,
                source.AssertCurrentPath, source.AssertCurrentPath, Authorize);
            using var leaf = parent.CreateOwnedChildScope(shape == "different-leaf" ? ".rmcp-transition-other" : issued);
            var content = Path.Combine(leaf.Path, "unadmitted.bin");
            if (shape == "nonempty-leaf") File.WriteAllBytes(content, Bytes(27)!);
            if (shape == "rebind") authority.BindLeaf(source, leaf);

            Assert.ThrowsExactly<IOException>(() => authority.BindLeaf(shape == "different-source-witness" ? otherSource : source, leaf));
            authority.AssertCurrent();
            if (shape == "nonempty-leaf") AssertBytes(content, Bytes(27));
            return Task.CompletedTask;
        });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task UnixRecoveryGrant_ReplacedCustodyLeafOrSourceParentRefusesBeforeEffects(bool replaceSourceParent)
        => WithFixtureAsync(async fixture =>
        {
            if (OperatingSystem.IsWindows()) { Assert.Inconclusive("POSIX current-name replacement under held directory descriptors."); return; }
            fixture.SourceDirectory = Path.Combine(fixture.Root, "source-parent");
            Directory.CreateDirectory(fixture.SourceDirectory);
            var before = Bytes(9)!;
            var unrelated = Bytes(35)!;
            var receipt = await fixture.RetainLateSourceOccupantAsync(before, Bytes(27)!, Bytes(19)!, PhysicalSourceTransitionMode.FreshReplacement);
            File.Delete(fixture.Source); // Explicitly resolve only the fixture-created late source blocker.
            var replaced = replaceSourceParent ? fixture.SourceDirectory : receipt.CustodyPath;
            var relocated = replaced + "-relocated";
            var replacementFile = Path.Combine(replaced, replaceSourceParent ? "source.bin" : "captured");
            var injected = false;
            var moved = false;
            var replacementCreated = false;
            Exception? primary = null;
            try
            {
                Assert.IsTrue(FileSystemPath.IsStrictDescendant(fixture.Root, replaced));
                Assert.IsTrue(FileSystemPath.IsStrictDescendant(fixture.Root, relocated));
                Exception? refusal = null;
                try
                {
                    await receipt.ResolveAsync(async (paths, ct) =>
                    {
                        await Authorize(paths, ct);
                        Assert.IsFalse(injected, "The replacement must occur at the actual recovery grant boundary once.");
                        injected = true;
                        Directory.Move(replaced, relocated);
                        moved = true;
                        Directory.CreateDirectory(replaced);
                        replacementCreated = true;
                        await File.WriteAllBytesAsync(replacementFile, unrelated, ct);
                    }, CancellationToken.None);
                }
                catch (Exception failure) { refusal = failure; }
                Assert.IsTrue(injected);
                AssertBytes(replacementFile, unrelated);
                AssertBytes(Path.Combine(replaceSourceParent ? receipt.CustodyPath : relocated, "captured"), before);
                Assert.IsFalse(File.Exists(replaceSourceParent ? Path.Combine(relocated, "source.bin") : fixture.Source),
                    "Recovery must not move the captured source through a stale directory coordinate.");
                Assert.IsInstanceOfType<IOException>(refusal);
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Recovery replacement fixture cleanup failed.", primary,
                    async () =>
                    {
                        if (replacementCreated)
                        {
                            AssertBytes(replacementFile, unrelated);
                            File.Delete(replacementFile);
                            Assert.HasCount(0, Directory.GetFileSystemEntries(replaced));
                            Directory.Delete(replaced, recursive: false);
                        }
                        if (moved) Directory.Move(relocated, replaced);
                        if (receipt.RequiresRecovery) await receipt.ResolveAsync(Authorize, CancellationToken.None);
                        fixture.Retained.Remove(receipt);
                        AssertBytes(fixture.Source, before);
                    });
            }
        });

    [TestMethod]
    public Task UnixForwardFinalCustodyGrant_ReplacedSourceParentRefusesBeforeEffects()
        => WithFixtureAsync(async fixture =>
        {
            if (OperatingSystem.IsWindows()) { Assert.Inconclusive("POSIX current-name replacement under a held source parent."); return; }
            fixture.SourceDirectory = Path.Combine(fixture.Root, "source-parent");
            Directory.CreateDirectory(fixture.SourceDirectory);
            var before = Bytes(9)!;
            var unrelated = Bytes(35)!;
            await File.WriteAllBytesAsync(fixture.Source, before);
            var relocated = fixture.SourceDirectory + "-relocated";
            var armed = false;
            var injected = false;
            var moved = false;
            var replacementCreated = false;
            Exception? primary = null;
            try
            {
                Assert.IsTrue(FileSystemPath.IsStrictDescendant(fixture.Root, relocated));
                Exception? refusal = null;
                try
                {
                    await fixture.ApplyAsync(before, Bytes(19), PhysicalSourceTransitionMode.FreshReplacement, async (paths, ct) =>
                    {
                        await Authorize(paths, ct);
                        if (paths.Any(path => Path.GetFileName(path).StartsWith(".rmcp-transition-", StringComparison.Ordinal)))
                        { armed = true; return; }
                        if (!armed || injected) return;
                        injected = true;
                        Directory.Move(fixture.SourceDirectory, relocated);
                        moved = true;
                        Directory.CreateDirectory(fixture.SourceDirectory);
                        replacementCreated = true;
                        await File.WriteAllBytesAsync(fixture.Source, unrelated, ct);
                    });
                }
                catch (Exception failure) { refusal = failure; }
                Assert.IsTrue(injected, "Mutation must occur in the final custody receipt's source authorization callback.");
                AssertBytes(fixture.Source, unrelated);
                AssertBytes(Path.Combine(relocated, "source.bin"), before);
                Assert.HasCount(0, fixture.Retained);
                Assert.IsInstanceOfType<IOException>(refusal);
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Forward source-parent replacement fixture cleanup failed.", primary,
                    CleanupFailureCollector.FromAction(() =>
                    {
                        if (replacementCreated)
                        {
                            AssertBytes(fixture.Source, unrelated);
                            File.Delete(fixture.Source);
                            Assert.HasCount(0, Directory.GetFileSystemEntries(fixture.SourceDirectory));
                            Directory.Delete(fixture.SourceDirectory, recursive: false);
                        }
                        if (moved) Directory.Move(relocated, fixture.SourceDirectory);
                    }));
            }
        });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task UnixSubstitutionAtNativeHandoff_RestoresOrRetainsActualUnexpectedObject(bool occupySource)
        => WithFixtureAsync(async fixture =>
        {
            if (OperatingSystem.IsWindows()) Assert.Inconclusive("Windows excludes source substitution with its held DELETE/share-read handle.");
            var before = Bytes(9)!;
            var external = Bytes(27)!;
            var late = Bytes(35)!;
            var originalMoved = Path.Combine(fixture.Root, "original-moved.bin");
            await File.WriteAllBytesAsync(fixture.Source, before);
            var substituted = false;
            fixture.BeforeMove = name =>
            {
                if (name != "source.bin" || substituted) return;
                File.Move(fixture.Source, originalMoved);
                File.WriteAllBytes(fixture.Source, external);
                substituted = true;
            };
            var occupied = false;
            fixture.AfterMatch = name =>
            {
                if (name != "captured" || !occupySource || occupied) return;
                File.WriteAllBytes(fixture.Source, late);
                occupied = true;
            };
            if (occupySource)
            {
                await Assert.ThrowsExactlyAsync<AggregateException>(() => fixture.ApplyAsync(before, Bytes(19), PhysicalSourceTransitionMode.FreshReplacement));
                Assert.HasCount(1, fixture.Retained);
                AssertBytes(fixture.Source, late);
                AssertBytes(Path.Combine(fixture.Retained[0].CustodyPath, "captured"), external);
                File.Delete(fixture.Source);
                await fixture.Retained[0].ResolveAsync(Authorize, CancellationToken.None);
                fixture.Retained.Clear();
            }
            else
            {
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => fixture.ApplyAsync(before, Bytes(19), PhysicalSourceTransitionMode.FreshReplacement));
                Assert.HasCount(0, fixture.Retained);
            }
            Assert.IsTrue(substituted);
            AssertBytes(fixture.Source, external);
            AssertBytes(originalMoved, before);
            AssertNoCustodyContent(fixture.Root);
        });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task UnixSameInodeWriteBeforeHandoff_RestoresOrRetainsExternalBytes(bool occupySource)
        => WithFixtureAsync(async fixture =>
        {
            if (OperatingSystem.IsWindows()) Assert.Inconclusive("Windows excludes in-place writers with its held DELETE/share-read source handle.");
            var before = Bytes(9)!;
            var external = Bytes(27)!;
            var late = Bytes(35)!;
            await File.WriteAllBytesAsync(fixture.Source, before);
            using var original = File.OpenHandle(fixture.Source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var changed = false;
            fixture.BeforeMove = name =>
            {
                if (name != "source.bin" || changed) return;
                File.WriteAllBytes(fixture.Source, external);
                using var current = File.OpenHandle(fixture.Source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                Assert.IsTrue(PhysicalDirectoryScope.SameFile(original, current),
                    "The adversarial write must change the still-held original inode, not replace its name.");
                changed = true;
            };
            var occupied = false;
            fixture.AfterMatch = name =>
            {
                if (name != "captured" || !occupySource || occupied) return;
                Assert.IsFalse(File.Exists(fixture.Source));
                File.WriteAllBytes(fixture.Source, late);
                occupied = true;
            };
            if (occupySource)
            {
                await Assert.ThrowsExactlyAsync<AggregateException>(() => fixture.ApplyAsync(before, Bytes(19), PhysicalSourceTransitionMode.FreshReplacement));
                Assert.HasCount(1, fixture.Retained);
                Assert.IsTrue(occupied);
                AssertBytes(fixture.Source, late);
                AssertBytes(Path.Combine(fixture.Retained[0].CustodyPath, "captured"), external);
                File.Delete(fixture.Source);
                await fixture.Retained[0].ResolveAsync(Authorize, CancellationToken.None);
                fixture.Retained.Clear();
            }
            else
            {
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => fixture.ApplyAsync(before, Bytes(19), PhysicalSourceTransitionMode.FreshReplacement));
                Assert.HasCount(0, fixture.Retained);
            }
            Assert.IsTrue(changed);
            AssertBytes(fixture.Source, external);
            using var restored = File.OpenHandle(fixture.Source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Assert.IsTrue(PhysicalDirectoryScope.SameFile(original, restored), "Recovery must return the captured original object.");
            AssertNoCustodyContent(fixture.Root);
        });

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public Task WindowsReadOnlyHardlink_RefusesSourceMutationAndPreservesBothNames(bool delete, bool preserve)
        => WithFixtureAsync(async fixture =>
        {
            if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows read-only object-bound disposition contract.");
            var external = Path.Combine(fixture.Root, "external.bin");
            var before = Bytes(9)!;
            await File.WriteAllBytesAsync(external, before);
            PhysicalFileLinks.CreateHardLink(fixture.Source, external);
            var attributes = File.GetAttributes(external);
            Exception? primary = null;
            try
            {
                File.SetAttributes(external, attributes | FileAttributes.ReadOnly);
                await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => fixture.ApplyAsync(before, delete ? null : Bytes(19),
                    preserve ? PhysicalSourceTransitionMode.PreserveExistingObject : PhysicalSourceTransitionMode.FreshReplacement));
                AssertBytes(fixture.Source, before);
                AssertBytes(external, before);
                using var held = File.OpenHandle(external, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                Assert.IsTrue((File.GetAttributes(held) & FileAttributes.ReadOnly) != 0,
                    "Refusing a source mutation must preserve attributes on the surviving file object.");
                Assert.HasCount(0, fixture.Retained);
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Read-only hardlink attributes cleanup failed.", primary,
                    CleanupFailureCollector.FromAction(() => File.SetAttributes(external, attributes)));
            }
        });

    [TestMethod]
    public Task WindowsHeldSource_RejectsRenameAtFinalNativeHandoff()
        => WithFixtureAsync(async fixture =>
        {
            if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows mandatory source sharing exclusion.");
            var before = Bytes(9)!;
            await File.WriteAllBytesAsync(fixture.Source, before);
            var moved = Path.Combine(fixture.Root, "forbidden-move.bin");
            var attempted = false;
            fixture.BeforeMove = name =>
            {
                if (name != "source.bin" || attempted) return;
                attempted = true;
                Assert.ThrowsExactly<IOException>(() => File.Move(fixture.Source, moved));
                AssertBytes(fixture.Source, before);
            };
            await fixture.ApplyAsync(before, Bytes(19), PhysicalSourceTransitionMode.FreshReplacement);
            Assert.IsTrue(attempted);
            Assert.IsFalse(File.Exists(moved));
            AssertBytes(fixture.Source, Bytes(19));
            Assert.HasCount(0, fixture.Retained);
        });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task FreshReplacement_WritableHardlinkLeavesOtherNameUnchanged(bool delete)
        => WithFixtureAsync(async fixture =>
        {
            var other = Path.Combine(fixture.Root, "other.bin");
            var before = Bytes(9)!;
            await File.WriteAllBytesAsync(other, before);
            PhysicalFileLinks.CreateHardLink(fixture.Source, other);
            await fixture.ApplyAsync(before, delete ? null : Bytes(19), PhysicalSourceTransitionMode.FreshReplacement);
            AssertBytes(fixture.Source, delete ? null : Bytes(19));
            AssertBytes(other, before);
            Assert.HasCount(0, fixture.Retained);
        });

    [TestMethod]
    public Task PreserveExistingObject_WindowsRetainsAlternateStreamAndUpdatesHardlink()
        => WithFixtureAsync(async fixture =>
        {
            if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Windows alternate data stream contract."); return; }
            var before = Bytes(9)!;
            var desired = Bytes(19)!;
            var other = Path.Combine(fixture.Root, "other.bin");
            await File.WriteAllBytesAsync(fixture.Source, before);
            await File.WriteAllTextAsync(fixture.Source + ":retained-stream", "owned alternate stream");
            PhysicalFileLinks.CreateHardLink(other, fixture.Source);
            using var original = File.OpenHandle(other, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            await fixture.ApplyAsync(before, desired, PhysicalSourceTransitionMode.PreserveExistingObject);
            AssertBytes(fixture.Source, desired);
            AssertBytes(other, desired);
            Assert.AreEqual("owned alternate stream", await File.ReadAllTextAsync(fixture.Source + ":retained-stream"));
            using var current = File.OpenHandle(fixture.Source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Assert.IsTrue(PhysicalDirectoryScope.SameFile(original, current), "Preserve mode must republish the same file object.");
            Assert.HasCount(0, fixture.Retained);
        });

    [TestMethod]
    public Task PreserveExistingObject_UnixRetainsExecutableModeAndUpdatesHardlink()
        => WithFixtureAsync(async fixture =>
        {
            if (OperatingSystem.IsWindows()) { Assert.Inconclusive("Unix file mode contract."); return; }
            var before = Bytes(9)!;
            var desired = Bytes(19)!;
            var other = Path.Combine(fixture.Root, "other.bin");
            await File.WriteAllBytesAsync(fixture.Source, before);
            const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute;
            File.SetUnixFileMode(fixture.Source, mode);
            PhysicalFileLinks.CreateHardLink(other, fixture.Source);
            using var original = File.OpenHandle(other, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            await fixture.ApplyAsync(before, desired, PhysicalSourceTransitionMode.PreserveExistingObject);
            AssertBytes(fixture.Source, desired);
            AssertBytes(other, desired);
            Assert.AreEqual(mode, File.GetUnixFileMode(fixture.Source));
            Assert.AreEqual(mode, File.GetUnixFileMode(other));
            using var current = File.OpenHandle(fixture.Source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Assert.IsTrue(PhysicalDirectoryScope.SameFile(original, current));
            Assert.HasCount(0, fixture.Retained);
        });

    [TestMethod]
    public Task PreserveExistingObject_CancelAfterWritingRestoresOriginalBytesOnSameObject()
        => WithFixtureAsync(async fixture =>
        {
            var before = Bytes(9)!;
            var desired = Bytes(19)!;
            await File.WriteAllBytesAsync(fixture.Source, before);
            using var original = File.OpenHandle(fixture.Source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var cancellation = new CancellationTokenSource();
            var mutated = false;
            fixture.BeforeMove = name =>
            {
                if (name != "captured" || mutated) return;
                var custody = Directory.GetDirectories(fixture.Root, ".rmcp-transition-*").Single();
                AssertBytes(Path.Combine(custody, "captured"), desired);
                Assert.IsFalse(File.Exists(fixture.Source), "The changed object is still in custody before republication.");
                mutated = true;
                cancellation.Cancel();
            };
            var failure = await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.ApplyAsync(before, desired,
                PhysicalSourceTransitionMode.PreserveExistingObject, ct: cancellation.Token));
            Assert.AreEqual(cancellation.Token, failure.CancellationToken);
            Assert.IsTrue(mutated, "Cancellation must occur after desired bytes were actually written.");
            AssertBytes(fixture.Source, before);
            using var restored = File.OpenHandle(fixture.Source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Assert.IsTrue(PhysicalDirectoryScope.SameFile(original, restored));
            Assert.HasCount(0, fixture.Retained);
            AssertNoCustodyContent(fixture.Root);
        });

    [TestMethod]
    public Task MissingSourceParents_UseOrdinaryDirectoryCreationMode()
        => WithFixtureAsync(async fixture =>
        {
            if (OperatingSystem.IsWindows()) { Assert.Inconclusive("Unix source-directory mode compared with the platform creation oracle."); return; }
            var oracle = Directory.CreateDirectory(Path.Combine(fixture.Root, "oracle"));
            var parent = Path.Combine(fixture.Root, "new", "nested");
            fixture.SourceDirectory = parent;
            var target = Path.Combine(parent, "source.bin");
            await fixture.ApplyAsync(null, Bytes(9), PhysicalSourceTransitionMode.FreshReplacement);
            AssertBytes(target, Bytes(9));
            Assert.IsTrue(fixture.RetainedNamespaces.Contains(parent, FileSystemPath.Comparer),
                "Creating source parents must record an unverified namespace effect for native graph validation.");
            Assert.AreEqual(File.GetUnixFileMode(oracle.FullName), File.GetUnixFileMode(parent));
            Assert.AreEqual(File.GetUnixFileMode(oracle.FullName), File.GetUnixFileMode(Path.GetDirectoryName(parent)!));
            Assert.HasCount(0, fixture.Retained);
        });

    [TestMethod]
    public Task FailureAfterExclusivePreparedCreation_CleansReceiptAndPreservesPrimaryFailure()
        => WithFixtureAsync(async fixture =>
        {
            var before = Bytes(9)!;
            await File.WriteAllBytesAsync(fixture.Source, before);
            var injected = new IOException("Fixture failure after exclusive preparation acquisition.");
            var observedCreation = false;
            fixture.BeforeMove = name =>
            {
                if (!name.StartsWith(".rmcp-prepared-", StringComparison.Ordinal) || observedCreation) return;
                AssertBytes(Path.Combine(fixture.Root, name), []);
                observedCreation = true;
                throw injected;
            };
            var failure = await Assert.ThrowsExactlyAsync<IOException>(() => fixture.ApplyAsync(before, Bytes(19), PhysicalSourceTransitionMode.FreshReplacement));
            Assert.AreSame(injected, failure);
            Assert.IsTrue(observedCreation);
            AssertBytes(fixture.Source, before);
            Assert.HasCount(0, fixture.Retained);
            Assert.HasCount(0, Directory.GetFiles(fixture.Root, ".rmcp-prepared-*"));
            AssertNoCustodyContent(fixture.Root);
        });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task UnixPreparedSubstitution_RestoresUnknownObjectOrRetainsOccupiedOriginalName(bool occupyTemporary)
        => WithFixtureAsync(async fixture =>
        {
            if (OperatingSystem.IsWindows()) { Assert.Inconclusive("Windows prevents prepared-name substitution with its held DELETE handle."); return; }
            var before = Bytes(9)!;
            var external = Bytes(27)!;
            var late = Bytes(35)!;
            await File.WriteAllBytesAsync(fixture.Source, before);
            string? temporary = null;
            var movedOriginal = Path.Combine(fixture.Root, "original-empty-prepared.bin");
            fixture.BeforeMove = name =>
            {
                if (!name.StartsWith(".rmcp-prepared-", StringComparison.Ordinal) || temporary is not null) return;
                temporary = Path.Combine(fixture.Root, name);
                File.Move(temporary, movedOriginal);
                File.WriteAllBytes(temporary, external);
            };
            var occupied = false;
            fixture.AfterMatch = name =>
            {
                if (name != "prepared" || !occupyTemporary || occupied) return;
                Assert.IsNotNull(temporary);
                File.WriteAllBytes(temporary, late);
                occupied = true;
            };
            if (occupyTemporary)
            {
                await Assert.ThrowsExactlyAsync<AggregateException>(() => fixture.ApplyAsync(before, Bytes(19), PhysicalSourceTransitionMode.FreshReplacement));
                Assert.HasCount(1, fixture.Retained);
                var receipt = fixture.Retained[0];
                Assert.IsNotNull(temporary);
                AssertBytes(temporary, late);
                AssertBytes(Path.Combine(receipt.CustodyPath, "prepared"), external);
                var denied = false;
                await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => receipt.ResolveAsync((paths, _) =>
                {
                    Assert.IsTrue(paths.Contains(temporary, FileSystemPath.Comparer), "Recovery must reacquire the exact prepared-name publication grant.");
                    denied = true;
                    throw new UnauthorizedAccessException("Original prepared-name grant revoked.");
                }, CancellationToken.None));
                Assert.IsTrue(denied);
                AssertBytes(temporary, late);
                File.Delete(temporary);
                await receipt.ResolveAsync(Authorize, CancellationToken.None);
                fixture.Retained.Remove(receipt);
            }
            else
            {
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => fixture.ApplyAsync(before, Bytes(19), PhysicalSourceTransitionMode.FreshReplacement));
                Assert.HasCount(0, fixture.Retained);
            }
            Assert.IsNotNull(temporary);
            AssertBytes(temporary, external);
            AssertBytes(movedOriginal, []);
            AssertBytes(fixture.Source, before);
            AssertNoCustodyContent(fixture.Root);
        });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task UnixPostPublicationSubstitution_DoesNotOverwriteActorOrLosePublishedObject(bool preserve)
        => WithFixtureAsync(async fixture =>
        {
            if (OperatingSystem.IsWindows()) { Assert.Inconclusive("Windows held publication handle prevents namespace substitution."); return; }
            var before = Bytes(9)!;
            var desired = Bytes(19)!;
            var external = Bytes(27)!;
            var movedPublished = Path.Combine(fixture.Root, "published-moved.bin");
            await File.WriteAllBytesAsync(fixture.Source, before);
            var replaced = false;
            fixture.BeforeMatch = name =>
            {
                if (name != "source.bin" || replaced) return;
                File.Move(fixture.Source, movedPublished);
                File.WriteAllBytes(fixture.Source, external);
                replaced = true;
            };
            if (preserve)
            {
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => fixture.ApplyAsync(before, desired, PhysicalSourceTransitionMode.PreserveExistingObject));
                Assert.HasCount(0, fixture.Retained);
            }
            else
            {
                await Assert.ThrowsExactlyAsync<AggregateException>(() => fixture.ApplyAsync(before, desired, PhysicalSourceTransitionMode.FreshReplacement));
                Assert.HasCount(1, fixture.Retained);
                AssertBytes(Path.Combine(fixture.Retained[0].CustodyPath, "captured"), before);
            }
            Assert.IsTrue(replaced);
            AssertBytes(fixture.Source, external);
            AssertBytes(movedPublished, desired);
        });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task SameFilesystemCustody_IsRelocatedWithoutAddingSourceSiblingNamespaces(bool preserve)
        => WithFixtureAsync(async fixture =>
        {
            var staging = Path.Combine(fixture.Root, "existing-staging");
            Directory.CreateDirectory(staging);
            fixture.PreferredCustodyParent = staging;
            using (var sourceParent = PhysicalDirectoryScope.OpenRoute(fixture.Root))
            using (var stagingParent = PhysicalDirectoryScope.OpenRoute(staging))
            {
                Assert.IsTrue(sourceParent.SameFilesystem(stagingParent), "Placement must be justified by held native filesystem identities.");
                Assert.IsFalse(sourceParent.SameIdentity(stagingParent), "The selected custody parent must be a distinct directory object.");
            }
            var before = Bytes(9)!;
            var desired = Bytes(19)!;
            await File.WriteAllBytesAsync(fixture.Source, before);
            var observedCustody = false;
            fixture.AfterMatch = name =>
            {
                if (name != "captured" || observedCustody) return;
                var child = Directory.GetDirectories(staging, ".rmcp-transition-*").Single();
                AssertBytes(Path.Combine(child, "captured"), before);
                Assert.HasCount(0, Directory.GetDirectories(fixture.Root, ".rmcp-transition-*"));
                observedCustody = true;
            };
            await fixture.ApplyAsync(before, desired, preserve ? PhysicalSourceTransitionMode.PreserveExistingObject : PhysicalSourceTransitionMode.FreshReplacement);
            Assert.IsTrue(observedCustody);
            AssertBytes(fixture.Source, desired);
            Assert.HasCount(0, fixture.Retained);
            Assert.HasCount(0, Directory.GetDirectories(fixture.Root, ".rmcp-transition-*"));
            AssertNoCustodyContent(staging);
            CollectionAssert.AreEquivalent(Directory.GetDirectories(staging, ".rmcp-transition-*"), fixture.RetainedNamespaces);
        });

    [TestMethod]
    public Task UnixRelocatedFreshPreparation_InheritsSourceParentModeInsteadOfCustodyMode()
        => WithFixtureAsync(async fixture =>
        {
            if (OperatingSystem.IsWindows()) { Assert.Inconclusive("Unix fresh-file creation mode contract."); return; }
            var staging = Path.Combine(fixture.Root, "private-staging");
            Directory.CreateDirectory(staging);
            File.SetUnixFileMode(staging, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            fixture.PreferredCustodyParent = staging;
            var oracle = Path.Combine(fixture.Root, "ordinary-created.bin");
            await File.WriteAllBytesAsync(oracle, Bytes(9)!);
            var createdInSourceParent = false;
            fixture.BeforeMove = name =>
            {
                if (!name.StartsWith(".rmcp-prepared-", StringComparison.Ordinal) || createdInSourceParent) return;
                var prepared = Path.Combine(fixture.Root, name);
                AssertBytes(prepared, []);
                Assert.IsFalse(File.Exists(Path.Combine(staging, name)));
                if (OperatingSystem.IsWindows()) throw new AssertFailedException("The Unix mode fixture cannot execute on Windows.");
                Assert.AreEqual(File.GetUnixFileMode(oracle), File.GetUnixFileMode(prepared));
                createdInSourceParent = true;
            };
            await fixture.ApplyAsync(null, Bytes(19), PhysicalSourceTransitionMode.FreshReplacement);
            Assert.IsTrue(createdInSourceParent, "The actual fresh file must acquire source-parent inheritance before custody transfer.");
            AssertBytes(fixture.Source, Bytes(19));
            Assert.AreEqual(File.GetUnixFileMode(oracle), File.GetUnixFileMode(fixture.Source));
            AssertNoCustodyContent(staging);
            Assert.HasCount(0, Directory.GetDirectories(fixture.Root, ".rmcp-transition-*"));
        });

    [TestMethod]
    public Task FailureAfterCustodyCreation_ReportsNamespaceAndPreservesUnadmittedContent()
        => WithFixtureAsync(async fixture =>
        {
            var before = Bytes(9)!;
            await File.WriteAllBytesAsync(fixture.Source, before);
            var injected = new IOException("Fixture custody acquisition failure after native creation.");
            string? created = null;
            fixture.AfterCustodyCreated = path =>
            {
                created = path;
                Assert.IsTrue(Directory.Exists(path), "The namespace effect must exist before acquisition fails.");
                File.WriteAllBytes(Path.Combine(path, "unadmitted.bin"), Bytes(27)!);
                throw injected;
            };
            var failure = await Assert.ThrowsExactlyAsync<IOException>(() => fixture.ApplyAsync(before, Bytes(19), PhysicalSourceTransitionMode.FreshReplacement));
            Assert.AreSame(injected, failure);
            Assert.IsNotNull(created);
            AssertBytes(fixture.Source, before);
            AssertBytes(Path.Combine(created, "unadmitted.bin"), Bytes(27));
            CollectionAssert.AreEqual(new[] { created }, fixture.RetainedNamespaces,
                "A possibly created namespace must be reported without claiming cleanup ownership over its contents.");
            Assert.HasCount(0, fixture.Retained);
            Assert.HasCount(0, Directory.GetFiles(fixture.Root, ".rmcp-prepared-*"));
        });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task LinuxDifferentFilesystemCustody_RefusesBeforeSourceOrCustodyEffects(bool preserve)
        => WithDifferentFilesystemAsync(async (fixture, preferred) =>
        {
            fixture.PreferredCustodyParent = preferred;
            var before = Bytes(9)!;
            var desired = Bytes(19)!;
            await File.WriteAllBytesAsync(fixture.Source, before);
            await Assert.ThrowsExactlyAsync<IOException>(() => fixture.ApplyAsync(before, desired,
                preserve ? PhysicalSourceTransitionMode.PreserveExistingObject : PhysicalSourceTransitionMode.FreshReplacement));
            AssertBytes(fixture.Source, before);
            Assert.HasCount(0, fixture.Retained);
            Assert.HasCount(0, Directory.GetFileSystemEntries(preferred));
            CollectionAssert.AreEqual(new[] { fixture.Source }, Directory.GetFileSystemEntries(fixture.Root));
            Assert.HasCount(0, fixture.RetainedNamespaces);
        });

    [TestMethod]
    public Task LinuxDifferentFilesystemCandidate_DeniedSourceGrantRefusesBeforeAcquisitionOrEffect()
        => WithDifferentFilesystemAsync(async (fixture, preferred) =>
        {
            var before = Bytes(9)!;
            await File.WriteAllBytesAsync(fixture.Source, before);
            var denied = false;
            var acquired = false;
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => PhysicalSourceTransition.ApplyAsync(
                fixture.Source, before, Bytes(19), PhysicalSourceTransitionMode.FreshReplacement,
                (parent, leaf, temporary, ct) => fixture.AcquireCustodyAsync(parent, leaf, temporary, Authorize, ct),
                (paths, _) =>
                {
                    Assert.IsTrue(paths.Contains(fixture.Source, FileSystemPath.Comparer));
                    denied = true;
                    throw new UnauthorizedAccessException("Source filesystem grant denied.");
                }, fixture.Retained.Add, fixture.RetainedNamespaces.Add,
                (path, ct) => { acquired = true; return PhysicalSourceTransition.OpenParent(path, ct); }, null, CancellationToken.None));
            Assert.IsTrue(denied);
            Assert.IsFalse(acquired);
            AssertBytes(fixture.Source, before);
            CollectionAssert.AreEqual(new[] { fixture.Source }, Directory.GetFileSystemEntries(fixture.Root));
            Assert.HasCount(0, Directory.GetFileSystemEntries(preferred));
            Assert.HasCount(0, fixture.Retained);
            Assert.HasCount(0, fixture.RetainedNamespaces);
        });

    private static Task WithDifferentFilesystemAsync(Func<Fixture, string, Task> action)
        => WithFixtureAsync(async fixture =>
        {
            if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("Linux distinct-filesystem qualification uses an independently held /dev/shm fixture."); return; }
            if (!Directory.Exists("/dev/shm")) { Assert.Inconclusive("The independent /dev/shm filesystem is unavailable."); return; }
            var preferred = Path.Combine("/dev/shm", nameof(PhysicalSourceTransitionTests) + "-" + Guid.NewGuid().ToString("N"));
            Exception? primary = null;
            try
            {
                try { Directory.CreateDirectory(preferred); }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
                {
                    Assert.Inconclusive("An owned directory on the independent filesystem could not be provisioned: " + failure.GetType().Name);
                    return;
                }
                using (var source = PhysicalDirectoryScope.OpenRoute(fixture.Root))
                using (var other = PhysicalDirectoryScope.OpenRoute(preferred))
                {
                    if (source.SameFilesystem(other)) { Assert.Inconclusive("The actual fixture paths are on the same native filesystem; cross-filesystem behavior cannot be qualified."); return; }
                    Assert.IsFalse(source.SameFilesystem(other), "The refusal premise requires actual held device identities, never lexical path differences.");
                }
                await action(fixture, preferred);
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Independent filesystem fixture cleanup failed.", primary,
                    CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(preferred)));
            }
        });

    private static byte[]? Bytes(int length) => length < 0 ? null : Enumerable.Range(0, length).Select(index => (byte)(index * 13 + length)).ToArray();
    private static Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        foreach (var path in paths) RoslynMcp.Roslyn.Services.WorkspaceManager.EnsurePhysicalPath(path);
        return Task.CompletedTask;
    }
    private static void AssertBytes(string path, byte[]? expected)
    {
        if (expected is null) { Assert.IsFalse(File.Exists(path)); return; }
        Assert.IsTrue(File.Exists(path));
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        CollectionAssert.AreEqual(expected, StreamContentBytes.ReadAll(stream));
    }
    private static void AssertNoCustodyContent(string root)
    {
        foreach (var path in Directory.GetDirectories(root, ".rmcp-transition-*"))
            Assert.HasCount(0, Directory.GetFileSystemEntries(path), "Only the approved empty POSIX directory skeleton may remain.");
    }
    private static async Task WithFixtureAsync(Func<Fixture, Task> action)
    {
        var path = Path.Combine(TestTempRoot.Current, nameof(PhysicalSourceTransitionTests), Guid.NewGuid().ToString("N"));
        Fixture? fixture = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(path);
            fixture = new Fixture(PhysicalPathResolver.Resolve(path));
            await action(fixture);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            List<Func<ValueTask>> cleanup = fixture is null ? []
                : fixture.Retained.Select(receipt => CleanupFailureCollector.FromAction(receipt.Dispose)).ToList();
            cleanup.Add(CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(path)));
            await CleanupFailureCollector.RunAfterFailureAsync("Source transition fixture cleanup failed.", primary, cleanup.ToArray());
        }
    }
    private sealed class Fixture(string root)
    {
        private readonly string _root = root;
        internal string Root => _root;
        internal string SourceDirectory { get; set; } = root;
        internal string Source => Path.Combine(SourceDirectory, "source.bin");
        internal string PreferredCustodyParent { get; set; } = root;
        internal List<PhysicalSourceTransition> Retained { get; } = [];
        internal List<string> RetainedNamespaces { get; } = [];
        internal Action<string>? BeforeMatch { get; set; }
        internal Action<string>? AfterCustodyCreated { get; set; }
        internal Action<string>? BeforeMove { get; set; }
        internal Action<string>? AfterMatch { get; set; }
        internal async Task<PhysicalSourceTransition> RetainLateSourceOccupantAsync(byte[] before, byte[] external, byte[] desired,
            PhysicalSourceTransitionMode mode)
        {
            Assert.IsNull(AfterMatch);
            await File.WriteAllBytesAsync(Source, before);
            var occupied = false;
            AfterMatch = name =>
            {
                if (name != "captured" || occupied) return;
                Assert.IsFalse(File.Exists(Source));
                File.WriteAllBytes(Source, external);
                occupied = true;
            };
            try { await Assert.ThrowsExactlyAsync<AggregateException>(() => ApplyAsync(before, desired, mode)); }
            finally { AfterMatch = null; }
            Assert.IsTrue(occupied);
            AssertBytes(Source, external);
            Assert.HasCount(1, Retained);
            var receipt = Retained[0];
            AssertBytes(Path.Combine(receipt.CustodyPath, "captured"), before);
            return receipt;
        }
        internal Task ApplyAsync(byte[]? expected, byte[]? desired, PhysicalSourceTransitionMode mode,
            Func<IReadOnlyList<string>, CancellationToken, Task>? authorize = null, CancellationToken ct = default)
            => PhysicalSourceTransition.ApplyAsync(Source, expected, desired, mode,
                (parent, leaf, temporary, token) => AcquireCustodyAsync(parent, leaf, temporary, authorize ?? Authorize, token),
                authorize ?? Authorize, Retained.Add, RetainedNamespaces.Add,
                (path, token) => new ObservedScope(PhysicalSourceTransition.OpenParent(path, token), this), null, ct);

        internal async Task<PhysicalSourceCustody> AcquireCustodyAsync(PhysicalDirectoryScope source, string leaf, string temporary,
            Func<IReadOnlyList<string>, CancellationToken, Task> authorize, CancellationToken ct)
        {
            await authorize([Source, source.Path, PreferredCustodyParent, Path.Combine(PreferredCustodyParent, leaf),
                Path.Combine(source.Path, temporary)], ct);
            var parent = new ObservedScope(PhysicalSourceTransition.OpenParent(PreferredCustodyParent, ct), this);
            return new PhysicalSourceCustody(source, parent, leaf, parent.AssertCurrentPath, parent.AssertCurrentPath, authorize);
        }
    }
    private sealed class ObservedScope(PhysicalDirectoryScope inner, Fixture fixture) : PhysicalDirectoryScope(inner.Path)
    {
        private bool _disposed;
        private static PhysicalDirectoryScope Unwrap(PhysicalDirectoryScope scope) => scope is ObservedScope observed ? observed.Inner : scope;
        private PhysicalDirectoryScope Inner => inner;
        internal override PhysicalDirectoryEntryKind? GetEntryKind(string name, CancellationToken ct) => inner.GetEntryKind(name, ct);
        internal override DateTime? ReadFileLastWriteTimeUtcIfPresent(string name, CancellationToken ct) => inner.ReadFileLastWriteTimeUtcIfPresent(name, ct);
        internal override IReadOnlyList<string> Entries() => inner.Entries();
        internal override PhysicalDirectoryScope? OpenChild(string name) => inner.OpenChild(name) is { } child ? new ObservedScope(child, fixture) : null;
        internal override PhysicalDirectoryScope CreateOwnedChildScope(string name)
        {
            var child = inner.CreateOwnedChildScope(name);
            try
            {
                fixture.AfterCustodyCreated?.Invoke(child.Path);
                return new ObservedScope(child, fixture);
            }
            catch (Exception failure)
            {
                OwnedResourceCleanup.DisposePreservingFailure("Injected custody acquisition failed.", failure, child);
                throw;
            }
        }
        internal override bool SameIdentity(PhysicalDirectoryScope other) => inner.SameIdentity(Unwrap(other));
        internal override bool SameFilesystem(PhysicalDirectoryScope other) => inner.SameFilesystem(Unwrap(other));
        internal override bool IsLink(string name) => inner.IsLink(name);
        internal override IDisposable? LockOwner(string name) => inner.LockOwner(name);
        internal override DateTime LastActivityUtc => inner.LastActivityUtc;
        internal override bool TryDeleteFile(string name, CancellationToken ct) => inner.TryDeleteFile(name, ct);
        internal override PhysicalDirectoryScope? OpenDirectoryForAccess(string name, bool create, PhysicalDirectoryCreationMode creationMode, CancellationToken ct)
            => inner.OpenDirectoryForAccess(name, create, creationMode, ct) is { } child ? new ObservedScope(child, fixture) : null;
        internal override SafeFileHandle CreateFreshFile(string name, CancellationToken ct) => inner.CreateFreshFile(name, ct);
        internal override SafeFileHandle? OpenReadFileIfPresent(string name, CancellationToken ct) => inner.OpenReadFileIfPresent(name, ct);
        internal override UnixFileMode? ReadFileModeIfPresent(string name, CancellationToken ct) => inner.ReadFileModeIfPresent(name, ct);
        internal override void PublishPreparedFile(string temporary, SafeFileHandle preparedWitness, string name, SafeFileHandle? replacedWitness, CancellationToken ct)
            => inner.PublishPreparedFile(temporary, preparedWitness, name, replacedWitness, ct);
        internal override SafeFileHandle? OpenTransitionFileIfPresent(string name, bool writable, CancellationToken ct) => inner.OpenTransitionFileIfPresent(name, writable, ct);
        internal override SafeFileHandle CreateTransitionFile(string name, CancellationToken ct) => inner.CreateTransitionFile(name, ct);
        internal override bool CurrentFileMatches(string name, SafeFileHandle witness)
        {
            fixture.BeforeMatch?.Invoke(name);
            var matches = inner.CurrentFileMatches(name, witness);
            fixture.AfterMatch?.Invoke(name);
            return matches;
        }
        internal override void MoveFileNoReplace(string name, SafeFileHandle? witness, PhysicalDirectoryScope destination, string destinationName, CancellationToken ct)
        {
            fixture.BeforeMove?.Invoke(name);
            inner.MoveFileNoReplace(name, witness, Unwrap(destination), destinationName, ct);
        }
        internal override void ReclaimAdmittedTransitionFile(string name, SafeFileHandle witness, CancellationToken ct)
            => inner.ReclaimAdmittedTransitionFile(name, witness, ct);
        internal override void ReleaseNamespacePin() => inner.ReleaseNamespacePin();
        internal override PhysicalDirectoryCleanupOutcome RemoveHeldChild(string name, PhysicalDirectoryScope child, CancellationToken ct)
            => inner.RemoveHeldChild(name, Unwrap(child), ct);
        public override void Dispose() { if (_disposed) return; _disposed = true; inner.Dispose(); }
    }
}
