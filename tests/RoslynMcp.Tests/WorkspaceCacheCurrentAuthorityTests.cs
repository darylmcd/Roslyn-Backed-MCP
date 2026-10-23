using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceCacheCurrentAuthorityTests
{
    [TestMethod]
    public async Task RealWarmCache_DeniesCurrentEntryBeforeHashOrNativeLoad()
    {
        await InFixtureAsync(async root =>
        {
            var entry = Path.Combine(root, "Entry.slnx");
            var reference = Path.Combine(root, "private-reference.dll");
            await File.WriteAllTextAsync(entry, "<Solution />");
            await File.WriteAllTextAsync(reference, "private cached reference bytes");
            Task Grant(IReadOnlyList<string> paths, CancellationToken ct) => Owned(root, paths, ct);
            var store = new WorkspaceCacheStore(Path.Combine(root, "cache"));
            var probe = await SeedAsync(store, entry, Path.Combine(root, "Actual.csproj"), reference, Grant);
            Assert.IsNotNull(probe.CachedEntry, "The denial must run with a real usable persisted candidate.");
            var expected = new UnauthorizedAccessException("Current client refuses this exact cache entry.");
            var policy = new ExactDenyPolicy(entry, expected);
            var loader = new StopBeforeNativeLoad(new InvalidDataException("Unexpected native entry"));            var locked = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.None);
            Exception? cleanupPrimary7 = null;
            try
            {                var manager = Manager(store, policy, loader, new RestoreStalenessDetector());
                Exception? cleanupPrimary6 = null;
                try
                {
                    if (OperatingSystem.IsWindows())
                        Assert.ThrowsExactly<IOException>(() => File.ReadAllBytes(entry), "The byte-read exclusion must actually be held.");
                    Exception? refusal = null;
                    try { await manager.LoadAsync(entry, CancellationToken.None); }
                    catch (Exception failure) { refusal = failure; }
                    Assert.AreEqual(1, policy.ProtectedChecks, "The entry hash requires the exact current grant before any byte observation.");
                    Assert.AreSame(expected, refusal, "Policy denial must escape cache best-effort catches with exact identity.");
                    Assert.AreEqual(0, loader.Calls);
                    Assert.AreEqual(0, manager.ListWorkspaces().Count);

                }
                catch (Exception failure) { cleanupPrimary6 = failure; throw; }
                finally { OwnedResourceCleanup.DisposePreservingFailure("Current cache resource cleanup failed.", cleanupPrimary6, manager); }

            }
            catch (Exception failure) { cleanupPrimary7 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Current cache resource cleanup failed.", cleanupPrimary7, locked); }
        });
    }

    [TestMethod]
    public async Task RealCandidate_ExtraUnusedReferenceIsPureCoordinateMiss()
    {
        await InFixtureAsync(async root =>
        {
            var entry = Path.Combine(root, "Entry.slnx");
            var projectPath = Path.Combine(root, "Actual.csproj");
            var reference = Path.Combine(root, "unused-private-reference.dll");
            await File.WriteAllTextAsync(entry, "<Solution />");
            await File.WriteAllTextAsync(reference, "unused cached bytes");
            Task Grant(IReadOnlyList<string> paths, CancellationToken ct) => Owned(root, paths, ct);
            var store = new WorkspaceCacheStore(Path.Combine(root, "cache"));
            var probe = await SeedAsync(store, entry, projectPath, reference, Grant);            var workspace = new AdhocWorkspace();
            Exception? cleanupPrimary5 = null;
            try
            {
                var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "Actual", "Actual", LanguageNames.CSharp, filePath: projectPath));
                var unusedChecks = 0;
                Task ActualGrant(IReadOnlyList<string> paths, CancellationToken ct)
                {
                    if (paths.Contains(reference, FileSystemPath.Comparer)) { unusedChecks++; throw new UnauthorizedAccessException("Unused cache coordinates must not request grants."); }
                    return Grant(paths, ct);
                }            var locked = new FileStream(reference, FileMode.Open, FileAccess.Read, FileShare.None);
                Exception? cleanupPrimary4 = null;
                try
                {
                    var coordinator = new WorkspaceCacheCoordinator(store, NullLogger.Instance);
                    var result = await coordinator.ResolveAndWriteAsync(project.Solution, probe, ActualGrant, CancellationToken.None);
                    Assert.IsNotNull(result);
                    Assert.IsFalse(result.CacheHit, "An unused extra coordinate is a miss despite matching graph and old timestamps.");
                    Assert.AreEqual(0, unusedChecks, "An unused persisted coordinate must neither demand authority nor initiate a physical probe.");
                    var key = new WorkspaceCacheKey(probe.SolutionHash, probe.SdkVersion, WorkspaceCacheCoordinator.ComputeMsbuildGraphHash(WorkspaceCacheCoordinator.BuildCachedGraphFromSolution(project.Solution)));
                    var replacement = await store.TryGetAsync(key, CancellationToken.None);
                    Assert.IsNotNull(replacement);
                    Assert.AreEqual(0, replacement.MetadataReferences.SelectMany(item => item.References).Count());

                }
                catch (Exception failure) { cleanupPrimary4 = failure; throw; }
                finally { OwnedResourceCleanup.DisposePreservingFailure("Current cache resource cleanup failed.", cleanupPrimary4, locked); }

            }
            catch (Exception failure) { cleanupPrimary5 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Current cache resource cleanup failed.", cleanupPrimary5, workspace); }
        });
    }

    [TestMethod]
    public async Task MatchingSelectedReference_DenialEscapesBeforeMetadataObservationOrWrite()
    {
        await InFixtureAsync(async root =>
        {
            var entry = Path.Combine(root, "Entry.slnx");
            var projectPath = Path.Combine(root, "Actual.csproj");
            var reference = Path.Combine(root, "actual-reference.dll");
            await File.WriteAllTextAsync(entry, "<Solution />");
            var bytes = await File.ReadAllBytesAsync(typeof(object).Assembly.Location);
            await File.WriteAllBytesAsync(reference, bytes);
            Task Grant(IReadOnlyList<string> paths, CancellationToken ct) => Owned(root, paths, ct);
            var store = new WorkspaceCacheStore(Path.Combine(root, "cache"));
            var probe = await SeedAsync(store, entry, projectPath, reference, Grant);            var workspace = new AdhocWorkspace();
            Exception? cleanupPrimary3 = null;
            try
            {
                var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "Actual", "Actual", LanguageNames.CSharp,
                    filePath: projectPath, metadataReferences: [MetadataReference.CreateFromImage(ImmutableArray.CreateRange(bytes), filePath: reference)]));
                var expected = new UnauthorizedAccessException("Current client denies the actual selected reference.");
                var policy = new ExactDenyPolicy(reference, expected);            var locked = new FileStream(reference, FileMode.Open, FileAccess.Read, FileShare.None);
                Exception? cleanupPrimary2 = null;
                try
                {
                    var observed = await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => new WorkspaceCacheCoordinator(store, NullLogger.Instance)
                        .ResolveAndWriteAsync(project.Solution, probe, (paths, ct) => policy.ValidateAsync("selected-cache", paths, ct), CancellationToken.None));
                    Assert.AreSame(expected, observed);
                    Assert.AreEqual(1, policy.ProtectedChecks);
                    var key = new WorkspaceCacheKey(probe.SolutionHash, probe.SdkVersion, WorkspaceCacheCoordinator.ComputeMsbuildGraphHash(probe.CachedEntry!.ProjectGraph));
                    var retained = await store.TryGetAsync(key, CancellationToken.None);
                    Assert.IsNotNull(retained);
                    Assert.AreEqual(probe.CachedEntry.CapturedAtUtc, retained.CapturedAtUtc, "Denied metadata authority must not refresh the persisted entry.");

                }
                catch (Exception failure) { cleanupPrimary2 = failure; throw; }
                finally { OwnedResourceCleanup.DisposePreservingFailure("Current cache resource cleanup failed.", cleanupPrimary2, locked); }

            }
            catch (Exception failure) { cleanupPrimary3 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Current cache resource cleanup failed.", cleanupPrimary3, workspace); }
        });
    }

    [TestMethod]
    public async Task UnverifiedWarmCandidate_DoesNotSkipPreloadRestoreWait()
    {
        await InFixtureAsync(async root =>
        {
            var entry = Path.Combine(root, "Entry.slnx");
            var reference = Path.Combine(root, "unused-private-reference.dll");
            await File.WriteAllTextAsync(entry, "<Solution />");
            await File.WriteAllTextAsync(reference, "unused cached bytes");
            Task Grant(IReadOnlyList<string> paths, CancellationToken ct) => Owned(root, paths, ct);
            var store = new WorkspaceCacheStore(Path.Combine(root, "cache"));
            _ = await SeedAsync(store, entry, Path.Combine(root, "Actual.csproj"), reference, Grant);
            var policy = new ExactDenyPolicy(reference, new UnauthorizedAccessException("An unverified cached coordinate must not be observed."));
            var expected = new InvalidDataException("Intentional stop at native load boundary");
            var loader = new StopBeforeNativeLoad(expected);
            var wait = new ObserveRestoreWait();            var manager = Manager(store, policy, loader, wait);
            Exception? cleanupPrimary1 = null;
            try
            {
                Exception? observed = null;
                try { await manager.LoadAsync(entry, CancellationToken.None); }
                catch (Exception failure) { observed = failure; }
                Assert.AreSame(expected, observed);
                Assert.AreEqual(1, wait.Calls, "Persisted metadata is unqualified before native load and cannot skip the stability wait.");
                Assert.AreEqual(1, loader.Calls);
                Assert.AreEqual(0, policy.ProtectedChecks);
                Assert.AreEqual(0, manager.ListWorkspaces().Count);

            }
            catch (Exception failure) { cleanupPrimary1 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Current cache resource cleanup failed.", cleanupPrimary1, manager); }
        });
    }

    private static WorkspaceManager Manager(IWorkspaceCacheStore store, IWorkspacePathPolicy policy, WorkspaceSessionLoader loader, RestoreStalenessDetector detector)
    {
        var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
        try
        {
            return new WorkspaceManager(new ForkInvocationPathPolicy(policy), NullLogger<WorkspaceManager>.Instance, new PreviewStore(),
                watcher, new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 1, RestoreRaceWaitMs = 1 }, store, loader, detector);
        }
        catch (Exception failure) { OwnedResourceCleanup.Dispose("Current cache manager construction cleanup failed.", failure, watcher); throw; }
    }

    private static async Task<WorkspaceCacheProbe> SeedAsync(WorkspaceCacheStore store, string entry, string project, string reference,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorize)
    {
        var graph = new[] { new CachedProjectGraphNode(project, []) };
        var hash = await WorkspaceCacheCoordinator.ComputeSolutionContentHashAsync(entry, authorize, CancellationToken.None);
        Assert.IsNotNull(hash);
        var sdk = WorkspaceCacheCoordinator.ResolveSdkVersionForCacheKey();
        Assert.IsTrue(await store.PutAsync(new(hash, sdk, WorkspaceCacheCoordinator.ComputeMsbuildGraphHash(graph)),
            new(graph, [new CachedProjectMetadataReferences(project, [new CachedMetadataReference(reference, File.GetLastWriteTimeUtc(reference))])], DateTime.UtcNow), CancellationToken.None));
        var probe = await new WorkspaceCacheCoordinator(store, NullLogger.Instance).TryProbeAsync(entry, authorize, CancellationToken.None);
        Assert.IsNotNull(probe?.CachedEntry);
        return probe;
    }

    private static Task Owned(string root, IReadOnlyList<string> paths, CancellationToken ct) =>
        new OwnedTestWorkspacePathPolicy([root]).ValidateAsync("cache-current", paths, ct);

    private static async Task InFixtureAsync(Func<string, Task> test)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(WorkspaceCacheCurrentAuthorityTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try { Directory.CreateDirectory(root); await test(root); }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Current cache authority fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private sealed class ExactDenyPolicy(string path, UnauthorizedAccessException refusal) : IWorkspacePathPolicy
    {
        internal int ProtectedChecks { get; private set; }
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (paths.Contains(path, FileSystemPath.Comparer)) { ProtectedChecks++; throw refusal; }
            return Task.CompletedTask;
        }
    }

    private sealed class ObserveRestoreWait : RestoreStalenessDetector
    {
        internal int Calls { get; private set; }
        public override Task WaitForStableRestoreArtifactsAsync(string path, IReadOnlyDictionary<string, string> globals,
            Func<IReadOnlyList<string>, CancellationToken, Task> authorize, int cap, ILogger logger, CancellationToken ct)
        { Calls++; return Task.CompletedTask; }
    }

    private sealed class StopBeforeNativeLoad(InvalidDataException refusal) : WorkspaceSessionLoader
    {
        internal int Calls { get; private set; }
        public override Task<(MSBuildWorkspace Workspace, AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease Lease, NativeCompilerContextCertificate Context, NativeSourceAdmission.SourceAdmissionReceipt? SourceAdmission)>
            CreateAndOpenAsync(string id, string path, IDictionary<string, string>? globals, IWorkspacePathPolicy policy,
                WorkspaceDiagnosticsSink diagnostics, ILogger logger, SupplementalPackageReadAuthority packages, NativeSourceAdmission? sourceAdmission, CancellationToken ct)
        { Calls++; throw refusal; }
    }
}
