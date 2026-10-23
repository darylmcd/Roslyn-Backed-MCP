using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceRecoveryConflictTests : WorkspaceForkGraphReplayFixture
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task PersistenceConflict_PreservesUnknownExternalBytes(bool conflictBeforeSecondWrite, bool finishPhysicalWrites)
    {
        var source = CreateIsolatedWorkspaceCopy();
        string? loadedWorkspaceId = null;
        Exception? primary = null;
        var a = source.GetPath(".guarded-rollback-A");
        var b = source.GetPath(".guarded-rollback-B");
        var entryA = Encoding.UTF8.GetBytes("entry A");
        var entryB = Encoding.UTF8.GetBytes("entry B");
        var postA = Encoding.UTF8.GetBytes("owned post A");
        var postB = Encoding.UTF8.GetBytes("owned post B");
        var external = Encoding.UTF8.GetBytes("external writer owns this byte sequence");
        try
        {
            loadedWorkspaceId = await source.LoadAsync();
            await File.WriteAllBytesAsync(a, entryA);
            await File.WriteAllBytesAsync(b, entryB);
            var authority = (IWorkspaceRecoveryAuthority)WorkspaceManager;
            var operation = await authority.CaptureRecoveryAsync(source.WorkspaceId, "guarded physical persistence",
                [FileSnapshotDto.FromExistingBytes(a, entryA), FileSnapshotDto.FromExistingBytes(b, entryB)], CancellationToken.None);
            var armed = true;
            var fileSystem = new InterleavingDocumentFileSystem(path =>
            {
                if (!armed) return;
                if (conflictBeforeSecondWrite && FileSystemPath.Comparer.Equals(path, a))
                {
                    armed = false;
                    File.WriteAllBytes(b, external);
                }
                else if (!conflictBeforeSecondWrite && FileSystemPath.Comparer.Equals(path, b))
                {
                    armed = false;
                    File.WriteAllBytes(a, external);
                    if (!finishPhysicalWrites)
                        throw new IOException("Synthetic second write failure after an external edit to the first target.");
                }
            });
            var persistence = new DocumentSetPersistenceService(WorkspaceManager, NullLogger.Instance, fileSystem);
            Exception? failure = null;
            try
            {
                await persistence.PersistMutationsAsync(source.WorkspaceId, expected: null,
                    [new(a, entryA, postA, "entry A", "owned post A"), new(b, entryB, postB, "entry B", "owned post B")], operation, CancellationToken.None);
            }
            catch (Exception observed) { failure = observed; }
            CollectionAssert.AreEqual(external, await File.ReadAllBytesAsync(conflictBeforeSecondWrite ? b : a),
                "Neither a later operation write nor compensation may overwrite an unknown external edit.");
            Assert.IsNotNull(failure, "Conflicted persistence must refuse the operation.");
            if (!conflictBeforeSecondWrite)
            {
                Assert.IsNotNull(authority.GetPendingRecovery(source.WorkspaceId));
                Assert.ThrowsExactly<PublicInvalidOperationException>(() => authority.EnsureMutationAllowed(source.WorkspaceId, [a]));
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Persistence conflict fixture cleanup failed.", primary,
                async () =>
                {
                    if (loadedWorkspaceId is null || ((IWorkspaceRecoveryAuthority)WorkspaceManager).GetPendingRecovery(source.WorkspaceId) is null) return;
                    await File.WriteAllBytesAsync(a, postA);
                    await File.WriteAllBytesAsync(b, entryB);
                    var recovery = new UndoService(NullLogger<UndoService>.Instance, WorkspaceManager);
                    Exception? cleanupFailure90 = null;
                    try
                    {
                        Assert.IsTrue(await recovery.RevertAsync(source.WorkspaceId), "Explicit fixture resolution must release recovery authority.");
                    }
                    catch (Exception failure) { cleanupFailure90 = failure; throw; }
                    finally
                    {
                        await CleanupFailureCollector.RunAfterFailureAsync("Recovery service cleanup failed.", cleanupFailure90,
                            CleanupFailureCollector.FromAction(recovery.Dispose));
                    }
                }, () => source.DisposeAsync());
        }
    }
    [TestMethod]
    public async Task InverseRollbackConflict_PreservesExternalBytesAndOriginalRetryTarget()
    {
        var source = CreateIsolatedWorkspaceCopy();
        string? loadedWorkspaceId = null;
        Exception? primary = null;
        var path = source.GetPath(".inverse-external-conflict");
        try
        {
            loadedWorkspaceId = await source.LoadAsync();
            var authority = (IWorkspaceRecoveryAuthority)WorkspaceManager;
            var reject = true;
            var external = Encoding.UTF8.GetBytes("external writer owns inverse target");
            var facade = UndoFacade(authority.AuthorizePathsAsync, authority.StageRestorationAsync, async (operation, expected, retained, ct) =>
            {
                if (reject && !retained)
                {
                    reject = false;
                    await File.WriteAllBytesAsync(path, external, ct);
                    throw new IOException("Synthetic inverse publication failure after an external target edit.");
                }
                await authority.CommitRestorationAsync(operation, expected, retained, ct);
            });
            var undo = new UndoService(NullLogger<UndoService>.Instance, facade);
            Exception? cleanupFailure117 = null;
            try
            {
                await PublishPreviousExternalUndoAsync(WorkspaceManager, undo, source.WorkspaceId, path, "original inverse target");
                Exception? failure = null;
                try { await undo.RevertAsync(source.WorkspaceId); }
                catch (Exception observed) { failure = observed; }
                CollectionAssert.AreEqual(external, await File.ReadAllBytesAsync(path), "Failed inverse compensation must preserve unknown external bytes.");
                Assert.IsInstanceOfType<AggregateException>(failure);
                Assert.IsNotNull(authority.GetPendingRecovery(source.WorkspaceId));
                Assert.ThrowsExactly<PublicInvalidOperationException>(() => authority.EnsureMutationAllowed(source.WorkspaceId, [path]));
                File.Delete(path); // Explicit fixture resolution to the known inverse-post state.
                Assert.IsTrue(await undo.RevertAsync(source.WorkspaceId));
                Assert.AreEqual("original inverse target", undo.GetLastOperation(source.WorkspaceId)?.Description);
                Assert.IsTrue(await undo.RevertAsync(source.WorkspaceId));
                Assert.IsNull(undo.GetLastOperation(source.WorkspaceId));
            }
            catch (Exception failure) { cleanupFailure117 = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Recovery undo cleanup failed.", cleanupFailure117,
                    CleanupFailureCollector.FromAction(undo.Dispose));
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Inverse conflict fixture cleanup failed.", primary,
                async () =>
                {
                    if (loadedWorkspaceId is null || ((IWorkspaceRecoveryAuthority)WorkspaceManager).GetPendingRecovery(source.WorkspaceId) is null) return;
                    File.Delete(path);
                    var recovery = new UndoService(NullLogger<UndoService>.Instance, WorkspaceManager);
                    Exception? cleanupFailure140 = null;
                    try
                    {
                        Assert.IsTrue(await recovery.RevertAsync(source.WorkspaceId));
                    }
                    catch (Exception failure) { cleanupFailure140 = failure; throw; }
                    finally
                    {
                        await CleanupFailureCollector.RunAfterFailureAsync("Recovery service cleanup failed.", cleanupFailure140,
                            CleanupFailureCollector.FromAction(recovery.Dispose));
                    }
                }, () => source.DisposeAsync());
        }
    }

    [TestMethod]
    public async Task FailedInverseRecovery_RetainsOriginalUndoUntilSuccessfulRetry()
    {
        var source = CreateIsolatedWorkspaceCopy();
        Exception? primary = null;
        try
        {
            await source.LoadAsync();
            var authority = (IWorkspaceRecoveryAuthority)WorkspaceManager;
            var path = source.GetPath(".inverse-recovery-history");
            var reject = true;
            var facade = UndoFacade(authority.AuthorizePathsAsync, authority.StageRestorationAsync, async (operation, expected, retained, ct) =>
            {
                if (reject && !retained)
                {
                    reject = false;
                    // Fail after the inverse write and obstruct its byte rollback as well.
                    File.Delete(path);
                    Directory.CreateDirectory(path);
                    throw new IOException("Synthetic inverse publication and rollback obstruction.");
                }
                await authority.CommitRestorationAsync(operation, expected, retained, ct);
            });
            var undo = new UndoService(NullLogger<UndoService>.Instance, facade);
            Exception? cleanupFailure169 = null;
            try
            {
                await PublishPreviousExternalUndoAsync(WorkspaceManager, undo, source.WorkspaceId, path, "original target");
                undo.CommitPendingCapture(source.WorkspaceId, 37, [path]);
                var post = await File.ReadAllBytesAsync(path);
                var initialVersion = WorkspaceManager.GetCurrentVersion(source.WorkspaceId);
                await Assert.ThrowsExactlyAsync<AggregateException>(() => undo.RevertAsync(source.WorkspaceId));
                Assert.IsNotNull(authority.GetPendingRecovery(source.WorkspaceId));
                Assert.AreEqual("Recover failed undo", undo.GetLastOperation(source.WorkspaceId)?.Description);
                Assert.IsTrue(Directory.Exists(path));
                // Resolve the synthetic conflict to the known inverse-post (absent) state.
                Directory.Delete(path);
                Assert.IsTrue(await undo.RevertAsync(source.WorkspaceId), "Recovery must restore the failed inverse entry, not consume the original target.");
                CollectionAssert.AreEqual(post, await File.ReadAllBytesAsync(path));
                Assert.IsNull(authority.GetPendingRecovery(source.WorkspaceId));
                Assert.AreEqual("original target", undo.GetLastOperation(source.WorkspaceId)?.Description);
                Assert.IsTrue(WorkspaceManager.GetCurrentVersion(source.WorkspaceId) > initialVersion);
                Assert.IsTrue((await undo.RevertBySequenceAsync(source.WorkspaceId, 37)).Reverted);
                Assert.IsFalse(File.Exists(path));
                Assert.IsNull(undo.GetLastOperation(source.WorkspaceId));
                Assert.AreEqual("unknown-sequence", (await undo.RevertBySequenceAsync(source.WorkspaceId, 37)).Reason);
                Assert.IsFalse(await undo.RevertAsync(source.WorkspaceId));
            }
            catch (Exception failure) { cleanupFailure169 = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Recovery undo cleanup failed.", cleanupFailure169,
                    CleanupFailureCollector.FromAction(undo.Dispose));
            }
        }
        catch (Exception failure)
        {
            primary = failure;
            Console.WriteLine("Controlled recovery primary: " + failure.GetType().Name + ": " + failure.Message);
            WriteSafeGraphDiagnostics(failure);
            throw;
        }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Recovery fixture cleanup failed.", primary, () => source.DisposeAsync());
        }
    }

    [TestMethod]
    public async Task OwnedPostConflict_RetainsBarrierAndPreservesUnknownExternalBytes()
    {
        var source = CreateIsolatedWorkspaceCopy();
        Exception? primary = null;
        try
        {
            await source.LoadAsync();
            var authority = (IWorkspaceRecoveryAuthority)WorkspaceManager;
            var previous = source.GetPath(".previous-conflict-undo");
            await PublishPreviousExternalUndoAsync(WorkspaceManager, UndoService, source.WorkspaceId, previous, "previous undo");
            var path = source.GetPath(".owned-post-conflict");
            var physical = await authority.CaptureRecoveryAsync(source.WorkspaceId, "conflicted operation",
                [new FileSnapshotDto(path, null)], CancellationToken.None);
            var prepared = await UndoService.PrepareBeforeApplyAsync(physical, CancellationToken.None);
            var post = Encoding.UTF8.GetBytes("owned post");
            var lease = await authority.BeginMutationAsync(physical, CancellationToken.None);
            Exception? cleanupFailure220 = null;
            try
            {
                lease.BeforeWrite(FileSnapshotDto.FromExistingBytes(path, post));
                await File.WriteAllBytesAsync(path, post);
                await authority.CommitMutationAsync(physical, expected: null, CancellationToken.None);
                lease.Complete([FileSnapshotDto.FromExistingBytes(path, post)]);
            }
            catch (Exception failure) { cleanupFailure220 = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Recovery lease cleanup failed.", cleanupFailure220,
                    CleanupFailureCollector.FromAction(lease.Dispose));
            }
            var external = Encoding.UTF8.GetBytes("external conflict must survive");
            await File.WriteAllBytesAsync(path, external);
            await Assert.ThrowsExactlyAsync<WorkspaceMutationOwnershipException>(() => prepared.PublishAsync(CancellationToken.None));
            Assert.AreSame(physical, authority.GetPendingRecovery(source.WorkspaceId));
            Assert.AreEqual("conflicted operation", UndoService.GetLastOperation(source.WorkspaceId)?.Description);
            await Assert.ThrowsExactlyAsync<WorkspaceMutationOwnershipException>(() => UndoService.RevertAsync(source.WorkspaceId));
            CollectionAssert.AreEqual(external, await File.ReadAllBytesAsync(path));
            Assert.ThrowsExactly<PublicInvalidOperationException>(() => authority.EnsureMutationAllowed(source.WorkspaceId, [previous]));
            Assert.IsTrue(File.Exists(previous));
            // Explicitly resolve to the operation's known post-state; only then may recovery write.
            await File.WriteAllBytesAsync(path, post);
            Assert.IsTrue(await UndoService.RevertAsync(source.WorkspaceId));
            Assert.IsFalse(File.Exists(path));
            Assert.IsNull(authority.GetPendingRecovery(source.WorkspaceId));
            Assert.AreEqual("previous undo", UndoService.GetLastOperation(source.WorkspaceId)?.Description);
            Assert.IsTrue(await UndoService.RevertAsync(source.WorkspaceId));
            Assert.IsFalse(File.Exists(previous));
        }
        catch (Exception failure)
        {
            primary = failure;
            Console.WriteLine("Controlled recovery primary: " + failure.GetType().Name + ": " + failure.Message);
            WriteSafeGraphDiagnostics(failure);
            throw;
        }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Recovery fixture cleanup failed.", primary, () => source.DisposeAsync());
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task UndoLoadFailure_RetainsFilesLoadedGraphVersionAndRetry(bool ownedCompensation)
    {
        var source = CreateIsolatedWorkspaceCopy();
        Exception? cleanupFailure263 = null;
        try
        {
            var loader = new ControlledReloadLoader();
            var previews = new PreviewStore();
            var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            WorkspaceManager? manager = null;
            Exception? cleanupFailure266 = null;
            try
            {
                manager = new WorkspaceManager(new RoslynMcp.Roslyn.Services.ForkInvocationPathPolicy(new RoslynMcp.Tests.Helpers.OwnedTestWorkspacePathPolicy()), NullLogger<WorkspaceManager>.Instance, previews,
                    watcher,
                    new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 }, cacheStore: null, sessionLoader: loader);
                var undo = new UndoService(NullLogger<UndoService>.Instance, manager);
                Exception? cleanupFailure269 = null;
                try
                {
                    var status = await manager.LoadAsync(source.SolutionPath, OwnedNativeLoadContext.Properties(source.SolutionPath, new Dictionary<string, string>()), CancellationToken.None);
                    var original = manager.GetCurrentSolution(status.WorkspaceId);
                    var path = source.GetPath("SampleLib", "Dog.cs");
                    var bytes = await File.ReadAllBytesAsync(path);
                    var document = original.Projects.SelectMany(project => project.Documents).Single(document => document.FilePath == path);
                    var text = await document.GetTextAsync();
                    var postText = SourceText.From("// operation to undo\n" + text, text.Encoding);
                    var intended = original.WithDocumentText(document.Id, postText);
                    FileSnapshotDto[] files = [FileSnapshotCapture.FromBytesOrFallback(path, bytes, null)];
                    var authority = (IWorkspaceRecoveryAuthority)manager;
                    var physical = await authority.CaptureRecoveryAsync(status.WorkspaceId, "retryable operation", files, CancellationToken.None);
                    var operation = await undo.PrepareBeforeApplyAsync(physical, CancellationToken.None);
                    var lease = await authority.BeginMutationAsync(physical, CancellationToken.None);
                    Exception? cleanupFailure282 = null;
                    try
                    {
                        lease.BeforeWrite(FileSnapshotDto.FromExistingBytes(path, SolutionFileMutationPlanner.Encode(postText.ToString(), SourceFileEncoding.FromBytes(bytes))));
                        await File.WriteAllTextAsync(path, postText.ToString(), SourceFileEncoding.FromBytes(bytes));
                        await authority.CommitMutationAsync(physical, intended, CancellationToken.None);
                        lease.Complete([FileSnapshotDto.FromExistingBytes(path, await File.ReadAllBytesAsync(path))]);
                    }
                    catch (Exception failure) { cleanupFailure282 = failure; throw; }
                    finally
                    {
                        await CleanupFailureCollector.RunAfterFailureAsync("Recovery lease cleanup failed.", cleanupFailure282,
                            CleanupFailureCollector.FromAction(lease.Dispose));
                    }
                    var postBytes = await File.ReadAllBytesAsync(path);
                    await operation.PublishAsync(CancellationToken.None);
                    undo.CommitPendingCapture(status.WorkspaceId, 1, [path]);
                    var loaded = manager.GetCurrentSolution(status.WorkspaceId);
                    var version = manager.GetCurrentVersion(status.WorkspaceId);
                    var token = previews.Store(status.WorkspaceId, loaded, loaded, version, "survives undo failure", [], PreviewKind.Unspecified);
                    loader.RejectReload = true;
                    Assert.IsFalse(ownedCompensation ? await operation.RestoreAsync(CancellationToken.None) : await undo.RevertAsync(status.WorkspaceId));
                    CollectionAssert.AreEqual(postBytes, await File.ReadAllBytesAsync(path));
                    Assert.AreSame(loaded, manager.GetCurrentSolution(status.WorkspaceId));
                    Assert.AreEqual(version, manager.GetCurrentVersion(status.WorkspaceId));
                    Assert.IsNotNull(previews.Retrieve(token));
                    Assert.AreEqual("retryable operation", undo.GetLastOperation(status.WorkspaceId)?.Description);
                    loader.RejectReload = false;
                    Assert.IsTrue(ownedCompensation ? await operation.RestoreAsync(CancellationToken.None) : await undo.RevertAsync(status.WorkspaceId));
                    CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path));
                    Assert.AreEqual(ownedCompensation ? version - 1 : version + 1, manager.GetCurrentVersion(status.WorkspaceId));
                    Assert.IsNull(undo.GetLastOperation(status.WorkspaceId));
                    Assert.IsNotNull(previews.Retrieve(token));
                }
                catch (Exception failure) { cleanupFailure269 = failure; throw; }
                finally
                {
                    await CleanupFailureCollector.RunAfterFailureAsync("Recovery undo cleanup failed.", cleanupFailure269,
                        CleanupFailureCollector.FromAction(undo.Dispose));
                }
            }
            catch (Exception failure) { cleanupFailure266 = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Recovery manager cleanup failed.", cleanupFailure266,
                    CleanupFailureCollector.FromAction(() => { if (manager is not null) manager.Dispose(); else watcher.Dispose(); }));
            }
        }
        catch (Exception failure) { cleanupFailure263 = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Recovery source cleanup failed.", cleanupFailure263,
                () => source.DisposeAsync());
        }
    }

    [TestMethod]
    public async Task OwnedCompensation_RejectsSupersededApplyAndPreservesLatestUndo()
    {
        var source = CreateIsolatedWorkspaceCopy();
        Exception? cleanupFailure313 = null;
        try
        {
            await source.LoadAsync();
            var path = source.GetPath("SampleLib", "Dog.cs");
            async Task<RefactoringApplyOperationResult> ApplyCommentAsync(string comment)
            {
                var original = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
                var document = original.Projects.SelectMany(project => project.Documents).Single(document => document.FilePath == path);
                var text = await document.GetTextAsync();
                var modified = original.WithDocumentText(document.Id, SourceText.From(comment + "\n" + text, text.Encoding));
                var changes = await SolutionDiffHelper.ComputeChangesAsync(original, modified, SolutionDiffReadContext.ForWorkspace(WorkspaceManager, source.WorkspaceId), CancellationToken.None);
                var token = PreviewStore.Store(source.WorkspaceId, original, modified,
                    WorkspaceManager.GetCurrentVersion(source.WorkspaceId), comment, changes, PreviewKind.Unspecified);
                return await RefactoringService.ApplyRefactoringWithUndoAsync(token, "test_apply", false, CancellationToken.None);
            }
            var first = await ApplyCommentAsync("// first operation");
            Assert.IsTrue(first.Result.Success);
            Assert.IsNotNull(first.UndoOperation);
            var second = await ApplyCommentAsync("// second operation");
            Assert.IsTrue(second.Result.Success);
            var bytes = await File.ReadAllBytesAsync(path);
            var loaded = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            var version = WorkspaceManager.GetCurrentVersion(source.WorkspaceId);
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => first.UndoOperation.RestoreAsync(CancellationToken.None));
            Assert.ThrowsExactly<WorkspaceMutationOwnershipException>(() => first.UndoOperation.RetainRecovery());
            Assert.IsNull(((IWorkspaceRecoveryAuthority)WorkspaceManager).GetPendingRecovery(source.WorkspaceId));
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path));
            Assert.AreSame(loaded, WorkspaceManager.GetCurrentSolution(source.WorkspaceId));
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(source.WorkspaceId));
            Assert.AreEqual("// second operation", UndoService.GetLastOperation(source.WorkspaceId)?.Description);
            Assert.IsNotNull(second.UndoOperation);
            Assert.IsTrue(await second.UndoOperation.RestoreAsync(CancellationToken.None));
            StringAssert.StartsWith(await File.ReadAllTextAsync(path), "// first operation");
            Assert.AreEqual("// first operation", UndoService.GetLastOperation(source.WorkspaceId)?.Description);
            Assert.IsTrue(await UndoService.RevertAsync(source.WorkspaceId));
            Assert.IsFalse((await File.ReadAllTextAsync(path)).Contains("// first operation", StringComparison.Ordinal));
            Assert.IsTrue(WorkspaceManager.GetCurrentVersion(source.WorkspaceId) > version - 1);
        }
        catch (Exception failure) { cleanupFailure313 = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Recovery source cleanup failed.", cleanupFailure313,
                () => source.DisposeAsync());
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task SourcePersistence_RejectsExternalTextOrEncodingEpochBeforeEffects(bool semanticEdit)
    {
        var source = CreateIsolatedWorkspaceCopy();
        Exception? cleanupFailure356 = null;
        try
        {
            await source.LoadAsync();
            var original = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            var version = WorkspaceManager.GetCurrentVersion(source.WorkspaceId);
            var path = source.GetPath("SampleLib", "Dog.cs");
            var document = original.Projects.SelectMany(project => project.Documents).Single(document => document.FilePath == path);
            var text = await document.GetTextAsync();
            var bytes = await File.ReadAllBytesAsync(path);
            var modified = original.WithDocumentText(document.Id, SourceText.From("// reviewed edit\n" + text, text.Encoding));
            var changes = await SolutionDiffHelper.ComputeChangesAsync(original, modified, SolutionDiffReadContext.ForWorkspace(WorkspaceManager, source.WorkspaceId), CancellationToken.None);
            var token = PreviewStore.Store(source.WorkspaceId, original, modified, version, "epoch guard", changes, PreviewKind.Unspecified);
            await PublishPreviousExternalUndoAsync(WorkspaceManager, UndoService, source.WorkspaceId, source.GetPath(".previous-undo"), "previous undo");
            var physical = await ((IWorkspaceRecoveryAuthority)WorkspaceManager).CaptureRecoveryAsync(source.WorkspaceId, "epoch guard",
                [FileSnapshotDto.FromExistingBytes(path, bytes)], CancellationToken.None);
            await File.WriteAllTextAsync(path, semanticEdit ? "// external edit\n" + text : text.ToString(), Encoding.Unicode);
            var externalBytes = await File.ReadAllBytesAsync(path);
            var persistence = new DocumentSetPersistenceService(WorkspaceManager, NullLogger.Instance);
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => persistence.PersistAsync(source.WorkspaceId, original, modified, modified.GetChanges(original),
                [FileSnapshotDto.FromExistingBytes(path, bytes)], physical, CancellationToken.None));
            CollectionAssert.AreEqual(externalBytes, await File.ReadAllBytesAsync(path));
            Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(source.WorkspaceId));
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(source.WorkspaceId));
            Assert.AreEqual("previous undo", UndoService.GetLastOperation(source.WorkspaceId)?.Description);
            Assert.IsNotNull(PreviewStore.Retrieve(token));
        }
        catch (Exception failure) { cleanupFailure356 = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Recovery source cleanup failed.", cleanupFailure356,
                () => source.DisposeAsync());
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PublicationCancellation_CompensatesOrRetainsItsOwnRetryAuthority(bool rejectCompensation)
    {
        var source = CreateIsolatedWorkspaceCopy();
        Exception? cleanupFailure387 = null;
        try
        {
            var loader = new ControlledReloadLoader();
            var previews = new PreviewStore();
            var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            WorkspaceManager? manager = null;
            Exception? cleanupFailure390 = null;
            try
            {
                manager = new WorkspaceManager(new RoslynMcp.Roslyn.Services.ForkInvocationPathPolicy(new RoslynMcp.Tests.Helpers.OwnedTestWorkspacePathPolicy()), NullLogger<WorkspaceManager>.Instance, previews,
                    watcher,
                    new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 }, cacheStore: null, sessionLoader: loader);
                var undo = new UndoService(NullLogger<UndoService>.Instance, manager);
                Exception? cleanupFailure393 = null;
                try
                {
                    var status = await manager.LoadAsync(source.SolutionPath, OwnedNativeLoadContext.Properties(source.SolutionPath, new Dictionary<string, string>()), CancellationToken.None);
                    var original = manager.GetCurrentSolution(status.WorkspaceId);
                    var version = manager.GetCurrentVersion(status.WorkspaceId);
                    var path = source.GetPath("SampleLib", "Dog.cs");
                    var bytes = await File.ReadAllBytesAsync(path);
                    await PublishPreviousExternalUndoAsync(manager, undo, status.WorkspaceId, source.GetPath(".previous-undo"), "previous operation");
                    undo.CommitPendingCapture(status.WorkspaceId, 99, [source.GetPath(".previous-undo")]);
                    var document = original.Projects.SelectMany(project => project.Documents).Single(document => document.FilePath == path);
                    var text = await document.GetTextAsync();
                    var modifiedText = SourceText.From("// unpublished operation\n" + text, text.Encoding);
                    var modified = original.WithDocumentText(document.Id, modifiedText);
                    FileSnapshotDto[] files = [FileSnapshotDto.FromExistingBytes(path, bytes)];
                    var authority = (IWorkspaceRecoveryAuthority)manager;
                    var physical = await authority.CaptureRecoveryAsync(status.WorkspaceId, "failed publication", files, CancellationToken.None);
                    var operation = await undo.PrepareBeforeApplyAsync(physical, CancellationToken.None);
                    var lease = await authority.BeginMutationAsync(physical, CancellationToken.None);
                    Exception? cleanupFailure409 = null;
                    try
                    {
                        lease.BeforeWrite(FileSnapshotDto.FromExistingBytes(path, SolutionFileMutationPlanner.Encode(modifiedText.ToString(), SourceFileEncoding.FromBytes(bytes))));
                        await File.WriteAllTextAsync(path, modifiedText.ToString(), SourceFileEncoding.FromBytes(bytes));
                        await authority.CommitMutationAsync(physical, modified, CancellationToken.None);
                        lease.Complete([FileSnapshotDto.FromExistingBytes(path, await File.ReadAllBytesAsync(path))]);
                    }
                    catch (Exception failure) { cleanupFailure409 = failure; throw; }
                    finally
                    {
                        await CleanupFailureCollector.RunAfterFailureAsync("Recovery lease cleanup failed.", cleanupFailure409,
                            CleanupFailureCollector.FromAction(lease.Dispose));
                    }
                    var changedBytes = await File.ReadAllBytesAsync(path);
                    loader.RejectReload = rejectCompensation;
                    var cancellation = new CancellationTokenSource();
                    Exception? cleanupFailure418 = null;
                    try
                    {
                        cancellation.Cancel();
                        if (rejectCompensation)
                        {
                            await Assert.ThrowsExactlyAsync<AggregateException>(() => operation.PublishAsync(cancellation.Token));
                            CollectionAssert.AreEqual(changedBytes, await File.ReadAllBytesAsync(path));
                            Assert.AreEqual("failed publication", undo.GetLastOperation(status.WorkspaceId)?.Description);
                            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => undo.PrepareBeforeApplyAsync(physical, CancellationToken.None));
                            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => undo.RevertBySequenceAsync(status.WorkspaceId, 99));
                            loader.RejectReload = false;
                            Assert.IsTrue(await undo.RevertAsync(status.WorkspaceId));
                            Assert.AreEqual(version + 2, manager.GetCurrentVersion(status.WorkspaceId));
                            Assert.AreEqual("previous operation", undo.GetLastOperation(status.WorkspaceId)?.Description);
                        }
                        else
                        {
                            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => operation.PublishAsync(cancellation.Token));
                            Assert.AreEqual("previous operation", undo.GetLastOperation(status.WorkspaceId)?.Description);
                            Assert.AreEqual(version, manager.GetCurrentVersion(status.WorkspaceId));
                        }
                        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path));
                    }
                    catch (Exception failure) { cleanupFailure418 = failure; throw; }
                    finally
                    {
                        await CleanupFailureCollector.RunAfterFailureAsync("Recovery cancellation cleanup failed.", cleanupFailure418,
                            CleanupFailureCollector.FromAction(cancellation.Dispose));
                    }
                }
                catch (Exception failure) { cleanupFailure393 = failure; throw; }
                finally
                {
                    await CleanupFailureCollector.RunAfterFailureAsync("Recovery undo cleanup failed.", cleanupFailure393,
                        CleanupFailureCollector.FromAction(undo.Dispose));
                }
            }
            catch (Exception failure) { cleanupFailure390 = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Recovery manager cleanup failed.", cleanupFailure390,
                    CleanupFailureCollector.FromAction(() => { if (manager is not null) manager.Dispose(); else watcher.Dispose(); }));
            }
        }
        catch (Exception failure) { cleanupFailure387 = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Recovery source cleanup failed.", cleanupFailure387,
                () => source.DisposeAsync());
        }
    }

    [TestMethod]
    public async Task NativeCaseDistinctUndoFiles_DoNotBlockIndependentSequence()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Case-distinct physical paths require a case-sensitive filesystem.");
        var source = CreateIsolatedWorkspaceCopy();
        Exception? cleanupFailure445 = null;
        try
        {
            var upper = source.GetPath("SampleLib", "CaseDistinct.cs");
            var lower = source.GetPath("SampleLib", "casedistinct.cs");
            await File.WriteAllTextAsync(upper, "// upper original");
            await File.WriteAllTextAsync(lower, "// lower original");
            await source.LoadAsync();
            var before = WorkspaceManager.GetCurrentVersion(source.WorkspaceId);
            async Task ApplyAsync(string path, string text, int sequence)
            {
                var original = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
                var document = original.Projects.SelectMany(project => project.Documents).Single(document => document.FilePath == path);
                var modified = original.WithDocumentText(document.Id, SourceText.From(text));
                FileSnapshotDto[] files = [FileSnapshotDto.FromExistingBytes(path, await File.ReadAllBytesAsync(path))];
                var authority = (IWorkspaceRecoveryAuthority)WorkspaceManager;
                var physical = await authority.CaptureRecoveryAsync(source.WorkspaceId, path, files, CancellationToken.None);
                var operation = await UndoService.PrepareBeforeApplyAsync(physical, CancellationToken.None);
                var lease = await authority.BeginMutationAsync(physical, CancellationToken.None);
                Exception? cleanupFailure461 = null;
                try
                {
                    lease.BeforeWrite(FileSnapshotDto.FromExistingBytes(path, Encoding.UTF8.GetBytes(text)));
                    await File.WriteAllTextAsync(path, text);
                    await authority.CommitMutationAsync(physical, modified, CancellationToken.None);
                    lease.Complete([FileSnapshotDto.FromExistingBytes(path, await File.ReadAllBytesAsync(path))]);
                }
                catch (Exception failure) { cleanupFailure461 = failure; throw; }
                finally
                {
                    await CleanupFailureCollector.RunAfterFailureAsync("Recovery lease cleanup failed.", cleanupFailure461,
                        CleanupFailureCollector.FromAction(lease.Dispose));
                }
                await operation.PublishAsync(CancellationToken.None);
                UndoService.CommitPendingCapture(source.WorkspaceId, sequence, [path]);
            }
            await ApplyAsync(upper, "// upper applied", 1);
            await ApplyAsync(lower, "// lower applied", 2);
            var result = await UndoService.RevertBySequenceAsync(source.WorkspaceId, 1);
            Assert.IsTrue(result.Reverted, result.Reason);
            Assert.AreEqual(before + 3, WorkspaceManager.GetCurrentVersion(source.WorkspaceId));
            Assert.AreEqual("// upper original", await File.ReadAllTextAsync(upper));
            Assert.AreEqual("// lower applied", await File.ReadAllTextAsync(lower));
            var loaded = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            var lowerDocument = loaded.Projects.SelectMany(project => project.Documents).Single(document => document.FilePath == lower);
            Assert.AreEqual("// lower applied", (await lowerDocument.GetTextAsync()).ToString());
        }
        catch (Exception failure) { cleanupFailure445 = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Recovery source cleanup failed.", cleanupFailure445,
                () => source.DisposeAsync());
        }
    }

    [TestMethod]
    public async Task PhysicalRollbackFailure_RetainsManagerRecoveryWithoutUndoCoupling()
    {
        var source = CreateIsolatedWorkspaceCopy();
        string? loadedWorkspaceId = null;
        Exception? primaryFailure = null;
        try
        {
            loadedWorkspaceId = await source.LoadAsync();
            var original = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            var version = WorkspaceManager.GetCurrentVersion(source.WorkspaceId);
            var path = source.GetPath("SampleLib", "Dog.cs");
            var bytes = await File.ReadAllBytesAsync(path);
            var document = original.Projects.SelectMany(project => project.Documents).Single(document => document.FilePath == path);
            var text = await document.GetTextAsync();
            var modified = original.WithDocumentText(document.Id, SourceText.From("// failed physical write\n" + text, text.Encoding));
            await PublishPreviousExternalUndoAsync(WorkspaceManager, UndoService, source.WorkspaceId, source.GetPath(".previous-undo"), "previous operation");
            var physical = await ((IWorkspaceRecoveryAuthority)WorkspaceManager).CaptureRecoveryAsync(source.WorkspaceId, "Recover failed physical mutation",
                [FileSnapshotDto.FromExistingBytes(path, bytes)], CancellationToken.None);
            var service = new DocumentSetPersistenceService(WorkspaceManager, NullLogger.Instance, new WriteThenFailRollbackFileSystem(path));
            await Assert.ThrowsExactlyAsync<AggregateException>(() => service.PersistAsync(source.WorkspaceId, original, modified,
                modified.GetChanges(original), [FileSnapshotDto.FromExistingBytes(path, bytes)], physical, CancellationToken.None));
            Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(source.WorkspaceId));
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(source.WorkspaceId));
            var failedBytes = await File.ReadAllBytesAsync(path);
            Assert.IsFalse(bytes.AsSpan().SequenceEqual(failedBytes));
            var recoveryAuthority = (IWorkspaceRecoveryAuthority)WorkspaceManager;
            Assert.IsNotNull(recoveryAuthority.GetPendingRecovery(source.WorkspaceId));
            Assert.AreEqual("Recover failed physical mutation", UndoService.GetLastOperation(source.WorkspaceId)?.Description);
            ChangeTracker.RecordChange(source.WorkspaceId, "failed mutation with effects", [path], "test_apply");
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => PhysicalMutationFixture.ApplyAsync(WorkspaceManager, source.WorkspaceId, modified, CancellationToken.None));
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => WorkspaceManager.ReloadAsync(source.WorkspaceId, CancellationToken.None));
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => ((IPhysicalSolutionCommitter)WorkspaceManager)
                .CommitPhysicalSolutionAsync(source.WorkspaceId, modified, CancellationToken.None));
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => recoveryAuthority.CaptureRecoveryAsync(source.WorkspaceId,
                "blocked optional-undo mutation", [FileSnapshotDto.FromExistingBytes(path, File.ReadAllBytes(path))], CancellationToken.None));
            Assert.IsTrue(await UndoService.RevertAsync(source.WorkspaceId));
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path));
            Assert.AreEqual(version + 1, WorkspaceManager.GetCurrentVersion(source.WorkspaceId));
            Assert.IsNull(recoveryAuthority.GetPendingRecovery(source.WorkspaceId));
            Assert.AreEqual("previous operation", UndoService.GetLastOperation(source.WorkspaceId)?.Description);
            Assert.AreEqual("unknown-sequence", (await UndoService.RevertBySequenceAsync(source.WorkspaceId,
                ChangeTracker.GetChanges(source.WorkspaceId).Single().SequenceNumber)).Reason);
        }
        catch (Exception failure) { primaryFailure = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Failed to clean up physical recovery regression.", primaryFailure,
                async () =>
                {
                    if (loadedWorkspaceId is not null && ((IWorkspaceRecoveryAuthority)WorkspaceManager).GetPendingRecovery(source.WorkspaceId) is not null)
                        Assert.IsTrue(await UndoService.RevertAsync(source.WorkspaceId));
                },
                () => source.DisposeAsync());
        }
    }


    [TestMethod]
    public async Task PhysicalIdentity_ReadFailureCannotBecomeKnownAbsence()
    {
        var directory = Path.Combine(TestTempRoot.Current, "identity-read-failure-" + Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(directory);
            var identity = await PhysicalInputIdentity.CaptureAsync(directory, CancellationToken.None);
            Assert.IsTrue(identity.Exists);
            Assert.AreEqual(PhysicalInputKind.Directory, identity.Kind);
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => PhysicalInputReader.ReadDigestAsync(directory, CancellationToken.None));

            var lockedFile = Path.Combine(directory, "locked.cs");
            await File.WriteAllTextAsync(lockedFile, "// exclusive file input");
            var exclusive = File.Open(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Exception? cleanupFailure555 = null;
            try
            {
                var failure = await Assert.ThrowsAsync<Exception>(() => PhysicalInputIdentity.CaptureAsync(lockedFile, CancellationToken.None));
                Assert.IsTrue(failure is IOException or UnauthorizedAccessException,
                    "An unreadable existing file must produce a read error rather than a known-absent file identity.");
            }
            catch (Exception failure) { cleanupFailure555 = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Recovery exclusive cleanup failed.", cleanupFailure555,
                    CleanupFailureCollector.FromAction(exclusive.Dispose));
            }
            File.Delete(lockedFile);
            var missing = await PhysicalInputIdentity.CaptureAsync(Path.Combine(directory, "missing", "file.cs"), CancellationToken.None);
            Assert.IsFalse(missing.Exists);
            Assert.IsTrue(missing.ContentHash.IsEmpty);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Physical identity fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }));
        }
    }

    [TestMethod]
    public async Task CanceledPublication_CompensatesOwnDeltaAndPreservesExternalConservativeSibling()
    {
        var source = CreateIsolatedWorkspaceCopy();
        string? loadedWorkspaceId = null;
        Exception? primary = null;
        try
        {
            loadedWorkspaceId = await source.LoadAsync();
            var id = source.WorkspaceId;
            var authority = (IWorkspaceRecoveryAuthority)WorkspaceManager;
            await PublishPreviousExternalUndoAsync(WorkspaceManager, UndoService, id, source.GetPath(".previous-undo"), "previous undo");
            var original = WorkspaceManager.GetCurrentSolution(id);
            var version = WorkspaceManager.GetCurrentVersion(id);
            var own = original.Projects.SelectMany(project => project.Documents).Single(document => document.Name == "Dog.cs");
            var sibling = original.Projects.SelectMany(project => project.Documents).Single(document => document.Name == "Cat.cs");
            var ownBytes = await File.ReadAllBytesAsync(own.FilePath!);
            var siblingBytes = await File.ReadAllBytesAsync(sibling.FilePath!);
            var text = await own.GetTextAsync();
            var ownPostBytes = SolutionFileMutationPlanner.Encode("// own change\n" + text, SourceFileEncoding.FromBytes(ownBytes));
            var modified = original.WithDocumentText(own.Id, SourceText.From("// own change\n" + text, text.Encoding));
            FileSnapshotDto[] files = [FileSnapshotDto.FromExistingBytes(own.FilePath!, ownBytes), FileSnapshotDto.FromExistingBytes(sibling.FilePath!, siblingBytes)];
            var physical = await authority.CaptureRecoveryAsync(id, "own canceled publication", files, CancellationToken.None);
            var operation = await UndoService.PrepareBeforeApplyAsync(physical, CancellationToken.None);
            var lease = await authority.BeginMutationAsync(physical, CancellationToken.None);
            Exception? cleanupFailure597 = null;
            try
            {
                lease.BeforeWrite(FileSnapshotDto.FromExistingBytes(own.FilePath!, ownPostBytes));
                await File.WriteAllBytesAsync(own.FilePath!, ownPostBytes);
                await authority.CommitMutationAsync(physical, modified, CancellationToken.None);
                lease.Complete([FileSnapshotDto.FromExistingBytes(own.FilePath!, ownPostBytes), files[1]]);
            }
            catch (Exception failure) { cleanupFailure597 = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Recovery lease cleanup failed.", cleanupFailure597,
                    CleanupFailureCollector.FromAction(lease.Dispose));
            }
            var external = SolutionFileMutationPlanner.Encode("// external sibling\n" + await File.ReadAllTextAsync(sibling.FilePath!), SourceFileEncoding.FromBytes(siblingBytes));
            await File.WriteAllBytesAsync(sibling.FilePath!, external);
            var cancellation = new CancellationTokenSource();
            Exception? cleanupFailure606 = null;
            try
            {
                cancellation.Cancel();
                await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => operation.PublishAsync(cancellation.Token));
                CollectionAssert.AreEqual(ownBytes, await File.ReadAllBytesAsync(own.FilePath!));
                CollectionAssert.AreEqual(external, await File.ReadAllBytesAsync(sibling.FilePath!));
                var currentSibling = WorkspaceManager.GetCurrentSolution(id).Projects.SelectMany(project => project.Documents)
                    .Single(document => document.FilePath == sibling.FilePath);
                StringAssert.StartsWith((await currentSibling.GetTextAsync()).ToString(), "// external sibling");
                Assert.AreEqual(version + 2, WorkspaceManager.GetCurrentVersion(id), "Retained external state must receive a new identity.");
                Assert.AreEqual("previous undo", UndoService.GetLastOperation(id)?.Description);
                Assert.IsNull(authority.GetPendingRecovery(id));
            }
            catch (Exception failure) { cleanupFailure606 = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Recovery cancellation cleanup failed.", cleanupFailure606,
                    CleanupFailureCollector.FromAction(cancellation.Dispose));
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("External sibling recovery cleanup failed.", primary,
                async () =>
                {
                    if (loadedWorkspaceId is not null && ((IWorkspaceRecoveryAuthority)WorkspaceManager).GetPendingRecovery(source.WorkspaceId) is not null)
                        Assert.IsTrue(await UndoService.RevertAsync(source.WorkspaceId));
                }, () => source.DisposeAsync());
        }
    }
}
