using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class UndoWriteAuthorizationOwnershipTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize] public static void Initialize(TestContext _) => InitializeServices();
    [TestMethod]
    public async Task RetainedSourceNamespace_ChangedNativeConditionCannotPassByteOnlyAbort()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Windows removes the held empty custody directory; this control qualifies POSIX retained names.");
        var scope = await CreateAsync(NamespaceObservation.Staging, WorkspaceManager, []);
        var path = scope.GetPath(".owned-transition");
        var original = Encoding.UTF8.GetBytes("original");
        var desired = Encoding.UTF8.GetBytes("desired");
        var authority = (IWorkspaceRecoveryAuthority)WorkspaceManager;
        var retainedTransitions = new List<PhysicalSourceTransition>();
        IWorkspaceMutationLease? lease = null;
        Exception? primary = null;
        try
        {
            await File.WriteAllBytesAsync(path, original);
            var solution = WorkspaceManager.GetCurrentSolution(scope.WorkspaceId);
            var options = (Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)solution.Projects.Single().ParseOptions!;
            Assert.IsFalse(options.PreprocessorSymbolNames.Contains("CUSTODY_PRESENT"));
            var operation = await authority.CaptureRecoveryAsync(scope.WorkspaceId, "namespace effect ownership",
                [FileSnapshotDto.FromExistingBytes(path, original)], CancellationToken.None);
            lease = await authority.BeginMutationAsync(operation, CancellationToken.None);
            {
                lease.BeforeWrite(FileSnapshotDto.FromExistingBytes(path, desired));
                await ApplyObservedTransitionAsync(operation, original, desired);
                lease.BeforeWrite(FileSnapshotDto.FromExistingBytes(path, original));
                await ApplyObservedTransitionAsync(operation, desired, original);
                CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(path));
                Assert.IsTrue(operation.HasUnverifiedSourceNamespaces);
                Assert.IsTrue(Directory.GetDirectories(scope.GetPath(".roslynmcp/staging"), ".rmcp-transition-*").Length > 0);
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => lease.AbortAsync(CancellationToken.None));
            }
            var completedLease = lease;
            lease = null;
            completedLease.Dispose();
            Assert.IsNotNull(authority.GetPendingRecovery(scope.WorkspaceId));
            Assert.ThrowsExactly<PublicInvalidOperationException>(() => authority.EnsureMutationAllowed(scope.WorkspaceId, [path]));

            Task ApplyObservedTransitionAsync(PhysicalMutationRecovery operation, byte[] expected, byte[] desiredBytes)
                => PhysicalSourceTransition.ApplyAsync(path, expected, desiredBytes, PhysicalSourceTransitionMode.FreshReplacement,
                    async (source, leaf, temporary, ct) =>
                    {
                        var staging = scope.GetPath(".roslynmcp/staging");
                        await authority.AuthorizePathsAsync(scope.WorkspaceId, [staging, Path.Combine(staging, leaf)], ct);
                        var parent = PhysicalDirectoryScope.OpenRoute(staging);
                        return new PhysicalSourceCustody(source, parent, leaf, parent.AssertCurrentPath, parent.AssertCurrentPath,
                            (paths, token) => authority.AuthorizePathsAsync(scope.WorkspaceId,
                                paths.Concat([staging, Path.Combine(staging, leaf)]).Distinct(FileSystemPath.Comparer).ToArray(), token));
                    }, (paths, ct) => authority.AuthorizePathsAsync(scope.WorkspaceId, paths, ct),
                    retainedTransitions.Add, operation.RecordSourceNamespace, PhysicalSourceTransition.OpenParent, null, CancellationToken.None);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Namespace ownership fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => lease?.Dispose()),
                CleanupFailureCollector.FromAction(() => OwnedResourceCleanup.Dispose("Observed fixture custody cleanup failed.", null, retainedTransitions.ToArray())),
                async () =>
                {
                    // Explicit fixture resolution: only empty owned transition directories.
                    foreach (var directory in Directory.GetDirectories(scope.GetPath(".roslynmcp/staging"), ".rmcp-transition-*"))
                    {
                        Assert.IsTrue(FileSystemPath.IsStrictDescendant(Path.GetDirectoryName(path)!, directory));
                        Assert.AreEqual(0, Directory.GetFileSystemEntries(directory).Length);
                        Directory.Delete(directory, recursive: false);
                    }
                    if (authority.GetPendingRecovery(scope.WorkspaceId) is null) return;
                    await AssertRecoveryRevertedAsync(scope.WorkspaceId);
                }, () => scope.DisposeAsync());
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MissingStagingCustody_DoesNotChangeNativeNamespaceObservation(bool recursive)
    {
        var scope = await CreateAsync(recursive ? NamespaceObservation.MissingStagingRecursive : NamespaceObservation.MissingStaging, WorkspaceManager, []);
        var staging = scope.GetPath(".roslynmcp/staging");
        var path = scope.GetPath(".owned-transition");
        var original = Encoding.UTF8.GetBytes("original");
        var desired = Encoding.UTF8.GetBytes("desired");
        var authority = (IWorkspaceRecoveryAuthority)WorkspaceManager;
        IWorkspaceMutationLease? lease = null;
        Exception? primary = null;
        try
        {
            Assert.IsFalse(Directory.Exists(staging), "Native capture must observe an actually absent preferred custody parent.");
            await File.WriteAllBytesAsync(path, original);
            var solution = WorkspaceManager.GetCurrentSolution(scope.WorkspaceId);
            var certificate = NativeCompilerContextCertificate.Require(solution);
            Assert.AreEqual(NativeCompilerContextAvailability.Certified, certificate.Availability);
            var options = (Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)solution.Projects.Single().ParseOptions!;
            Assert.IsFalse(options.PreprocessorSymbolNames.Contains("CUSTODY_PRESENT"));
            var operation = await authority.CaptureRecoveryAsync(scope.WorkspaceId, "absent custody namespace",
                [FileSnapshotDto.FromExistingBytes(path, original)], CancellationToken.None);
            lease = await authority.BeginMutationAsync(operation, CancellationToken.None);
            lease.BeforeWrite(FileSnapshotDto.FromExistingBytes(path, desired));
            await operation.TransitionSourceAsync(path, original, desired, PhysicalSourceTransitionMode.FreshReplacement, authority, null, CancellationToken.None);
            lease.BeforeWrite(FileSnapshotDto.FromExistingBytes(path, original));
            await operation.TransitionSourceAsync(path, desired, original, PhysicalSourceTransitionMode.FreshReplacement, authority, null, CancellationToken.None);
            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(path));
            await certificate.AssertCurrentAsync(solution,
                (paths, ct) => authority.AuthorizePathsAsync(scope.WorkspaceId, paths, ct),
                (input, ct) => FileSnapshotCapture.ReadIfPresentAsync(input, PhysicalInputReader.ReadDigestAsync, ct), CancellationToken.None);
            Assert.IsFalse(Directory.Exists(staging), "Private custody must not create an observed source descendant.");
            await lease.AbortAsync(CancellationToken.None);
            Assert.IsFalse(operation.HasUnverifiedSourceNamespaces);
            Assert.IsNull(authority.GetPendingRecovery(scope.WorkspaceId));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Absent custody namespace fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => lease?.Dispose()),
                () => ResolveMissingCustodyAsync(scope, path, original, authority), () => scope.DisposeAsync());
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DesiredGraphCustody_DoesNotCreateNewlyObservedSourceNamespace(bool recursive)
    {
        var scope = await CreateAsync(NamespaceObservation.None, WorkspaceManager, []);
        var path = scope.GetPath("Ownership.csproj");
        byte[]? original = null;
        var authority = (IWorkspaceRecoveryAuthority)WorkspaceManager;
        Exception? primary = null;
        try
        {
            original = await File.ReadAllBytesAsync(path);
            var originalText = await File.ReadAllTextAsync(path);
            var desiredText = AddMissingStagingObservation(originalText, recursive);
            var desired = Encoding.UTF8.GetBytes(desiredText);
            Assert.IsFalse(Directory.Exists(scope.GetPath(".roslynmcp/staging")));
            var solution = WorkspaceManager.GetCurrentSolution(scope.WorkspaceId);
            Assert.AreEqual(NativeCompilerContextAvailability.Certified, NativeCompilerContextCertificate.Require(solution).Availability);
            var operation = await authority.CaptureRecoveryAsync(scope.WorkspaceId, "desired-only custody observer",
                [FileSnapshotDto.FromExistingBytes(path, original)], CancellationToken.None);
            var logger = new ListLogger<DocumentSetPersistenceService>();
            var result = await new DocumentSetPersistenceService(WorkspaceManager, logger).PersistMutationsAsync(
                scope.WorkspaceId, solution, [new(path, original, desired, originalText, desiredText)], operation, CancellationToken.None);
            Assert.IsTrue(result.Success, "The independently staged desired graph must remain valid through physical custody. "
                + string.Join("; ", logger.Entries.Select(entry => entry.Exception?.ToString())));
            CollectionAssert.AreEqual(desired, await File.ReadAllBytesAsync(path));
            Assert.IsFalse(Directory.Exists(scope.GetPath(".roslynmcp/staging")));
            Assert.IsNull(authority.GetPendingRecovery(scope.WorkspaceId));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Desired custody observer fixture cleanup failed.", primary,
                () => original is null ? ValueTask.CompletedTask : ResolveMissingCustodyAsync(scope, path, original, authority), () => scope.DisposeAsync());
        }
    }

    private static async ValueTask ResolveMissingCustodyAsync(IsolatedWorkspaceScope scope, string path, byte[] original,
        IWorkspaceRecoveryAuthority authority)
    {
        // Resolve only empty, fixture-owned custody names before restoring the original native condition.
        var staging = scope.GetPath(".roslynmcp/staging");
        if (Directory.Exists(staging))
        {
            Assert.IsTrue(FileSystemPath.IsStrictDescendant(scope.RootPath, staging));
            foreach (var directory in Directory.GetDirectories(staging, ".rmcp-transition-*"))
            {
                Assert.AreEqual(0, Directory.GetFileSystemEntries(directory).Length);
                Directory.Delete(directory, recursive: false);
            }
            Assert.AreEqual(0, Directory.GetFileSystemEntries(staging).Length);
            Directory.Delete(staging, recursive: false);
        }
        if (authority.GetPendingRecovery(scope.WorkspaceId) is null) return;
        await File.WriteAllBytesAsync(path, original);
        await AssertRecoveryRevertedAsync(scope.WorkspaceId);
    }

    [TestMethod]
    public async Task EveryCustodyCandidateObserved_RefusesBeforeSourceOrIssuedLeafEffects()
    {
        var leaf = ".rmcp-transition-" + Guid.NewGuid().ToString("N");
        var temporary = ".rmcp-prepared-" + Guid.NewGuid().ToString("N");
        var hypotheticalRoot = Path.Combine(TestTempRoot.Current, nameof(UndoWriteAuthorizationOwnershipTests), "future-fixture");
        var witnesses = RoslynMcp.Roslyn.Services.WorkspaceManager.SourceCustodyAnchors(hypotheticalRoot)
            .Select(anchor => Path.Combine(anchor, ".roslynmcp-custody", leaf)).ToArray();
        FileWatcherService? watcher = null;
        WorkspaceManager? manager = null;
        IWorkspaceMutationLease? lease = null;
        IsolatedWorkspaceScope? scope = null;
        PhysicalDirectoryScope? source = null;
        PhysicalSourceCustody? unexpectedCustody = null;
        Exception? primary = null;
        try
        {
            watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            manager = new WorkspaceManager(new ForkInvocationPathPolicy(new CustodyWitnessPolicy(witnesses)),
                NullLogger<WorkspaceManager>.Instance, new PreviewStore(), watcher, new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 2 });
            Assert.IsTrue(witnesses.All(path => !Directory.Exists(path)), "Every operation-specific witness starts absent.");
            scope = await CreateAsync(NamespaceObservation.None, manager, witnesses);
            var path = scope.GetPath(".owned-transition");
            var original = Encoding.UTF8.GetBytes("original");
            await File.WriteAllBytesAsync(path, original);
            var solution = manager.GetCurrentSolution(scope.WorkspaceId);
            Assert.AreEqual(NativeCompilerContextAvailability.Certified, NativeCompilerContextCertificate.Require(solution).Availability);
            var authority = (IWorkspaceRecoveryAuthority)manager;
            var operation = await authority.CaptureRecoveryAsync(scope.WorkspaceId, "all custody candidates observed",
                [FileSnapshotDto.FromExistingBytes(path, original)], CancellationToken.None);
            var probes = operation.CapturedLayout!.BuildNodes.SelectMany(node => node.PathEvidence.ConditionProbes)
                .Select(probe => probe.Path).ToHashSet(FileSystemPath.Comparer);
            Assert.IsTrue(witnesses.All(probes.Contains), "The actual native graph must enroll every issued-leaf existence observation.");
            lease = await authority.BeginMutationAsync(operation, CancellationToken.None);
            var heldSource = PhysicalDirectoryScope.OpenRoute(scope.RootPath);
            source = heldSource;
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(async () =>
            {
                unexpectedCustody = await authority.AcquireSourceCustodyAsync(operation, path, heldSource, leaf, temporary, CancellationToken.None);
            });
            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(path));
            Assert.IsTrue(witnesses.All(candidate => !Directory.Exists(candidate)));
            Assert.IsFalse(File.Exists(scope.GetPath(temporary)));
            Assert.IsFalse(operation.HasRetainedSourceTransitions);
            Assert.IsFalse(operation.HasUnverifiedSourceNamespaces);
            await lease.AbortAsync(CancellationToken.None);
            Assert.IsNull(authority.GetPendingRecovery(scope.WorkspaceId));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Observed custody candidate fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => lease?.Dispose()),
                CleanupFailureCollector.FromAction(() => unexpectedCustody?.Dispose()),
                CleanupFailureCollector.FromAction(() => source?.Dispose()),
                async () => { if (scope is not null) await scope.DisposeAsync(); },
                CleanupFailureCollector.FromAction(() => manager?.Dispose()),
                CleanupFailureCollector.FromAction(() => { if (manager is null) watcher?.Dispose(); }));
        }
    }

    private sealed class CustodyWitnessPolicy(IReadOnlyList<string> witnesses) : IWorkspacePathPolicy
    {
        private readonly OwnedTestWorkspacePathPolicy _owned = new();
        private readonly HashSet<string> _witnesses = witnesses.ToHashSet(FileSystemPath.Comparer);

        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();
                if (_witnesses.Contains(path))
                {
                    Assert.IsTrue(FileSystemPath.Comparer.Equals(Path.GetFullPath(path), PhysicalPathResolver.Resolve(path)),
                        "An exact native condition witness does not grant an aliased coordinate.");
                }
                else await _owned.ValidateAsync(workspaceId, [path], ct);
            }
        }
    }

    [TestMethod]
    public async Task ExistingStagingCustody_DoesNotChangeSourceRootDirectoryCondition()
    {
        var scope = await CreateAsync(NamespaceObservation.SourceRoot, WorkspaceManager, []);
        var path = scope.GetPath(".owned-transition");
        var original = Encoding.UTF8.GetBytes("original");
        var desired = Encoding.UTF8.GetBytes("desired");
        var authority = (IWorkspaceRecoveryAuthority)WorkspaceManager;
        IWorkspaceMutationLease? lease = null;
        Exception? primary = null;
        try
        {
            await File.WriteAllBytesAsync(path, original);
            var solution = WorkspaceManager.GetCurrentSolution(scope.WorkspaceId);
            var operation = await authority.CaptureRecoveryAsync(scope.WorkspaceId, "stable source namespace",
                [FileSnapshotDto.FromExistingBytes(path, original)], CancellationToken.None);
            lease = await authority.BeginMutationAsync(operation, CancellationToken.None);
            lease.BeforeWrite(FileSnapshotDto.FromExistingBytes(path, desired));
            await operation.TransitionSourceAsync(path, original, desired, PhysicalSourceTransitionMode.FreshReplacement, authority, null, CancellationToken.None);
            lease.BeforeWrite(FileSnapshotDto.FromExistingBytes(path, original));
            await operation.TransitionSourceAsync(path, desired, original, PhysicalSourceTransitionMode.FreshReplacement, authority, null, CancellationToken.None);
            Assert.AreEqual(0, Directory.GetDirectories(Path.GetDirectoryName(path)!, ".rmcp-transition-*").Length);
            await NativeCompilerContextCertificate.Require(solution).AssertCurrentAsync(solution,
                (paths, ct) => authority.AuthorizePathsAsync(scope.WorkspaceId, paths, ct),
                (input, ct) => FileSnapshotCapture.ReadIfPresentAsync(input, PhysicalInputReader.ReadDigestAsync, ct), CancellationToken.None);
            await lease.AbortAsync(CancellationToken.None);
            Assert.IsFalse(operation.HasUnverifiedSourceNamespaces);
            Assert.IsNull(authority.GetPendingRecovery(scope.WorkspaceId));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Stable source namespace fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => lease?.Dispose()),
                async () =>
                {
                    if (authority.GetPendingRecovery(scope.WorkspaceId) is null) return;
                    await File.WriteAllBytesAsync(path, original);
                    await AssertRecoveryRevertedAsync(scope.WorkspaceId);
                }, () => scope.DisposeAsync());
        }
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task ForwardWriteAuthorization_ExternalBytesSurvive(bool composite, bool deletion)
    {
        var scope = await CreateAsync(NamespaceObservation.None, WorkspaceManager, []);
        var path = scope.GetPath(".owned-transition");
        var original = Encoding.UTF8.GetBytes("original");
        var external = Encoding.UTF8.GetBytes("external callback bytes");
        var desired = deletion ? null : Encoding.UTF8.GetBytes("desired");
        var authority = (IWorkspaceRecoveryAuthority)WorkspaceManager;
        var armed = false;
        var injected = false;
        Exception? primary = null;
        try
        {
            await File.WriteAllBytesAsync(path, original);
            var facade = ForwardingFacade(authority, async (workspaceId, paths, ct) =>
            {
                await authority.AuthorizePathsAsync(workspaceId, paths, ct);
                if (armed && !injected && paths.Contains(path, FileSystemPath.Comparer))
                {
                    injected = true;
                    await File.WriteAllBytesAsync(path, external, ct);
                }
            }, intended => { if (FileSystemPath.Comparer.Equals(path, intended.FilePath)) armed = true; });
            Exception? refusal = null;
            string? returnedError = null;
            var persistenceLog = new ListLogger<DocumentSetPersistenceService>();
            var compositeLog = new ListLogger<CompositeApplyOrchestrator>();
            try
            {
                if (composite)
                {
                    var store = new CompositePreviewStore();
                    var token = store.Store(scope.WorkspaceId, WorkspaceManager.GetCurrentVersion(scope.WorkspaceId), "callback ownership",
                        [new CompositeFileMutation(path, deletion ? null : "desired", DeleteFile: deletion)]);
                    var result = await new CompositeApplyOrchestrator(facade, store, logger: compositeLog).ApplyCompositeAsync(token, CancellationToken.None);
                    returnedError = result.Error;
                    Assert.IsFalse(result.Success);
                }
                else
                {
                    var operation = await authority.CaptureRecoveryAsync(scope.WorkspaceId, "callback ownership",
                        [FileSnapshotDto.FromExistingBytes(path, original)], CancellationToken.None);
                    var result = await new DocumentSetPersistenceService(facade, persistenceLog).PersistMutationsAsync(
                        scope.WorkspaceId, WorkspaceManager.GetCurrentSolution(scope.WorkspaceId),
                        [new(path, original, desired, "original", deletion ? "" : "desired")], operation, CancellationToken.None);
                    Assert.IsFalse(result.Success);
                }
            }
            catch (Exception failure) when (failure is PublicInvalidOperationException or AggregateException) { refusal = failure; }
            Assert.IsTrue(injected, "The real write intention must arm the authorization callback. " + refusal
                + " Returned error: " + returnedError + " Logged failures: "
                + string.Join("; ", persistenceLog.Entries.Select(entry => entry.Exception))
                + string.Join("; ", compositeLog.Entries.Select(entry => entry.Exception)));
            CollectionAssert.AreEqual(external, await File.ReadAllBytesAsync(path));
            Assert.IsNotNull(authority.GetPendingRecovery(scope.WorkspaceId));
            Assert.ThrowsExactly<PublicInvalidOperationException>(() => authority.EnsureMutationAllowed(scope.WorkspaceId, [path]));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            armed = false;
            await CleanupFailureCollector.RunAfterFailureAsync("Forward callback fixture cleanup failed.", primary,
                async () =>
                {
                    if (authority.GetPendingRecovery(scope.WorkspaceId) is null) return;
                    await File.WriteAllBytesAsync(path, original);
                    await AssertRecoveryRevertedAsync(scope.WorkspaceId);
                }, () => scope.DisposeAsync());
        }
    }

    [TestMethod]
    public async Task PersistenceCompensation_AuthorizationMutationAfterExactReadCannotOverwriteExternalBytes()
    {
        var scope = await CreateAsync(NamespaceObservation.None, WorkspaceManager, []);
        var first = scope.GetPath(".first-transition");
        var second = scope.GetPath(".second-transition");
        var original = Encoding.UTF8.GetBytes("original");
        var desired = Encoding.UTF8.GetBytes("desired");
        var external = Encoding.UTF8.GetBytes("external during compensation authorization");
        var authority = (IWorkspaceRecoveryAuthority)WorkspaceManager;
        var armed = false;
        var injected = false;
        Exception? primary = null;
        try
        {
            await File.WriteAllBytesAsync(first, original);
            await File.WriteAllBytesAsync(second, original);
            var facade = ForwardingFacade(authority, async (workspaceId, paths, ct) =>
            {
                await authority.AuthorizePathsAsync(workspaceId, paths, ct);
                if (armed && !injected && paths.Contains(first, FileSystemPath.Comparer))
                {
                    injected = true;
                    await File.WriteAllBytesAsync(first, external, ct);
                }
            }, _ => { });
            var operation = await authority.CaptureRecoveryAsync(scope.WorkspaceId, "compensation callback ownership",
                [FileSnapshotDto.FromExistingBytes(first, original), FileSnapshotDto.FromExistingBytes(second, original)], CancellationToken.None);
            var filesystem = new CompensationFileSystem(first, second, () => armed = true);
            var service = new DocumentSetPersistenceService(facade, NullLogger.Instance, filesystem);
            await Assert.ThrowsExactlyAsync<AggregateException>(() => service.PersistMutationsAsync(scope.WorkspaceId,
                WorkspaceManager.GetCurrentSolution(scope.WorkspaceId),
                [new(first, original, desired, "original", "desired"), new(second, original, desired, "original", "desired")],
                operation, CancellationToken.None));
            Assert.IsTrue(injected, "Injection occurs only after the compensation guard captured intended-post bytes.");
            CollectionAssert.AreEqual(external, await File.ReadAllBytesAsync(first));
            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(second));
            Assert.IsNotNull(authority.GetPendingRecovery(scope.WorkspaceId));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            armed = false;
            await CleanupFailureCollector.RunAfterFailureAsync("Compensation callback fixture cleanup failed.", primary,
                async () =>
                {
                    if (authority.GetPendingRecovery(scope.WorkspaceId) is null) return;
                    await File.WriteAllBytesAsync(first, desired);
                    await AssertRecoveryRevertedAsync(scope.WorkspaceId);
                }, () => scope.DisposeAsync());
        }
    }

    private sealed class CompensationFileSystem(string first, string second, Action arm) : IDocumentSetFileSystem
    {
        public async Task<FileContentDigest> ReadDigestAsync(string path, CancellationToken ct)
            => FileContentDigest.FromBytes(await ReadAllBytesAsync(path, ct).ConfigureAwait(false))
                ?? throw new InvalidOperationException("The required file reader returned no content.");

        private bool _compensating;
        public async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct)
        {
            var bytes = await PhysicalInputReader.ReadAllBytesAsync(path, ct);
            if (_compensating && FileSystemPath.Comparer.Equals(path, first)) arm();
            return bytes;
        }
        public async Task TransitionAsync(string path, byte[]? expected, byte[]? desired,
            PhysicalMutationRecovery operation, IWorkspaceRecoveryAuthority authority, CancellationToken ct)
        {
            if (FileSystemPath.Comparer.Equals(path, second))
            {
                _compensating = true;
                throw new IOException("Controlled second-write failure before its effect.");
            }
            await operation.TransitionSourceAsync(path, expected, desired, PhysicalSourceTransitionMode.PreserveExistingObject, authority, null, ct);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InverseWriteAuthorization_ExternalBytesRemainOwnedByExternalWriter(bool replaceExisting)
    {
        var scope = await CreateAsync(NamespaceObservation.None, WorkspaceManager, []);
        var path = scope.GetPath(".owned-transition");
        var original = Encoding.UTF8.GetBytes("original pre-operation bytes");
        var current = Encoding.UTF8.GetBytes("published operation bytes");
        var external = Encoding.UTF8.GetBytes("external bytes installed by current write authorization");
        var authority = (IWorkspaceRecoveryAuthority)WorkspaceManager;
        var armed = false;
        var injected = false;
        UndoService? undo = null;
        IWorkspaceMutationLease? lease = null;
        Exception? primary = null;
        try
        {
            if (replaceExisting) await File.WriteAllBytesAsync(path, original);
            var facade = ForwardingFacade(authority,
                async (workspaceId, paths, ct) =>
                {
                    await authority.AuthorizePathsAsync(workspaceId, paths, ct);
                    if (armed && !injected && paths.Contains(path, FileSystemPath.Comparer))
                    {
                        armed = false;
                        injected = true;
                        await File.WriteAllBytesAsync(path, external, ct);
                    }
                }, intended => { if (FileSystemPath.Comparer.Equals(intended.FilePath, path)) armed = true; });
            var undoLog = new ListLogger<UndoService>();
            undo = new UndoService(undoLog, facade);
            FileSnapshotDto[] before = [replaceExisting ? FileSnapshotDto.FromExistingBytes(path, original) : new FileSnapshotDto(path, null)];
            var physical = await authority.CaptureRecoveryAsync(scope.WorkspaceId, "original inverse target", before, CancellationToken.None);
            var publication = await undo.PrepareBeforeApplyAsync(physical, CancellationToken.None);
            // Publish through the real authority; only the inverse lease arms our callback.
            lease = await authority.BeginMutationAsync(physical, CancellationToken.None);
            {
                lease.BeforeWrite(FileSnapshotDto.FromExistingBytes(path, current));
                await File.WriteAllBytesAsync(path, current);
                lease.Complete([FileSnapshotDto.FromExistingBytes(path, current)]);
            }
            var completedLease = lease;
            lease = null;
            completedLease.Dispose();
            await publication.PublishAsync(CancellationToken.None);

            Exception? failure = null;
            var reverted = false;
            try { reverted = await undo.RevertAsync(scope.WorkspaceId); }
            catch (Exception observed) { failure = observed; }
            Assert.IsTrue(injected, "The real inverse write intention must precede the injected authorization mutation. Observed failure: " + failure + " Logged failures: " + string.Join("; ", undoLog.Entries.Select(entry => entry.Exception
                + " [SdkGeneratedConfig=" + entry.Exception?.Data["SdkGeneratedConfig"]
                + "; DifferentLineIndices=" + entry.Exception?.Data["DifferentLineIndices"]
                + "; GeneratedConfigDifferenceKinds=" + entry.Exception?.Data["GeneratedConfigDifferenceKinds"] + "]")));
            Assert.IsTrue(File.Exists(path), "An inverse deletion must not delete the external writer's new bytes.");
            CollectionAssert.AreEqual(external, await File.ReadAllBytesAsync(path), "Neither inverse replacement nor compensation may overwrite the authorization callback's external bytes.");
            Assert.IsFalse(reverted);
            Assert.IsInstanceOfType<AggregateException>(failure);
            Assert.IsNotNull(authority.GetPendingRecovery(scope.WorkspaceId));
            Assert.ThrowsExactly<PublicInvalidOperationException>(() => authority.EnsureMutationAllowed(scope.WorkspaceId, [path]));

            // Explicit fixture conflict resolution to the captured inverse-entry state.
            await File.WriteAllBytesAsync(path, current);
            Assert.IsTrue(await undo.RevertAsync(scope.WorkspaceId));
            Assert.AreEqual("original inverse target", undo.GetLastOperation(scope.WorkspaceId)?.Description);
            Assert.IsTrue(await undo.RevertAsync(scope.WorkspaceId));
            Assert.IsNull(undo.GetLastOperation(scope.WorkspaceId));
            if (replaceExisting) CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(path));
            else Assert.IsFalse(File.Exists(path));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            armed = false;
            await CleanupFailureCollector.RunAfterFailureAsync("Inverse authorization fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => lease?.Dispose()),
                CleanupFailureCollector.FromAction(() => undo?.Dispose()),
                async () =>
                {
                    if (authority.GetPendingRecovery(scope.WorkspaceId) is null) return;
                    await File.WriteAllBytesAsync(path, current);
                    await AssertRecoveryRevertedAsync(scope.WorkspaceId);
                }, () => scope.DisposeAsync());
        }
    }

    private static async ValueTask AssertRecoveryRevertedAsync(string workspaceId)
    {
        UndoService? undo = null;
        Exception? primary = null;
        try
        {
            undo = new UndoService(NullLogger<UndoService>.Instance, WorkspaceManager);
            Assert.IsTrue(await undo.RevertAsync(workspaceId));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Fixture undo cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => undo?.Dispose()));
        }
    }

    private static FailClosedWorkspaceManagerStub ForwardingFacade(IWorkspaceRecoveryAuthority authority,
        Func<string, IReadOnlyList<string>, CancellationToken, Task> authorize, Action<FileSnapshotDto> arm) => new()
        {
            AuthorizePathsHandler = authorize,
            BeginMutationHandler = async (operation, ct) => new ArmingLease(await authority.BeginMutationAsync(operation, ct), arm),
            EnsureMutationAllowedHandler = authority.EnsureMutationAllowed,
            StageRestorationHandler = authority.StageRestorationAsync,
            CommitRestorationHandler = authority.CommitRestorationAsync,
            EnsurePreparedOperationCurrentHandler = authority.EnsurePreparedOperationCurrentAsync,
            EnsureOperationCurrentHandler = authority.EnsureOperationCurrentAsync,
            RestoreCapturedVersionHandler = authority.RestoreCapturedVersion,
            CommitMutationHandler = authority.CommitMutationAsync,
            CaptureRecoveryHandler = authority.CaptureRecoveryAsync,
            GetPendingRecoveryHandler = authority.GetPendingRecovery,
            RetainRecoveryHandler = authority.RetainRecovery,
            AssertKnownFileReadHandler = authority.AssertKnownFileRead,
            EnsureRecoveryCurrentHandler = authority.EnsureRecoveryCurrentAsync,
            AcquireSourceCustodyHandler = authority.AcquireSourceCustodyAsync,
            GetCurrentSolutionHandler = WorkspaceManager.GetCurrentSolution,
            GetCurrentVersionHandler = WorkspaceManager.GetCurrentVersion
        };
    private sealed class ArmingLease(IWorkspaceMutationLease inner, Action<FileSnapshotDto> arm) : IWorkspaceMutationLease
    {
        public void BeforeWrite(FileSnapshotDto intendedPost) { inner.BeforeWrite(intendedPost); arm(intendedPost); }
        public void Complete(IReadOnlyList<FileSnapshotDto> intendedPostFiles) => inner.Complete(intendedPostFiles);
        public void CancelBeforeSourceTransition(IPhysicalRestorationAuthority verification) => inner.CancelBeforeSourceTransition(verification);
        public Task AbortAsync(CancellationToken ct) => inner.AbortAsync(ct);
        public void Dispose() => inner.Dispose();
    }

    private enum NamespaceObservation { None, SourceRoot, Staging, MissingStaging, MissingStagingRecursive }
    private static async Task<IsolatedWorkspaceScope> CreateAsync(NamespaceObservation observation,
        WorkspaceManager manager, IReadOnlyList<string> custodyWitnesses)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(UndoWriteAuthorizationOwnershipTests), Guid.NewGuid().ToString("N"));
        IsolatedWorkspaceScope? scope = null;
        try
        {
            scope = CreateWorkspaceScope(root, Path.Combine(root, "Ownership.slnx"), manager);
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), "<Project />");
            await File.WriteAllTextAsync(scope.SolutionPath, "<Solution><Project Path=\"Ownership.csproj\" /></Solution>");
            var projectText = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultItems>false</EnableDefaultItems><NuGetAudit>false</NuGetAudit></PropertyGroup>
                  <ItemGroup><Compile Include="Entry.cs" /></ItemGroup>
                </Project>
                """;
            if (observation is NamespaceObservation.MissingStaging or NamespaceObservation.MissingStagingRecursive)
            {
                projectText = AddMissingStagingObservation(projectText, observation == NamespaceObservation.MissingStagingRecursive);
            }
            else if (observation != NamespaceObservation.None)
            {
                Directory.CreateDirectory(Path.Combine(root, ".roslynmcp", "staging"));
                var observedDirectory = observation == NamespaceObservation.Staging
                    ? "$(MSBuildProjectDirectory)/.roslynmcp/staging" : "$(MSBuildProjectDirectory)";
                projectText = projectText.Replace("</Project>", """
                    <PropertyGroup Condition="Exists('OBSERVED_DIRECTORY')">
                      <DefineConstants Condition="$([System.IO.Directory]::GetDirectories('OBSERVED_DIRECTORY', '.rmcp-transition-*').Length) != 0">$(DefineConstants);CUSTODY_PRESENT</DefineConstants>
                    </PropertyGroup>
                    </Project>
                    """.Replace("OBSERVED_DIRECTORY", observedDirectory, StringComparison.Ordinal), StringComparison.Ordinal);
            }
            if (custodyWitnesses.Count != 0)
            {
                var document = System.Xml.Linq.XDocument.Parse(projectText);
                foreach (var witness in custodyWitnesses)
                    document.Root!.Add(new System.Xml.Linq.XElement("PropertyGroup",
                        new System.Xml.Linq.XAttribute("Condition", "Exists('" + witness + "')"),
                        new System.Xml.Linq.XElement("DefineConstants", "$(DefineConstants);CUSTODY_PRESENT")));
                projectText = document.ToString();
            }
            await File.WriteAllTextAsync(Path.Combine(root, "Ownership.csproj"), projectText);
            await File.WriteAllTextAsync(Path.Combine(root, "Entry.cs"), "public class Entry {}");
            await RestoreWorkspaceAsync(scope);
            await scope.LoadAsync();
            return scope;
        }
        catch (Exception failure)
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Inverse authorization setup cleanup failed.", failure,
                () => scope is null ? ValueTask.CompletedTask : scope.DisposeAsync());
            throw;
        }
    }

    private static string AddMissingStagingObservation(string projectText, bool recursive)
    {
        var condition = recursive
            ? "$([System.IO.Directory]::GetDirectories('$(MSBuildProjectDirectory)', 'staging', 'System.IO.SearchOption.AllDirectories').Length) != 0"
            : "Exists('$(MSBuildProjectDirectory)/.roslynmcp/staging')";
        return projectText.Replace("</Project>",
            "<PropertyGroup Condition=\"" + condition + "\"><DefineConstants>$(DefineConstants);CUSTODY_PRESENT</DefineConstants></PropertyGroup></Project>", StringComparison.Ordinal);
    }
}
