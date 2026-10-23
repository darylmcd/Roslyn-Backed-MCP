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
public sealed class WorkspaceUndoOwnershipTests : WorkspaceForkGraphReplayFixture
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    // Keep unrelated native-evaluation classes outside the request budget.
    // The two competing undo requests inside this test remain concurrent.
    [TestMethod]
    [DoNotParallelize]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ConcurrentUndoRoutes_ClaimTheSameInverseExactlyOnce(bool secondUsesSequence)
    {
        // Scope: inverse ownership, not sample-graph throughput. Keep real native
        // capture/staging/commit on one project; multi-project replay is covered by
        // ProjectFileDocumentAndReferenceDelta_AreReviewedAppliedAndUndoneTogether.
        var root = Path.Combine(TestTempRoot.Current, nameof(ConcurrentUndoRoutes_ClaimTheSameInverseExactlyOnce), Guid.NewGuid().ToString("N"));
        var source = CreateWorkspaceScope(root, Path.Combine(root, "Ownership.slnx"), WorkspaceManager);
        Exception? cleanupFailure36 = null;
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(source.GetPath("global.json"), "{}");
            await File.WriteAllTextAsync(source.GetPath("Directory.Build.props"), "<Project />");
            await File.WriteAllTextAsync(source.SolutionPath, "<Solution><Project Path=\"Ownership.csproj\" /></Solution>");
            await File.WriteAllTextAsync(source.GetPath("Ownership.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>");
            await File.WriteAllTextAsync(source.GetPath("Owned.cs"), "public sealed class Owned { }");
            await RestoreWorkspaceAsync(source);
            await source.LoadAsync();
            var loaded = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            Assert.AreEqual(1, loaded.ProjectIds.Count);
            Assert.AreEqual(NativeCompilerContextAvailability.Certified, NativeCompilerContextCertificate.Require(loaded).Availability);
            var authority = (IWorkspaceRecoveryAuthority)WorkspaceManager;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var stages = 0;
            var facade = UndoFacade(authority.AuthorizePathsAsync, async (operation, files, layout, ct) =>
            {
                Interlocked.Increment(ref stages);
                entered.SetResult();
                await release.Task.WaitAsync(ct);
                return await authority.StageRestorationAsync(operation, files, layout, ct);
            }, authority.CommitRestorationAsync);
            var undo = new UndoService(NullLogger<UndoService>.Instance, facade);
            Exception? cleanupFailure60 = null;
            try
            {
                var path = source.GetPath(".one-owned-inverse");
                await PublishPreviousExternalUndoAsync(WorkspaceManager, undo, source.WorkspaceId, path, "one inverse");
                undo.CommitPendingCapture(source.WorkspaceId, 17, [path]);
                var version = WorkspaceManager.GetCurrentVersion(source.WorkspaceId);
                var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                Exception? cleanupFailure65 = null;
                try
                {
                    var first = undo.RevertAsync(source.WorkspaceId, timeout.Token);
                    var firstRestored = false;
                    Exception? primary = null;
                    try
                    {
                        await entered.Task.WaitAsync(timeout.Token);
                        if (secondUsesSequence)
                        {
                            var second = await undo.RevertBySequenceAsync(source.WorkspaceId, 17, timeout.Token);
                            Assert.IsFalse(second.Reverted);
                            Assert.AreEqual("revert-in-progress", second.Reason);
                        }
                        else Assert.IsFalse(await undo.RevertAsync(source.WorkspaceId, timeout.Token));
                        Assert.AreEqual(1, stages);
                    }
                    catch (Exception failure) { primary = failure; throw; }
                    finally
                    {
                        release.TrySetResult();
                        await CleanupFailureCollector.RunAfterFailureAsync("Concurrent inverse cleanup failed.", primary,
                            async () => { firstRestored = await first; });
                    }
                    Assert.IsTrue(firstRestored);
                    Assert.AreEqual(version + 1, WorkspaceManager.GetCurrentVersion(source.WorkspaceId));
                    Assert.IsFalse(File.Exists(path));
                    Assert.IsNull(undo.GetLastOperation(source.WorkspaceId));
                    Assert.AreEqual("unknown-sequence", (await undo.RevertBySequenceAsync(source.WorkspaceId, 17)).Reason);
                }
                catch (Exception failure) { cleanupFailure65 = failure; throw; }
                finally
                {
                    await CleanupFailureCollector.RunAfterFailureAsync("Recovery timeout cleanup failed.", cleanupFailure65,
                        CleanupFailureCollector.FromAction(timeout.Dispose));
                }
            }
            catch (Exception failure) { cleanupFailure60 = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Recovery undo cleanup failed.", cleanupFailure60,
                    CleanupFailureCollector.FromAction(undo.Dispose));
            }
        }
        catch (Exception failure) { cleanupFailure36 = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Recovery source cleanup failed.", cleanupFailure36,
                () => source.DisposeAsync());
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CommittedUndo_CleanupFailureDoesNotReselectAppliedInverse(bool bySequence)
    {
        var source = CreateIsolatedWorkspaceCopy();
        Exception? cleanupFailure100 = null;
        try
        {
            await source.LoadAsync();
            var authority = (IWorkspaceRecoveryAuthority)WorkspaceManager;
            var facade = UndoFacade(authority.AuthorizePathsAsync, async (operation, files, layout, ct) =>
                new FailingRestorationCleanup(await authority.StageRestorationAsync(operation, files, layout, ct)), authority.CommitRestorationAsync);
            var undo = new UndoService(NullLogger<UndoService>.Instance, facade);
            Exception? cleanupFailure105 = null;
            try
            {
                var path = source.GetPath(".cleanup-owned-inverse");
                await PublishPreviousExternalUndoAsync(WorkspaceManager, undo, source.WorkspaceId, path, "cleanup inverse");
                undo.CommitPendingCapture(source.WorkspaceId, 21, [path]);
                var version = WorkspaceManager.GetCurrentVersion(source.WorkspaceId);
                var failure = await Assert.ThrowsExactlyAsync<AggregateException>(async () =>
                {
                    if (bySequence) await undo.RevertBySequenceAsync(source.WorkspaceId, 21);
                    else await undo.RevertAsync(source.WorkspaceId);
                });
                Assert.IsTrue(failure.Flatten().InnerExceptions.Any(exception => exception is IOException && exception.Message == "Synthetic stage disposal failure."));
                Assert.IsFalse(File.Exists(path));
                Assert.AreEqual(version + 1, WorkspaceManager.GetCurrentVersion(source.WorkspaceId));
                Assert.IsNull(undo.GetLastOperation(source.WorkspaceId));
                Assert.IsNull(authority.GetPendingRecovery(source.WorkspaceId));
                Assert.IsFalse(await undo.RevertAsync(source.WorkspaceId));
                Assert.AreEqual("unknown-sequence", (await undo.RevertBySequenceAsync(source.WorkspaceId, 21)).Reason);
            }
            catch (Exception failure) { cleanupFailure105 = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Recovery undo cleanup failed.", cleanupFailure105,
                    CleanupFailureCollector.FromAction(undo.Dispose));
            }
        }
        catch (Exception failure) { cleanupFailure100 = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Recovery source cleanup failed.", cleanupFailure100,
                () => source.DisposeAsync());
        }
    }

    [TestMethod]
    public async Task NativeUndoPolicy_ExcludesProvenSdkConfigsAndRejectsMutableExternalDocuments()
    {
        var source = CreateIsolatedWorkspaceCopy();
        Exception? cleanupFailure127 = null;
        try
        {
            var policy = new InventoryPolicy();
            var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            WorkspaceManager? manager = null;
            Exception? cleanupFailure129 = null;
            try
            {
                manager = new WorkspaceManager(new RoslynMcp.Roslyn.Services.ForkInvocationPathPolicy(policy), NullLogger<WorkspaceManager>.Instance, new PreviewStore(),
                    watcher);
                var undo = new UndoService(NullLogger<UndoService>.Instance, manager);
                Exception? cleanupFailure131 = null;
                try
                {
                    var status = await manager.LoadAsync(source.SolutionPath, OwnedNativeLoadContext.Properties(source.SolutionPath, new Dictionary<string, string>()), CancellationToken.None);
                    var solution = manager.GetCurrentSolution(status.WorkspaceId);
                    var sdkConfigs = solution.Projects.SelectMany(project => project.AnalyzerConfigDocuments)
                        .Select(document => document.FilePath).OfType<string>()
                        .Where(path => Path.GetFileName(path).StartsWith("analysislevel_", StringComparison.Ordinal)).ToArray();
                    Assert.IsTrue(sdkConfigs.Length > 0, "The native SDK configuration must exercise the immutable-input boundary.");
                    var layout = await PhysicalGraphLayout.CreateAsync(status.WorkspaceId, source.SolutionPath, solution, policy, CancellationToken.None);
                    Assert.IsTrue(sdkConfigs.All(path => layout.ImmutableRoots.Any(root => FileSystemPath.IsStrictDescendant(root, path))));
                    var owned = source.GetPath(".sdk-policy-operation");
                    await PublishPreviousExternalUndoAsync(manager, undo, status.WorkspaceId, owned, "SDK policy operation");
                    Assert.IsTrue(await undo.RevertAsync(status.WorkspaceId));
                    Assert.IsTrue(sdkConfigs.All(path => !policy.Paths.Contains(path, FileSystemPath.Comparer)),
                        "Proven immutable SDK configs stay external and never enter the mutable copy inventory.");
                    var external = Path.Combine(Path.GetTempPath(), "outside-owned-document-" + Guid.NewGuid().ToString("N") + ".cs");
                    var authority = (IWorkspaceRecoveryAuthority)manager;
                    await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => authority.CaptureRecoveryAsync(status.WorkspaceId,
                        "external document creation", [new FileSnapshotDto(external, null)], CancellationToken.None));
                    Assert.IsFalse(File.Exists(external));
                    Assert.IsNull(authority.GetPendingRecovery(status.WorkspaceId));
                }
                catch (Exception failure) { cleanupFailure131 = failure; throw; }
                finally
                {
                    await CleanupFailureCollector.RunAfterFailureAsync("Recovery undo cleanup failed.", cleanupFailure131,
                        CleanupFailureCollector.FromAction(undo.Dispose));
                }
            }
            catch (Exception failure) { cleanupFailure129 = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Recovery manager cleanup failed.", cleanupFailure129,
                    CleanupFailureCollector.FromAction(() => { if (manager is not null) manager.Dispose(); else watcher.Dispose(); }));
            }
        }
        catch (Exception failure) { cleanupFailure127 = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Recovery source cleanup failed.", cleanupFailure127,
                () => source.DisposeAsync());
        }
    }

    [TestMethod]
    [DataRow("prepare")]
    [DataRow("begin")]
    [DataRow("publish")]
    [DataRow("abort")]
    [DataRow("recovery")]
    public async Task CapturedAuthority_ReauthorizesCurrentRequestBeforeReadingBytes(string phase)
    {
        var source = CreateIsolatedWorkspaceCopy();
        Exception? cleanupFailure161 = null;
        try
        {
            var policy = new NarrowedRequestPolicy();
            var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            WorkspaceManager? manager = null;
            Exception? cleanupFailure163 = null;
            try
            {
                manager = new WorkspaceManager(new RoslynMcp.Roslyn.Services.ForkInvocationPathPolicy(policy), NullLogger<WorkspaceManager>.Instance, new PreviewStore(),
                    watcher);
                var undo = new UndoService(NullLogger<UndoService>.Instance, manager);
                Exception? cleanupFailure165 = null;
                try
                {
                    var status = await manager.LoadAsync(source.SolutionPath, OwnedNativeLoadContext.Properties(source.SolutionPath, new Dictionary<string, string>()), CancellationToken.None);
                    var authority = (IWorkspaceRecoveryAuthority)manager;
                    var path = source.GetPath(".request-owned-bytes");
                    await File.WriteAllTextAsync(path, "entry");
                    var physical = await authority.CaptureRecoveryAsync(status.WorkspaceId, "request authorization",
                        [FileSnapshotDto.FromExistingBytes(path, await File.ReadAllBytesAsync(path))], CancellationToken.None);
                    IWorkspaceMutationLease? lease = null;
                    Exception? authorityCleanupFailure = null;
                    try
                    {
                        if (phase is "abort" or "recovery" or "publish")
                        {
                            lease = await authority.BeginMutationAsync(physical, CancellationToken.None);
                            if (phase == "publish")
                            {
                                lease.BeforeWrite(FileSnapshotDto.FromExistingBytes(path, Encoding.UTF8.GetBytes("post")));
                                await File.WriteAllTextAsync(path, "post");
                                lease.Complete([FileSnapshotDto.FromExistingBytes(path, await File.ReadAllBytesAsync(path))]);
                            }
                            if (phase == "recovery")
                            {
                                lease.BeforeWrite(FileSnapshotDto.FromExistingBytes(path, Encoding.UTF8.GetBytes("entry")));
                                var retiredLease = lease;
                                lease = null;
                                retiredLease.Dispose();
                            }
                        }
                        // A read would encounter a directory, while authorization must fail with the exact
                        // request refusal first. The capability's original grant cannot authorize this request.
                        File.Delete(path);
                        Directory.CreateDirectory(path);
                        policy.DeniedPath = path;
                        Func<Task> action = phase switch
                        {
                            "prepare" => async () => { await undo.PrepareBeforeApplyAsync(physical, CancellationToken.None); }
                            ,
                            "begin" => async () => { await authority.BeginMutationAsync(physical, CancellationToken.None); }
                            ,
                            "publish" => () => authority.EnsureOperationCurrentAsync(physical, CancellationToken.None),
                            "abort" => () => lease!.AbortAsync(CancellationToken.None),
                            "recovery" => () => authority.EnsureRecoveryCurrentAsync(physical, CancellationToken.None),
                            _ => throw new NotSupportedException()
                        };
                        var refusal = await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(action);
                        Assert.AreEqual(NarrowedRequestPolicy.Refusal, refusal.Message);
                        Assert.AreEqual(1, policy.Refusals);
                        Assert.IsTrue(Directory.Exists(path));
                    }
                    catch (Exception failure) { authorityCleanupFailure = failure; throw; }
                    finally
                    {
                        await CleanupFailureCollector.RunAfterFailureAsync("Captured authority fixture cleanup failed.", authorityCleanupFailure,
                            CleanupFailureCollector.FromAction(() => policy.DeniedPath = null),
                            CleanupFailureCollector.FromAction(() => { if (Directory.Exists(path)) Directory.Delete(path); }),
                            async () => await File.WriteAllTextAsync(path, phase == "publish" ? "post" : "entry"),
                            async () => { if (phase == "abort" && lease is not null) await lease.AbortAsync(CancellationToken.None); },
                            CleanupFailureCollector.FromAction(() => { var retiredLease = lease; lease = null; retiredLease?.Dispose(); }),
                            async () =>
                            {
                                if (authority.GetPendingRecovery(status.WorkspaceId) is not null)
                                    Assert.IsTrue(await undo.RevertAsync(status.WorkspaceId), "Restore the explicit pending operation before closing its workspace.");
                            },
                            CleanupFailureCollector.FromAction(() => manager.Close(status.WorkspaceId)));
                    }
                }
                catch (Exception failure) { cleanupFailure165 = failure; throw; }
                finally
                {
                    await CleanupFailureCollector.RunAfterFailureAsync("Recovery undo cleanup failed.", cleanupFailure165,
                        CleanupFailureCollector.FromAction(undo.Dispose));
                }
            }
            catch (Exception failure) { cleanupFailure163 = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Recovery manager cleanup failed.", cleanupFailure163,
                    CleanupFailureCollector.FromAction(() => { if (manager is not null) manager.Dispose(); else watcher.Dispose(); }));
            }
        }
        catch (Exception failure) { cleanupFailure161 = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Recovery source cleanup failed.", cleanupFailure161,
                () => source.DisposeAsync());
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CompletedMutation_RequiresExactRegisteredWriteIntentAndUniqueOwnership(bool duplicatePath)
    {
        var source = CreateIsolatedWorkspaceCopy();
        string? loadedWorkspaceId = null;
        Exception? primary = null;
        var path = source.GetPath(".completed-intent");
        var intended = FileSnapshotDto.FromExistingBytes(path, Encoding.UTF8.GetBytes("registered owned post"));
        try
        {
            loadedWorkspaceId = await source.LoadAsync();
            var authority = (IWorkspaceRecoveryAuthority)WorkspaceManager;
            var physical = await authority.CaptureRecoveryAsync(source.WorkspaceId, "intent completion", [new(path, null)], CancellationToken.None);
            var lease = await authority.BeginMutationAsync(physical, CancellationToken.None);
            Exception? cleanupFailure238 = null;
            try
            {
                lease.BeforeWrite(intended);
                await File.WriteAllBytesAsync(path, intended.OriginalBytes.ToArray());
                var supplied = duplicatePath ? new[] { intended, intended }
                    : new[] { FileSnapshotDto.FromExistingBytes(path, Encoding.UTF8.GetBytes("unregistered post")) };
                Assert.ThrowsExactly<InvalidOperationException>(() => lease.Complete(supplied));
            }
            catch (Exception failure) { cleanupFailure238 = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Recovery lease cleanup failed.", cleanupFailure238,
                    CleanupFailureCollector.FromAction(lease.Dispose));
            }
            Assert.IsNotNull(authority.GetPendingRecovery(source.WorkspaceId));
            CollectionAssert.AreEqual(intended.OriginalBytes.ToArray(), await File.ReadAllBytesAsync(path));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Completion intent fixture cleanup failed.", primary,
                async () =>
                {
                    if (loadedWorkspaceId is null || ((IWorkspaceRecoveryAuthority)WorkspaceManager).GetPendingRecovery(source.WorkspaceId) is null) return;
                    var recovery = new UndoService(NullLogger<UndoService>.Instance, WorkspaceManager);
                    Exception? cleanupFailure256 = null;
                    try
                    {
                        Assert.IsTrue(await recovery.RevertAsync(source.WorkspaceId));
                    }
                    catch (Exception failure) { cleanupFailure256 = failure; throw; }
                    finally
                    {
                        await CleanupFailureCollector.RunAfterFailureAsync("Recovery service cleanup failed.", cleanupFailure256,
                            CleanupFailureCollector.FromAction(recovery.Dispose));
                    }
                }, () => source.DisposeAsync());
        }
    }

    [TestMethod]
    [DataRow(true, false, false)]
    [DataRow(false, true, false)]
    [DataRow(false, false, true)]
    public async Task PhysicalMutationAuthority_AcceptsEquivalentValidatedWindowsPathSpellings(bool captureForward, bool intentForward, bool completeForward)
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Alternate Windows path separators are required.");
        var source = CreateIsolatedWorkspaceCopy();
        string? loadedWorkspaceId = null;
        Exception? primary = null;
        try
        {
            loadedWorkspaceId = await source.LoadAsync();
            var path = Path.GetFullPath(source.GetPath(".physical-path-spelling"));
            string Spelling(bool forward) => forward ? path.Replace('\\', '/') : path;
            var authority = (IWorkspaceRecoveryAuthority)WorkspaceManager;
            var operation = await authority.CaptureRecoveryAsync(source.WorkspaceId, "canonical physical ownership",
                [new(Spelling(captureForward), null)], CancellationToken.None);
            var bytes = Encoding.UTF8.GetBytes("one physical owned file");
            var lease = await authority.BeginMutationAsync(operation, CancellationToken.None);
            Exception? cleanupFailure280 = null;
            try
            {
                lease.BeforeWrite(FileSnapshotDto.FromExistingBytes(Spelling(intentForward), bytes));
                await File.WriteAllBytesAsync(path, bytes);
                lease.Complete([FileSnapshotDto.FromExistingBytes(Spelling(completeForward), bytes)]);
            }
            catch (Exception failure) { cleanupFailure280 = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Recovery lease cleanup failed.", cleanupFailure280,
                    CleanupFailureCollector.FromAction(lease.Dispose));
            }
            await authority.EnsureOperationCurrentAsync(operation, CancellationToken.None);
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path));
            Assert.AreEqual(path, operation.Files.Single().FilePath, "Issued ownership must use canonical physical path spelling.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Physical path spelling fixture cleanup failed.", primary,
                async () =>
                {
                    if (loadedWorkspaceId is null || ((IWorkspaceRecoveryAuthority)WorkspaceManager).GetPendingRecovery(source.WorkspaceId) is null) return;
                    var recovery = new UndoService(NullLogger<UndoService>.Instance, WorkspaceManager);
                    Exception? cleanupFailure297 = null;
                    try
                    {
                        Assert.IsTrue(await recovery.RevertAsync(source.WorkspaceId));
                    }
                    catch (Exception failure) { cleanupFailure297 = failure; throw; }
                    finally
                    {
                        await CleanupFailureCollector.RunAfterFailureAsync("Recovery service cleanup failed.", cleanupFailure297,
                            CleanupFailureCollector.FromAction(recovery.Dispose));
                    }
                }, () => source.DisposeAsync());
        }
    }

    [TestMethod]
    public async Task RecoveryCapture_RejectsDuplicatePhysicalOwnershipBeforeMutation()
    {
        var source = CreateIsolatedWorkspaceCopy();
        Exception? cleanupFailure306 = null;
        try
        {
            await source.LoadAsync();
            var path = Path.GetFullPath(source.GetPath(".duplicate-physical-ownership"));
            var authority = (IWorkspaceRecoveryAuthority)WorkspaceManager;
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => authority.CaptureRecoveryAsync(source.WorkspaceId,
                "duplicate physical ownership", [new(path, null), new(path, null)], CancellationToken.None));
            Assert.IsFalse(File.Exists(path));
            Assert.IsNull(authority.GetPendingRecovery(source.WorkspaceId));
        }
        catch (Exception failure) { cleanupFailure306 = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Recovery source cleanup failed.", cleanupFailure306,
                () => source.DisposeAsync());
        }
    }

    [TestMethod]
    [DataRow("relative.cs")]
    [DataRow("../relative.cs")]
    public void PhysicalPathGuard_StillRejectsRelativePaths(string path)
    {
        Assert.Throws<InvalidOperationException>(() => WorkspaceManager.EnsurePhysicalPath(path));
    }

    [TestMethod]
    [DataRow("unknown")]
    [DataRow("entry")]
    [DataRow("intent")]
    public async Task RetainedRecovery_ReadAuthorityRejectsUnknownBytesAfterInitialOwnershipCheck(string observedState)
    {
        var source = CreateIsolatedWorkspaceCopy();
        Exception? primary = null;
        string? workspaceId = null;
        var path = source.GetPath(".retained-read-authority");
        var intended = Encoding.UTF8.GetBytes("known owned intended post");
        var external = Encoding.UTF8.GetBytes("unknown external edit after the initial recovery check");
        try
        {
            workspaceId = await source.LoadAsync();
            var authority = (IWorkspaceRecoveryAuthority)WorkspaceManager;
            var pending = await authority.CaptureRecoveryAsync(workspaceId, "retained read ownership", [new(path, null)], CancellationToken.None);
            var lease = await authority.BeginMutationAsync(pending, CancellationToken.None);
            Exception? cleanupFailure341 = null;
            try
            {
                lease.BeforeWrite(FileSnapshotDto.FromExistingBytes(path, intended));
                await File.WriteAllBytesAsync(path, intended);
                // Disposal retains this incomplete, genuinely written operation.
            }
            catch (Exception failure) { cleanupFailure341 = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Recovery lease cleanup failed.", cleanupFailure341,
                    CleanupFailureCollector.FromAction(lease.Dispose));
            }
            Assert.AreSame(pending, authority.GetPendingRecovery(workspaceId));
            var version = WorkspaceManager.GetCurrentVersion(workspaceId);
            var authorizations = 0;
            var injected = false;
            var facade = UndoFacade(async (id, paths, ct) =>
            {
                await authority.AuthorizePathsAsync(id, paths, ct);
                if (paths.Count != 1 || !FileSystemPath.Comparer.Equals(Path.GetFullPath(paths[0]), Path.GetFullPath(path))) return;
                // RevertFromFileSnapshots authorizes its inventory once, then the
                // per-file reader authorizes again after EnsureRecoveryCurrent.
                if (++authorizations != 2) return;
                injected = true;
                if (observedState == "entry") File.Delete(path);
                else await File.WriteAllBytesAsync(path, observedState == "intent" ? intended : external, ct);
            }, authority.StageRestorationAsync, authority.CommitRestorationAsync);
            var undo = new UndoService(NullLogger<UndoService>.Instance, facade);
            Exception? cleanupFailure362 = null;
            try
            {
                Exception? refusal = null;
                var restored = false;
                try { restored = await undo.RevertAsync(workspaceId); }
                catch (Exception failure) { refusal = failure; }
                Assert.IsTrue(injected, "The controlled edit must occur at the per-file read after the first recovery ownership check.");
                if (observedState == "unknown")
                {
                    var current = await FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, CancellationToken.None);
                    Assert.IsNotNull(current, "Recovery must preserve the unknown external file instead of deleting it.");
                    CollectionAssert.AreEqual(external, current);
                    Assert.IsFalse(restored);
                    Assert.IsInstanceOfType<WorkspaceMutationOwnershipException>(refusal);
                    Assert.AreSame(pending, authority.GetPendingRecovery(workspaceId));
                    Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(workspaceId));
                    Assert.ThrowsExactly<PublicInvalidOperationException>(() => authority.EnsureMutationAllowed(workspaceId, [path]));
                }
                else
                {
                    Assert.IsNull(refusal, "Known entry and registered intended bytes remain representable recovery states.");
                    Assert.IsTrue(restored);
                    Assert.IsFalse(File.Exists(path));
                    Assert.IsNull(authority.GetPendingRecovery(workspaceId));
                    authority.EnsureMutationAllowed(workspaceId, [path]);
                }
            }
            catch (Exception failure) { cleanupFailure362 = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Recovery undo cleanup failed.", cleanupFailure362,
                    CleanupFailureCollector.FromAction(undo.Dispose));
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Retained recovery read fixture cleanup failed.", primary,
                async () =>
                {
                    if (workspaceId is null || ((IWorkspaceRecoveryAuthority)WorkspaceManager).GetPendingRecovery(workspaceId) is null) return;
                    await File.WriteAllBytesAsync(path, intended); // Explicit fixture resolution to registered owned bytes.
                    var recovery = new UndoService(NullLogger<UndoService>.Instance, WorkspaceManager);
                    Exception? cleanupFailure396 = null;
                    try
                    {
                        Assert.IsTrue(await recovery.RevertAsync(workspaceId));
                    }
                    catch (Exception failure) { cleanupFailure396 = failure; throw; }
                    finally
                    {
                        await CleanupFailureCollector.RunAfterFailureAsync("Recovery service cleanup failed.", cleanupFailure396,
                            CleanupFailureCollector.FromAction(recovery.Dispose));
                    }
                }, () => source.DisposeAsync());
        }
    }

    [TestMethod]
    public async Task VersionOnlyMutation_ExpiresPreparedPhysicalOwnership()
    {
        var source = CreateIsolatedWorkspaceCopy();
        Exception? cleanupFailure405 = null;
        try
        {
            await source.LoadAsync();
            var authority = (IWorkspaceRecoveryAuthority)WorkspaceManager;
            var physical = await authority.CaptureRecoveryAsync(source.WorkspaceId, "stale version", [], CancellationToken.None);
            WorkspaceManager.RestoreVersion(source.WorkspaceId, WorkspaceManager.GetCurrentVersion(source.WorkspaceId));
            await Assert.ThrowsExactlyAsync<WorkspaceMutationOwnershipException>(() => authority.BeginMutationAsync(physical, CancellationToken.None));
            Assert.IsNull(authority.GetPendingRecovery(source.WorkspaceId));
        }
        catch (Exception failure) { cleanupFailure405 = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Recovery source cleanup failed.", cleanupFailure405,
                () => source.DisposeAsync());
        }
    }
}
